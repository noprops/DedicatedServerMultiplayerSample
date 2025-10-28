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
        /// Submitted (or fallback) hands tracked per client identifier for the current round.
        /// </summary>
        private readonly Dictionary<ulong, Hand?> _choices = new();
        private TaskCompletionSource<bool> _allChoicesSubmitted;

        /// <summary>
        /// Cancellation token that aborts the active round upon shutdown.
        /// </summary>
        /// <summary>
        /// Game manager used to query display names and trigger shutdown.
        /// </summary>
        private ServerGameManager _gameManager;

        /// <summary>
        /// Attaches server-side callbacks and prepares for incoming participants.
        /// </summary>
        partial void OnServerSpawn()
        {
            _gameManager = ServerSingleton.Instance?.GameManager;
            if (_gameManager == null)
            {
                Debug.LogError("[NetworkGame] No ServerGameManager");
                return;
            }

            _gameManager.AddAllClientsConnected(HandleAllClientsConnected);
            _gameManager.AddShutdownRequested(HandleShutdownRequested);

        }

        /// <summary>
        /// Removes server-side callbacks and cleans round state on despawn.
        /// </summary>
        partial void OnServerDespawn()
        {
            CleanupRound();


            if (_gameManager != null)
            {
                _gameManager.RemoveAllClientsConnected(HandleAllClientsConnected);
                _gameManager.RemoveShutdownRequested(HandleShutdownRequested);
                _gameManager = null;
            }
        }

        /// <summary>
        /// Entry point once the required clients have connected; begins the round if possible.
        /// </summary>
        private void HandleAllClientsConnected(ulong[] clientIds)
        {
            if (_allChoicesSubmitted != null)
            {
                return;
            }

            if (clientIds == null || clientIds.Length == 0)
            {
                _gameManager?.RequestShutdown(ShutdownKind.StartTimeout, "No participants connected", 0f);
                return;
            }

            SetPlayerSlots(clientIds);
            RunRoundFlowAsync();
        }

        /// <summary>
        /// Reacts to shutdown requests issued by the game manager.
        /// </summary>
        private void HandleShutdownRequested(ShutdownKind kind, string reason)
        {
            if (kind != ShutdownKind.Normal)
            {
                _allChoicesSubmitted?.TrySetCanceled();
            }
        }

        /// <summary>
        /// Assigns player slots for the round, padding any missing entries with CPU placeholders and resolving names.
        /// </summary>
        private void SetPlayerSlots(IReadOnlyList<ulong> connectedClientIds)
        {
            Array.Clear(_clientIds, 0, _clientIds.Length);
            _playerNames.Clear();
            _choices.Clear();

            var assigned = 0;

            for (; assigned < connectedClientIds.Count && assigned < RequiredGamePlayers; assigned++)
            {
                ulong clientId = connectedClientIds[assigned];
                _clientIds[assigned] = clientId;
                _playerNames[clientId] = ResolveDisplayName(clientId);
                _choices[clientId] = null;
            }

            var cpuId = CpuPlayerBaseId;
            for (; assigned < RequiredGamePlayers; assigned++)
            {
                _clientIds[assigned] = cpuId;
                _playerNames[cpuId] = "CPU";
                _choices[cpuId] = null;
                cpuId++;
            }
        }

        private async void RunRoundFlowAsync()
        {
            CleanupRound();

            _allChoicesSubmitted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BroadcastRoundStart();
            SeedCpuChoices();

            try
            {
                var readyTask = _allChoicesSubmitted.Task;
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(RoundTimeoutSeconds));
                var completedTask = await Task.WhenAny(readyTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == timeoutTask)
                {
                    FillMissingChoices();
                    _allChoicesSubmitted.TrySetResult(true);
                }

                await readyTask.ConfigureAwait(false);

                var hand0 = ResolveChoice(_clientIds[0]);
                var hand1 = ResolveChoice(_clientIds[1]);
                var result = RockPaperScissorsGameLogic.Resolve(_clientIds[0], _clientIds[1], hand0, hand1);

                NotifyRoundResult(result);
                _gameManager?.RequestShutdown(ShutdownKind.Normal, "Game completed");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkGame] Fatal error: {e.Message}");
                _gameManager?.RequestShutdown(ShutdownKind.Error, e.Message, 5f);
            }
            finally
            {
                CleanupRound();
            }
        }

        /// <summary>
        /// Releases round resources and cancels outstanding waiters.
        /// </summary>
        private void CleanupRound()
        {
            _allChoicesSubmitted?.TrySetCanceled();
            _allChoicesSubmitted = null;

            if (_choices.Count > 0)
            {
                _choices.Keys.ToList().ForEach(id => _choices[id] = null);
            }
        }

        /// <summary>
        /// Preemptively supplies hands for CPU players so the server does not wait on them.
        /// </summary>
        private void SeedCpuChoices()
        {
            foreach (var clientId in _clientIds)
            {
                if (IsCpuId(clientId))
                {
                    _choices[clientId] = HandExtensions.RandomHand();
                }
                else if (!_choices.ContainsKey(clientId))
                {
                    _choices[clientId] = null;
                }
            }
            TrySetChoicesReady();
        }

        /// <summary>
        /// Sends the round-start notification (names included) to every non-CPU participant.
        /// </summary>
        private void BroadcastRoundStart()
        {
            foreach (var clientId in _clientIds)
            {
                if (IsCpuId(clientId))
                {
                    continue;
                }

                var opponentId = GetOpponentId(clientId);
                if (!_playerNames.TryGetValue(clientId, out var myName) ||
                    !_playerNames.TryGetValue(opponentId, out var opponentName))
                {
                    continue;
                }

                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
                {
                    continue;
                }

                var targetParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new[] { clientId }
                    }
                };

                RoundStartedClientRpc(myName, opponentName, targetParams);
            }
        }

        /// <summary>
        /// Ensures any missing hands are filled (for timeouts) so the round can resolve.
        /// </summary>
        private void FillMissingChoices()
        {
            foreach (var clientId in _clientIds)
            {
                if (!_choices.TryGetValue(clientId, out var choice) || !choice.HasValue)
                {
                    _choices[clientId] = HandExtensions.RandomHand();
                }
            }
        }

        /// <summary>
        /// Converts a nullable choice into a concrete hand, assigning a random fallback when needed.
        /// </summary>
        private Hand ResolveChoice(ulong clientId)
        {
            return _choices.TryGetValue(clientId, out var choice) && choice.HasValue
                ? choice.Value
                : HandExtensions.RandomHand();
        }

        /// <summary>
        /// Completes the round waiter when every slot has submitted a hand.
        /// </summary>
        private void TrySetChoicesReady()
        {
            if (_allChoicesSubmitted == null)
            {
                return;
            }

            foreach (var clientId in _clientIds)
            {
                if (!_choices.TryGetValue(clientId, out var choice) || !choice.HasValue)
                {
                    return;
                }
            }

            _allChoicesSubmitted.TrySetResult(true);
        }

        /// <summary>
        /// Delivers the resolved result to each human player from their own perspective.
        /// </summary>
        private void NotifyRoundResult(RpsResult result)
        {
            foreach (var clientId in _clientIds)
            {
                if (IsCpuId(clientId))
                {
                    continue;
                }

                var isFirst = clientId == _clientIds[0];
                var myOutcome = isFirst ? result.Player1Outcome : result.Player2Outcome;
                var myHand = isFirst ? result.Player1Hand : result.Player2Hand;
                var opponentHand = isFirst ? result.Player2Hand : result.Player1Hand;

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
        /// Records the submitted hand for the matching participant, ignoring CPUs.
        /// </summary>
        partial void HandleSubmitChoice(ulong clientId, Hand choice)
        {
            if (_allChoicesSubmitted == null)
            {
                return;
            }

            if (IsCpuId(clientId) || Array.IndexOf(_clientIds, clientId) < 0)
            {
                return;
            }

            if (_choices.TryGetValue(clientId, out var existing) && existing.HasValue)
            {
                return;
            }

            _choices[clientId] = choice;
            TrySetChoicesReady();
        }

        /// <summary>
        /// Resolves a friendly display name for the supplied identifier.
        /// </summary>
        private ulong GetOpponentId(ulong clientId)
        {
            foreach (var otherId in _clientIds)
            {
                if (otherId != clientId)
                {
                    return otherId;
                }
            }

            return clientId;
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
