#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
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

        private readonly Dictionary<ulong, int> _playerSlots = new();
        private readonly bool[] _slotSubmittedByClient = new bool[2];
        private readonly Dictionary<int, TaskCompletionSource<Hand?>> _handWaiters = new();
        private ServerGameManager _gameManager;
        private CancellationTokenSource _roundCts;
        private bool _roundStarted;

        /// <summary>
        /// Initializes server-only state and installs event hooks when the server instance spawns.
        /// </summary>
        partial void OnServerSpawn()
        {
            Phase.Value = GamePhase.WaitingForPlayers;
            LastResult.Value = default;
            PlayerIds.Clear();
            PlayerNames.Clear();

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

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
        /// Cleans up subscriptions and round state when the server instance despawns.
        /// </summary>
        partial void OnServerDespawn()
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            CancelActiveRound();
            _roundCts?.Dispose();
            _roundCts = null;
            DisposeRoundResources();

            if (_gameManager != null)
            {
                _gameManager.RemoveAllClientsConnected(HandleAllClientsConnected);
                _gameManager.RemoveShutdownRequested(HandleShutdownRequested);
                _gameManager = null;
            }
        }

        /// <summary>
        /// Starts the gameplay flow once the required participants are connected.
        /// </summary>
        private void HandleAllClientsConnected(ulong[] participantIds)
        {
            if (_roundStarted)
            {
                return;
            }

            _roundStarted = true;
            var idsCopy = participantIds != null ? (ulong[])participantIds.Clone() : Array.Empty<ulong>();
            _ = RunGameplayAsync(idsCopy);
        }

        /// <summary>
        /// Reacts to shutdown requests by cancelling active rounds and updating the game phase when needed.
        /// </summary>
        private void HandleShutdownRequested(ShutdownKind kind, string reason)
        {
            if (kind != ShutdownKind.Normal)
            {
                CancelActiveRound();
            }

            switch (kind)
            {
                case ShutdownKind.StartTimeout:
                    if (!_roundStarted)
                    {
                        Phase.Value = GamePhase.StartFailed;
                        PlayerIds.Clear();
                        PlayerNames.Clear();
                    }
                    break;
                case ShutdownKind.Error:
                    if (!_roundStarted)
                    {
                        Phase.Value = GamePhase.StartFailed;
                    }
                    break;
                case ShutdownKind.AllPlayersDisconnected:
                    Debug.LogWarning($"[NetworkGame] Shutdown due to disconnects: {reason}");
                    break;
                case ShutdownKind.Normal:
                    break;
            }
        }

        /// <summary>
        /// Orchestrates the server-side lifecycle of a single match.
        /// </summary>
        private async Task RunGameplayAsync(IReadOnlyList<ulong> participantIds)
        {
            try
            {
                ApplyPlayerIds(participantIds);
                Phase.Value = GamePhase.Choosing;

                _roundCts = new CancellationTokenSource();
                await RunRoundAsync(_roundCts.Token);

                if (!_roundCts.IsCancellationRequested)
                {
                    _gameManager?.RequestShutdown(ShutdownKind.Normal, "Game completed");
                }
            }
            catch (OperationCanceledException)
            {
                // cancellation requested (shutdown triggered)
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkGame] Fatal error: {e.Message}");
                _gameManager?.RequestShutdown(ShutdownKind.Error, e.Message, 5f);
            }
            finally
            {
                DisposeRoundResources();
                _roundCts?.Dispose();
                _roundCts = null;
            }
        }

        /// <summary>
        /// Copies the participant list and inserts CPU placeholders until the roster is full.
        /// </summary>
        private void ApplyPlayerIds(IReadOnlyList<ulong> participantIds)
        {
            PlayerIds.Clear();
            PlayerNames.Clear();

            if (participantIds != null)
            {
                foreach (var id in participantIds)
                {
                    PlayerIds.Add(id);
                }
            }

            while (PlayerIds.Count < RequiredGamePlayers)
            {
                var cpuId = NextCpuId();
                PlayerIds.Add(cpuId);
            }

            RebuildPlayerNames();
        }

        /// <summary>
        /// Refreshes player display names based on current connection metadata.
        /// </summary>
        private void RebuildPlayerNames()
        {
            PlayerNames.Clear();

            foreach (var id in PlayerIds)
            {
                PlayerNames.Add(GetDisplayName(id));
            }
        }

        /// <summary>
        /// Returns true when the supplied identifier belongs to a CPU placeholder.
        /// </summary>
        private static bool IsCpuId(ulong clientId) => clientId >= CpuPlayerBaseId;

        /// <summary>
        /// Picks the next unused CPU identifier.
        /// </summary>
        private ulong NextCpuId()
        {
            var cpuId = CpuPlayerBaseId;

            while (true)
            {
                var taken = false;
                for (var i = 0; i < PlayerIds.Count; i++)
                {
                    if (PlayerIds[i] == cpuId)
                    {
                        taken = true;
                        break;
                    }
                }

                if (!taken)
                {
                    return cpuId;
                }

                cpuId++;
            }
        }

        /// <summary>
        /// Resolves a friendly display name for the specified participant.
        /// </summary>
        private FixedString64Bytes GetDisplayName(ulong id)
        {
            if (IsCpuId(id))
            {
                return "CPU";
            }

            if (_gameManager != null && _gameManager.TryGetPlayerDisplayName(id, out var resolvedName))
            {
                return resolvedName;
            }

            return $"Player{id}";
        }

        /// <summary>
        /// Handles disconnects by updating names and auto-selecting hands for absent players.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            RebuildPlayerNames();

            if (!_playerSlots.TryGetValue(clientId, out var slot))
            {
                return;
            }

            if (_slotSubmittedByClient[slot] ||
                (_handWaiters.TryGetValue(slot, out var waiter) && waiter.Task.IsCompleted))
            {
                return;
            }

            var autoHand = GetRandomHand();
            SubmitHandForSlot(slot, autoHand, true);
            Debug.Log($"[NetworkGame] Auto-assigned {autoHand} to disconnected player {clientId}");
        }

        /// <summary>
        /// Refreshes UI names when a new client connects.
        /// </summary>
        private void OnClientConnected(ulong clientId)
        {
            RebuildPlayerNames();
        }

        // ========== ServerRpc Implementation ==========
        /// <summary>
        /// Records a player's submitted hand, ignoring invalid or duplicate submissions.
        /// </summary>
        partial void HandleSubmitChoice(ulong clientId, Hand choice)
        {
            if (!_playerSlots.TryGetValue(clientId, out var slot))
            {
                Debug.LogWarning($"[Server] Ignoring choice from non-participant {clientId}");
                return;
            }

            if (_slotSubmittedByClient[slot] ||
                (_handWaiters.TryGetValue(slot, out var waiter) && waiter.Task.IsCompleted))
            {
                Debug.LogWarning($"[Server] Ignoring duplicate choice from {clientId}");
                return;
            }

            SubmitHandForSlot(slot, choice, !IsCpuId(clientId));
            Debug.Log($"[Server] Choice received: {choice} from {clientId}");
        }

        /// <summary>
        /// Updates the replicated phase/result variables to reflect the resolved outcome.
        /// </summary>
        private void PublishGameResult(RpsResult result)
        {
            Phase.Value = GamePhase.Resolving;
            LastResult.Value = result;
            Phase.Value = GamePhase.Finished;
        }

        /// <summary>
        /// Cancels any active round processing tasks.
        /// </summary>
        private void CancelActiveRound()
        {
            _roundCts?.Cancel();
            CancelHandWaiters();
        }

        /// <summary>
        /// Extracts a readable player name from the connection payload.
        /// </summary>
        private static FixedString64Bytes ResolvePlayerName(Dictionary<string, object> payload, ulong clientId)
        {
            if (payload != null &&
                payload.TryGetValue("playerName", out var value) &&
                value is string name &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return $"Player{clientId}";
        }

        /// <summary>
        /// Placeholder hook for server-side post game reporting. Replace with project-specific logic
        /// (e.g., Cloud Save, Analytics, Leaderboards) to keep results authoritative on the server.
        /// </summary>
        private static async Task ReportGameCompletedAsync(RpsResult result, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(0.5f), ct);
        }

        /// <summary>
        /// Executes a single round from start to finish, including CPU seeding and timeout handling.
        /// </summary>
        private async Task RunRoundAsync(CancellationToken ct)
        {
            DisposeRoundResources();

            if (PlayerIds.Count < RequiredGamePlayers)
            {
                Debug.LogWarning("[NetworkGame] Not enough players to start round");
                return;
            }

            var player0 = PlayerIds[0];
            var player1 = PlayerIds[1];

            _playerSlots[player0] = 0;
            _playerSlots[player1] = 1;
            _slotSubmittedByClient[0] = false;
            _slotSubmittedByClient[1] = false;
            _handWaiters.Clear();
            _handWaiters[0] = CreateHandWaiter();
            _handWaiters[1] = CreateHandWaiter();

            Phase.Value = GamePhase.Choosing;

            AutoSubmitCpuHands();

            var player0HandTask = WaitForHandAsync(0, ct);
            var player1HandTask = WaitForHandAsync(1, ct);

            var logic = new RockPaperScissorsGameLogic();
            var result = await logic.RunRoundAsync(
                player0,
                player1,
                TimeSpan.FromSeconds(RoundTimeoutSeconds),
                player0HandTask,
                player1HandTask,
                ct).ConfigureAwait(false);

            HandleUnresponsivePlayers();
            PublishGameResult(result);
            await ReportGameCompletedAsync(result, ct).ConfigureAwait(false);
        }

        private Task<Hand?> WaitForHandAsync(int slot, CancellationToken token)
        {
            if (!_handWaiters.TryGetValue(slot, out var waiter))
            {
                waiter = CreateHandWaiter();
                _handWaiters[slot] = waiter;
            }

            if (!token.CanBeCanceled)
            {
                return waiter.Task;
            }

            var forwarder = new TaskCompletionSource<Hand?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = token.Register(() => forwarder.TrySetCanceled(token));

            waiter.Task.ContinueWith(t =>
            {
                registration.Dispose();
                if (t.IsCanceled)
                {
                    forwarder.TrySetCanceled();
                }
                else if (t.IsFaulted)
                {
                    forwarder.TrySetException(t.Exception?.InnerExceptions ?? Array.Empty<Exception>());
                }
                else
                {
                    forwarder.TrySetResult(t.Result);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            return forwarder.Task;
        }

        private static TaskCompletionSource<Hand?> CreateHandWaiter()
        {
            return new TaskCompletionSource<Hand?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void AutoSubmitCpuHands()
        {
            foreach (var pair in _playerSlots)
            {
                if (!IsCpuId(pair.Key))
                {
                    continue;
                }

                SubmitHandForSlot(pair.Value, GetRandomHand(), true);
            }
        }

        private void SubmitHandForSlot(int slot, Hand hand, bool markSubmitted)
        {
            var waiter = _handWaiters.TryGetValue(slot, out var existing) ? existing : (_handWaiters[slot] = CreateHandWaiter());

            if (!waiter.Task.IsCompleted && waiter.TrySetResult(hand) && markSubmitted)
            {
                _slotSubmittedByClient[slot] = true;
            }
        }

        private void HandleUnresponsivePlayers()
        {
            foreach (var pair in _playerSlots)
            {
                if (IsCpuId(pair.Key))
                {
                    continue;
                }

                var slot = pair.Value;
                if (_slotSubmittedByClient[slot])
                {
                    continue;
                }

                if (NetworkManager.Singleton == null ||
                    NetworkManager.Singleton.ConnectedClientsIds.Contains(pair.Key))
                {
                    _gameManager?.DisconnectClient(pair.Key, "Selection timeout");
                }
            }
        }

        private void CancelHandWaiters()
        {
            foreach (var waiter in _handWaiters.Values)
            {
                waiter.TrySetCanceled();
            }

            _handWaiters.Clear();
        }

        private static Hand GetRandomHand()
        {
            return (Hand)UnityEngine.Random.Range(1, 4);
        }

        private void DisposeRoundResources()
        {
            CancelHandWaiters();
            _playerSlots.Clear();
            _slotSubmittedByClient[0] = false;
            _slotSubmittedByClient[1] = false;
        }
    }
}
#endif
