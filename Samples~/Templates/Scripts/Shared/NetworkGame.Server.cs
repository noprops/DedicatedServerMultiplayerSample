#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using DedicatedServerMultiplayerSample.Server.Bootstrap;
using DedicatedServerMultiplayerSample.Server.Core;
using DedicatedServerMultiplayerSample.Samples.Client.UI;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public partial class NetworkGame
    {
        private const float RoundTimeoutSeconds = 30f;

        private ulong _player1Id;
        private ulong _player2Id;
        private string _player1Name = string.Empty;
        private string _player2Name = string.Empty;
        private bool _player1IsCpu;
        private bool _player2IsCpu;

        private TaskCompletionSource<Hand?> _player1HandTcs;
        private TaskCompletionSource<Hand?> _player2HandTcs;
        private RockPaperScissorsGameLogic _logic;
        private CancellationTokenSource _roundCts;

        private ServerGameManager _gameManager;

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

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        partial void OnServerDespawn()
        {
            CleanupRound();

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            if (_gameManager != null)
            {
                _gameManager.RemoveAllClientsConnected(HandleAllClientsConnected);
                _gameManager.RemoveShutdownRequested(HandleShutdownRequested);
                _gameManager = null;
            }
        }

        private void HandleAllClientsConnected(ulong[] clientIds)
        {
            if (_roundCts != null)
            {
                return;
            }

            if (clientIds == null || clientIds.Length == 0)
            {
                _gameManager?.RequestShutdown(ShutdownKind.StartTimeout, "No participants connected", 0f);
                return;
            }

            var participants = PrepareParticipants(clientIds);
            ConfigureParticipants(participants[0], participants[1]);
            BeginRound();
        }

        private void HandleShutdownRequested(ShutdownKind kind, string reason)
        {
            if (kind != ShutdownKind.Normal)
            {
                _roundCts?.Cancel();
            }
        }

        private ulong[] PrepareParticipants(IReadOnlyList<ulong> clientIds)
        {
            var participants = new List<ulong>(Math.Max(clientIds.Count, RequiredGamePlayers));
            for (var i = 0; i < clientIds.Count && participants.Count < RequiredGamePlayers; i++)
            {
                participants.Add(clientIds[i]);
            }

            var existing = new HashSet<ulong>(participants);
            while (participants.Count < RequiredGamePlayers)
            {
                var cpuId = NextCpuId(existing);
                participants.Add(cpuId);
                existing.Add(cpuId);
            }

            return participants.ToArray();
        }

        private static ulong NextCpuId(HashSet<ulong> existing)
        {
            var candidate = CpuPlayerBaseId;
            while (existing.Contains(candidate))
            {
                candidate++;
            }

            return candidate;
        }

        private void ConfigureParticipants(ulong player1Id, ulong player2Id)
        {
            _player1Id = player1Id;
            _player2Id = player2Id;
            _player1IsCpu = IsCpuId(player1Id);
            _player2IsCpu = IsCpuId(player2Id);
            _player1Name = ResolveDisplayName(player1Id);
            _player2Name = ResolveDisplayName(player2Id);
        }

        /// <summary>
        /// Seeds the round state and begins waiting for player submissions.
        /// </summary>
        private void BeginRound()
        {
            CleanupRound();

            _roundCts = new CancellationTokenSource();
            _player1HandTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _player2HandTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _logic = new RockPaperScissorsGameLogic();

            if (_player1IsCpu)
            {
                _player1HandTcs.TrySetResult(HandExtensions.RandomHand());
            }

            if (_player2IsCpu)
            {
                _player2HandTcs.TrySetResult(HandExtensions.RandomHand());
            }

            NotifyChoicePanels();
            _ = RunRoundAsync(_roundCts.Token);
       }

        /// <summary>
        /// Issues client RPCs so each human player sees the choice UI with the correct names.
        /// </summary>
        private void NotifyChoicePanels()
        {
            if (!_player1IsCpu)
            {
                SendChoicePanel(_player1Id, _player1Name, _player2Name);
            }

            if (!_player2IsCpu)
            {
                SendChoicePanel(_player2Id, _player2Name, _player1Name);
            }
        }

        /// <summary>
        /// Sends a single targeted choice-panel RPC.
        /// </summary>
        private void SendChoicePanel(ulong targetClientId, string myName, string yourName)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.ContainsKey(targetClientId))
            {
                return;
            }

            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { targetClientId }
                }
            };

            ShowChoicePanelClientRpc(myName, yourName, target);
        }

        /// <summary>
        /// Waits for both hands (with timeout) and reports the outcome back to participants.
        /// </summary>
        private async Task RunRoundAsync(CancellationToken ct)
        {
            try
            {
                var result = await _logic.RunRoundAsync(
                    _player1Id,
                    _player2Id,
                    TimeSpan.FromSeconds(RoundTimeoutSeconds),
                    _player1HandTcs.Task,
                    _player2HandTcs.Task,
                    ct).ConfigureAwait(false);

                NotifyRoundResult(result);
                _gameManager?.RequestShutdown(ShutdownKind.Normal, "Game completed");
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested.
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
        /// Delivers the resolved result to each human player from their own perspective.
        /// </summary>
        private void NotifyRoundResult(RpsResult result)
        {
            if (!_player1IsCpu)
            {
                SendRoundResult(
                    _player1Id,
                    result.Player1Outcome,
                    result.Player1Hand,
                    result.Player2Hand);
            }

            if (!_player2IsCpu)
            {
                SendRoundResult(
                    _player2Id,
                    result.Player2Outcome,
                    result.Player2Hand,
                    result.Player1Hand);
            }
        }

        /// <summary>
        /// Sends the result RPC to a single participant.
        /// </summary>
        private void SendRoundResult(ulong targetClientId, RoundOutcome myOutcome, Hand myHand, Hand opponentHand)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.ContainsKey(targetClientId))
            {
                return;
            }

            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { targetClientId }
                }
            };

            ShowResultClientRpc(myOutcome, myHand, opponentHand, target);
        }

        partial void HandleSubmitChoice(ulong clientId, Hand choice)
        {
            if (clientId == _player1Id && !_player1IsCpu)
            {
                _player1HandTcs?.TrySetResult(choice);
            }
            else if (clientId == _player2Id && !_player2IsCpu)
            {
                _player2HandTcs?.TrySetResult(choice);
            }
        }

        /// <summary>
        /// Replaces a disconnected human participant with an auto-hand so the round can finish.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (clientId == _player1Id && !_player1IsCpu)
            {
                _player1IsCpu = true;
                _player1HandTcs?.TrySetResult(HandExtensions.RandomHand());
            }
            else if (clientId == _player2Id && !_player2IsCpu)
            {
                _player2IsCpu = true;
                _player2HandTcs?.TrySetResult(HandExtensions.RandomHand());
            }
        }

        [ClientRpc]
        private void ShowChoicePanelClientRpc(string myName, string yourName, ClientRpcParams rpcParams = default)
        {
            if (!TryGetUi(out var ui))
            {
                return;
            }

            ui.ShowChoicePanel(myName, yourName);
        }

        [ClientRpc]
        private void ShowResultClientRpc(RoundOutcome myOutcome, Hand myHand, Hand yourHand, ClientRpcParams rpcParams = default)
        {
            if (!TryGetUi(out var ui))
            {
                return;
            }

            ui.ShowResult(myOutcome, myHand, yourHand);
        }

        /// <summary>
        /// Locates the UI component on the local client.
        /// </summary>
        private static bool TryGetUi(out RockPaperScissorsUI ui)
        {
            ui = UnityEngine.Object.FindObjectOfType<RockPaperScissorsUI>();
            return ui != null;
        }

        /// <summary>
        /// Releases round resources and cancels outstanding waiters.
        /// </summary>
        private void CleanupRound()
        {
            _player1HandTcs = null;
            _player2HandTcs = null;
            _logic = null;

            _roundCts?.Cancel();
            _roundCts?.Dispose();
            _roundCts = null;
        }

        /// <summary>
        /// Resolves a friendly display name for the supplied identifier.
        /// </summary>
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
