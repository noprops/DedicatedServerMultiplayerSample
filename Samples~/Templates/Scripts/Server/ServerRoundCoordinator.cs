using UnityEngine;

#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Server.Bootstrap;
using DedicatedServerMultiplayerSample.Server.Core;
#endif

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Server-side coordinator that owns the RPS round lifecycle and talks to clients via the event dispatcher.
    /// </summary>
    public sealed class ServerRoundCoordinator : MonoBehaviour
    {
        [SerializeField] private RpsGameEventChannel eventChannel;
#if UNITY_SERVER || ENABLE_UCS_SERVER
        public const int RequiredGamePlayers = 2;
        public const ulong CpuPlayerBaseId = 100;
        private const int HandCollectionTimeoutSeconds = 15;
        private const int ResultConfirmTimeoutSeconds = 20;

        private readonly ulong[] _clientIds = new ulong[RequiredGamePlayers];
        private readonly Dictionary<ulong, string> _playerNames = new();
        private ServerStartupRunner _startupRunner;
        private ServerConnectionManager _connectionManager;
        private static readonly int ShutdownDelay = 10;

        /// <summary>
        /// Kicks off the server-side orchestration once the scene loads.
        /// </summary>
        private async void Start()
        {
            _startupRunner = ServerSingleton.Instance?.StartupRunner;
            if (_startupRunner == null)
            {
                Debug.LogError("[ServerRoundCoordinator] No ServerStartupRunner; shutting down");
                return;
            }
            _connectionManager = ServerSingleton.Instance?.ConnectionManager;
            if (_connectionManager == null)
            {
                Debug.LogError("[ServerRoundCoordinator] No ServerConnectionManager; shutting down");
                return;
            }
            if (eventChannel == null)
            {
                Debug.LogError("[ServerRoundCoordinator] Event channel is not assigned.");
                return;
            }

            _connectionManager.ClientDisconnected += HandleClientDisconnected;
            await eventChannel.WaitForChannelReadyAsync(CancellationToken.None);
            var startupSucceeded = await _startupRunner.WaitForStartupCompletionAsync();
            if (!startupSucceeded)
            {
                Debug.LogWarning("[ServerRoundCoordinator] Startup did not complete successfully; aborting.");
                BroadcastGameAbort("Failed to start the game.");
                return;
            }

            _ = RunRoundAsync();
        }

        /// <summary>
        /// Cleans dispatcher hooks and round state when the coordinator is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            if (_connectionManager != null)
            {
                _connectionManager.ClientDisconnected -= HandleClientDisconnected;
            }

            _startupRunner = null;
            _connectionManager = null;
        }

        /// <summary>
        /// Full round lifecycle: wait for readiness, gather players, resolve, and shutdown.
        /// </summary>
        private async Task RunRoundAsync()
        {
            try
            {
                Debug.Log("[ServerRoundCoordinator] RunRoundAsync starting");
                var connectedIds = _connectionManager.GetReadyClientsSnapshot() ?? Array.Empty<ulong>();
                SetPlayerSlots(connectedIds);

                // Only send player identities once at the start.
                BroadcastPlayersReady();

                while (true)
                {
                    var cpuIds = _clientIds.Where(IsCpuId).ToArray();
                    var expectedChoices = new HashSet<ulong>(_clientIds);
                    var expectedConfirmations = new HashSet<ulong>(_clientIds.Where(id => !IsCpuId(id)));
                    var logic = new RockPaperScissorsGameLogic(_clientIds);

                    eventChannel.RaiseRoundStarted();
                    var choices = await CollectChoicesAsync(expectedChoices, cpuIds, TimeSpan.FromSeconds(HandCollectionTimeoutSeconds));
                    var result = logic.ResolveRound(choices);
                    Debug.LogFormat("[ServerRoundCoordinator] Round resolved. P1={0}({1}), P2={2}({3})",
                        result.Player1Id, result.Player1Hand, result.Player2Id, result.Player2Hand);
                    var continueGame = await NotifyResultsAndAwaitExitAsync(
                        result,
                        expectedConfirmations,
                        TimeSpan.FromSeconds(ResultConfirmTimeoutSeconds));
                    eventChannel.RaiseContinueDecision(continueGame);
                    if (!continueGame)
                    {
                        break;
                    }
                }

                Debug.Log("[ServerRoundCoordinator] Round acknowledgements satisfied; requesting shutdown");
                ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.Normal, "Game completed", ShutdownDelay);
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested elsewhere; nothing additional to do.
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerRoundCoordinator] Unexpected error: {e.Message}");
                ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.Error, e.Message, ShutdownDelay);
            }
        }

        /// <summary>
        /// Maps connected client IDs into fixed slots and fills remaining with CPU placeholders.
        /// </summary>
        private void SetPlayerSlots(IReadOnlyList<ulong> connectedClientIds)
        {
            Array.Clear(_clientIds, 0, _clientIds.Length);
            _playerNames.Clear();

            var assigned = 0;

            Debug.LogFormat("[ServerRoundCoordinator] Assigning slots for {0} connected clients", connectedClientIds.Count);

            for (; assigned < connectedClientIds.Count && assigned < RequiredGamePlayers; assigned++)
            {
                ulong clientId = connectedClientIds[assigned];
                _clientIds[assigned] = clientId;
                _playerNames[clientId] = ResolveDisplayName(clientId);
            }

            // Fill remaining slots with CPU participants when insufficient humans joined.
            var cpuId = CpuPlayerBaseId;
            for (; assigned < RequiredGamePlayers; assigned++)
            {
                _clientIds[assigned] = cpuId;
                _playerNames[cpuId] = "CPU";
                cpuId++;
            }

            Debug.LogFormat("[ServerRoundCoordinator] Slot assignment complete. P1={0}, P2={1}", _clientIds[0], _clientIds[1]);

            string ResolveDisplayName(ulong clientId)
            {
                if (IsCpuId(clientId))
                {
                    return "CPU";
                }

                if (_connectionManager != null && _connectionManager.TryGetPlayerName(clientId, out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }

                return $"Player{clientId}";
            }
        }

        /// <summary>
        /// Sends per-client ready notifications via the dispatcher.
        /// </summary>
        private void BroadcastPlayersReady()
        {
            var player1Id = _clientIds[0];
            var player2Id = _clientIds[1];

            var player1Name = _playerNames[player1Id];
            var player2Name = _playerNames[player2Id];

            Debug.LogFormat("[ServerRoundCoordinator] Broadcasting players ready. P1={0}({1}), P2={2}({3})",
                player1Id, player1Name, player2Id, player2Name);

            eventChannel.RaisePlayersReady(player1Id, player1Name, player2Id, player2Name);
        }
        /// <summary>
        /// Informs each human client of their personal round results.
        /// </summary>
        private void BroadcastRoundResult(RpsResult result, bool canContinue)
        {
            eventChannel.RaiseRoundResult(
                result.Player1Id, result.Player1Outcome, result.Player1Hand,
                result.Player2Id, result.Player2Outcome, result.Player2Hand, canContinue);

            Debug.LogFormat("[ServerRoundCoordinator] Sent RoundEnded: P1({0}) hand={1} outcome={2}; P2({3}) hand={4} outcome={5}",
                result.Player1Id, result.Player1Hand, result.Player1Outcome,
                result.Player2Id, result.Player2Hand, result.Player2Outcome);
        }

        /// <summary>
        /// Notifies every human client that the match aborted abnormally.
        /// </summary>
        private void BroadcastGameAbort(string message)
        {
            eventChannel.RaiseGameAborted(message);
        }

        /// <summary>
        /// Broadcasts the round outcome and waits until every human player confirms or the timeout elapses.
        /// </summary>
        private async Task<bool> NotifyResultsAndAwaitExitAsync(
            RpsResult result,
            HashSet<ulong> pending,
            TimeSpan timeout)
        {
            if (pending.Count == 0)
            {
                return false;
            }

            var canContinue = ShouldAllowContinue();
            BroadcastRoundResult(result, canContinue);
            if (!canContinue)
            {
                return false;
            }

            return await CollectConfirmationsAsync(pending, timeout);
        }

        private static bool IsCpuId(ulong clientId) => clientId >= CpuPlayerBaseId;

        private void HandleClientDisconnected(ulong clientId)
        {
            if (IsCpuId(clientId))
            {
                return;
            }

            if (!_clientIds.Contains(clientId))
            {
                return;
            }

            eventChannel.RaiseChoiceSelectedForPlayer(clientId, HandExtensions.RandomHand());
        }

        private bool ShouldAllowContinue()
        {
            return IsFriendMatch() && AreAllClientsConnected();
        }

        private bool IsFriendMatch()
        {
            var firstId = _clientIds[0];
            if (IsCpuId(firstId))
            {
                return false;
            }

            if (_connectionManager != null &&
                _connectionManager.TryGetPlayerPayloadValue(firstId, "gameMode", out string mode) &&
                !string.IsNullOrWhiteSpace(mode))
            {
                return string.Equals(mode, "friend", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private bool AreAllClientsConnected()
        {
            var connected = Unity.Netcode.NetworkManager.Singleton?.ConnectedClientsIds;
            if (connected == null)
            {
                return false;
            }

            foreach (var id in _clientIds)
            {
                if (IsCpuId(id))
                {
                    // Any CPU slot means we are not in a full-human match.
                    return false;
                }

                if (!connected.Contains(id))
                {
                    return false;
                }
            }

            return true;
        }

        private async Task<Dictionary<ulong, Hand>> CollectChoicesAsync(
            HashSet<ulong> expectedIds,
            IReadOnlyCollection<ulong> cpuIds,
            TimeSpan timeout)
        {
            var choices = new Dictionary<ulong, Hand>();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(ulong playerId, Hand hand)
            {
                if (!expectedIds.Contains(playerId) || choices.ContainsKey(playerId))
                {
                    return;
                }

                choices[playerId] = hand;
                if (choices.Count == expectedIds.Count)
                {
                    tcs.TrySetResult(true);
                }
            }

            eventChannel.ChoiceSelected += Handler;
            using (var cts = new CancellationTokenSource(timeout))
            {
                using (cts.Token.Register(() => tcs.TrySetCanceled(cts.Token)))
                {
                    foreach (var cpuId in cpuIds)
                    {
                        eventChannel.RaiseChoiceSelectedForPlayer(cpuId, HandExtensions.RandomHand());
                    }

                    try
                    {
                        await tcs.Task;
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.LogWarning("[ServerRoundCoordinator] Hand collection timed out; filling missing hands.");
                    }
                }
            }

            eventChannel.ChoiceSelected -= Handler;

            foreach (var expectedId in expectedIds)
            {
                if (!choices.ContainsKey(expectedId))
                {
                    choices[expectedId] = HandExtensions.RandomHand();
                }
            }

            return choices;
        }

        private async Task<bool> CollectConfirmationsAsync(HashSet<ulong> expectedIds, TimeSpan timeout)
        {
            var confirmations = new Dictionary<ulong, bool>();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(ulong playerId, bool continueGame)
            {
                if (!expectedIds.Contains(playerId) || confirmations.ContainsKey(playerId))
                {
                    return;
                }

                confirmations[playerId] = continueGame;

                if (!continueGame)
                {
                    tcs.TrySetResult(false);
                    return;
                }

                if (confirmations.Count == expectedIds.Count)
                {
                    tcs.TrySetResult(true);
                }
            }

            eventChannel.RoundResultConfirmed += Handler;
            using (var cts = new CancellationTokenSource(timeout))
            {
                using (cts.Token.Register(() => tcs.TrySetCanceled(cts.Token)))
                {
                    try
                    {
                        return await tcs.Task;
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.LogWarning("[ServerRoundCoordinator] Result confirmation timed out. Treating as quit.");
                        return false;
                    }
                }
            }
        }
#endif
    }
}
