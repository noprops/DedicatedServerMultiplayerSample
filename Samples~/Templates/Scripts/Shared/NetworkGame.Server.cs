#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using DedicatedServerMultiplayerSample.Server.Bootstrap;
using DedicatedServerMultiplayerSample.Server.Core;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public partial class NetworkGame
    {
        private const float RoundTimeoutSeconds = 30f;

        /// <summary>
        /// Netcode client identifiers mapped by game slot (human or CPU).
        /// </summary>
        private readonly ulong[] _clientIds = new ulong[RequiredGamePlayers];
        /// <summary>
        /// Display names resolved per client identifier for the current round.
        /// </summary>
        private readonly Dictionary<ulong, string> _playerNames = new();
        /// <summary>
        /// Round logic that coordinates submissions and computes the result.
        /// </summary>
        private RockPaperScissorsGameLogic _roundLogic;

        /// <summary>
        /// Game manager used to query display names and trigger shutdown.
        /// </summary>
        private ServerGameManager _gameManager;

        /// <summary>
        /// Attaches server-side callbacks and prepares for incoming clients.
        /// </summary>
        partial void OnServerSpawn()
        {
            _gameManager = ServerSingleton.Instance?.GameManager;
            if (_gameManager == null)
            {
                Debug.LogError("[NetworkGame] No ServerGameManager");
                return;
            }

            _ = RunRoundAsync();
        }

        /// <summary>
        /// Removes server-side callbacks and cleans round state on despawn.
        /// </summary>
        partial void OnServerDespawn()
        {
            if (_gameManager != null)
            {
                _gameManager = null;
            }
        }

        /// <summary>
        /// Orchestrates a single round: wait for clients, gather inputs, resolve, and trigger shutdown.
        /// </summary>
        private async Task RunRoundAsync()
        {
            try
            {
                var (connected, connectedIds) = await WaitForAllClientsConnectedAsync();
                if (!connected || connectedIds == null || connectedIds.Length == 0)
                {
                    Debug.LogWarning("[NetworkGame] Failed to gather required clients.");
                    RequestClientDisconnectClientRpc("Failed to start the game.");
                    _gameManager?.RequestShutdown(ShutdownKind.StartTimeout, "Clients did not join in time", 0f);
                    return;
                }

                SetPlayerSlots(connectedIds);
                BroadcastRoundStart();

                _roundLogic = new RockPaperScissorsGameLogic(
                    _clientIds,
                    TimeSpan.FromSeconds(RoundTimeoutSeconds));

                foreach (var id in _clientIds)
                {
                    if (IsCpuId(id))
                    {
                        _roundLogic.SubmitHand(id, HandExtensions.RandomHand());
                    }
                }

                var result = await _roundLogic.RunAsync();

                BroadcastRoundResult(result);
                _gameManager?.RequestShutdown(ShutdownKind.Normal, "Game completed");
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested elsewhere; nothing additional to do.
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkGame] Unexpected error: {e.Message}");
                _gameManager?.RequestShutdown(ShutdownKind.Error, e.Message, 5f);
            }
            finally
            {
                _roundLogic = null;
            }
        }

        /// <summary>
        /// Waits until the required clients are connected.
        /// </summary>
        private async Task<(bool ok, ulong[] ids)> WaitForAllClientsConnectedAsync(CancellationToken ct = default)
        {
            if (_gameManager == null)
            {
                return (false, Array.Empty<ulong>());
            }

            // All expected clients are already connected.
            if (_gameManager.AreAllClientsConnected)
            {
                return (true, _gameManager.ConnectedClientSnapshot.ToArray());
            }

            ulong[] payload = Array.Empty<ulong>();
            using var awaiter = new SimpleSignalAwaiter(ct);

            void Handler(ulong[] ids)
            {
                payload = ids;
                awaiter.OnSignal();
            }

            _gameManager.AllClientsConnected += Handler;

            try
            {
                var signalled = await awaiter.WaitAsync(ct);
                return signalled
                    ? (true, payload ?? Array.Empty<ulong>())
                    : (false, Array.Empty<ulong>());
            }
            finally
            {
                _gameManager.AllClientsConnected -= Handler;
            }
        }

        /// <summary>
        /// Assigns player slots for the round, padding any missing entries with CPU placeholders and resolving names.
        /// </summary>
        private void SetPlayerSlots(IReadOnlyList<ulong> connectedClientIds)
        {
            Array.Clear(_clientIds, 0, _clientIds.Length);
            _playerNames.Clear();

            var assigned = 0;

            for (; assigned < connectedClientIds.Count && assigned < RequiredGamePlayers; assigned++)
            {
                ulong clientId = connectedClientIds[assigned];
                _clientIds[assigned] = clientId;
                _playerNames[clientId] = ResolveDisplayName(clientId);
            }

            // Fill remaining slots with CPU clients when not enough players joined.
            var cpuId = CpuPlayerBaseId;
            for (; assigned < RequiredGamePlayers; assigned++)
            {
                _clientIds[assigned] = cpuId;
                _playerNames[cpuId] = "CPU";
                cpuId++;
            }
        }

        /// <summary>
        /// Notifies all clients that the round has started, sending both client identifiers and names.
        /// </summary>
        private void BroadcastRoundStart()
        {
            if (NetworkManager.Singleton == null || _clientIds.Length < RequiredGamePlayers)
            {
                return;
            }

            var player1Id = _clientIds[0];
            var player2Id = _clientIds[1];

            if (!_playerNames.TryGetValue(player1Id, out var player1Name))
            {
                player1Name = ResolveDisplayName(player1Id);
            }

            if (!_playerNames.TryGetValue(player2Id, out var player2Name))
            {
                player2Name = ResolveDisplayName(player2Id);
            }

            // Notify every connected client that the round has started.
            RoundStartedClientRpc(player1Id, player2Id, player1Name, player2Name);
        }

        /// <summary>
        /// Sends each connected player their personal round outcome and hand information.
        /// </summary>
        private void BroadcastRoundResult(RpsResult result)
        {
            foreach (var clientId in _clientIds)
            {
                // Skip CPU slots; there is no client to notify.
                if (IsCpuId(clientId))
                {
                    continue;
                }

                var isPlayerOne = clientId == result.Player1Id;
                var myHand = isPlayerOne ? result.Player1Hand : result.Player2Hand;
                var opponentHand = isPlayerOne ? result.Player2Hand : result.Player1Hand;
                var myOutcome = isPlayerOne ? result.Player1Outcome : result.Player2Outcome;

                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
                {
                    continue;
                }

                var target = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new[] { clientId }
                    }
                };

                RoundEndedClientRpc(myOutcome, myHand, opponentHand, target);
            }
        }

        /// <summary>
        /// Records the submitted hand for the matching client, ignoring CPUs.
        /// </summary>
        partial void HandleSubmitChoice(ulong clientId, Hand choice)
        {
            if (IsCpuId(clientId) || Array.IndexOf(_clientIds, clientId) < 0)
            {
                return;
            }

            if (choice == Hand.None)
            {
                return;
            }

            _roundLogic?.SubmitHand(clientId, choice);
        }

        private string ResolveDisplayName(ulong clientId)
        {
            if (IsCpuId(clientId))
            {
                return "CPU";
            }

            if (_gameManager != null && _gameManager.TryGetPlayerDisplayName(clientId, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return $"Player{clientId}";
        }

        private static bool IsCpuId(ulong clientId) => clientId >= CpuPlayerBaseId;
    }
}
#endif
