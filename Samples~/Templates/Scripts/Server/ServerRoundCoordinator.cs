#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Server.Bootstrap;
using DedicatedServerMultiplayerSample.Server.Core;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Server-side coordinator that owns the RPS round lifecycle and talks to clients via the event dispatcher.
    /// </summary>
    public sealed class ServerRoundCoordinator : MonoBehaviour
    {
        public const int RequiredGamePlayers = 2;
        public const ulong CpuPlayerBaseId = 100;
        private const int HandCollectionTimeoutSeconds = 15;
        private const int ResultConfirmTimeoutSeconds = 20;

        private readonly ulong[] _clientIds = new ulong[RequiredGamePlayers];
        private readonly Dictionary<ulong, string> _playerNames = new();
        private ServerStartupRunner _startupRunner;
        private ServerConnectionStack _connectionStack;
        [SerializeField] private RpsGameEventChannel eventChannel;
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
            _connectionStack = ServerSingleton.Instance?.ConnectionStack;
            if (_connectionStack == null)
            {
                Debug.LogError("[ServerRoundCoordinator] No ServerConnectionStack; shutting down");
                return;
            }

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
            _startupRunner = null;
            _connectionStack = null;
        }

        /// <summary>
        /// Full round lifecycle: wait for readiness, gather players, resolve, and shutdown.
        /// </summary>
        private async Task RunRoundAsync()
        {
            try
            {
                await eventChannel.WaitForChannelReadyAsync();

                Debug.Log("[ServerRoundCoordinator] RunRoundAsync starting");
                var connectedIds = _connectionStack.GetReadyClientsSnapshot() ?? Array.Empty<ulong>();
                SetPlayerSlots(connectedIds);

                // Only send player identities once at the start.
                BroadcastPlayersReady();

                bool continueGame;
                do
                {
                    eventChannel.ResetRoundAwaiters();

                    var result = await CollectHandsAndResolveRoundAsync();
                    Debug.LogFormat("[ServerRoundCoordinator] Round resolved. P1={0}({1}), P2={2}({3})",
                        result.Player1Id, result.Player1Hand, result.Player2Id, result.Player2Hand);
                    continueGame = await NotifyResultsAndAwaitExitAsync(result, TimeSpan.FromSeconds(ResultConfirmTimeoutSeconds));
                } while (continueGame);

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

                if (_connectionStack != null && _connectionStack.TryGetPlayerName(clientId, out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }

                return $"Player{clientId}";
            }
        }

        /// <summary>
        /// Registers choice handlers, notifies clients to start, and waits for both hands before resolving the round.
        /// </summary>
        private async Task<RpsResult> CollectHandsAndResolveRoundAsync()
        {
            var logic = new RockPaperScissorsGameLogic(_clientIds);
            var choicesTask = eventChannel.WaitForChoicesAsync(
                _clientIds,
                TimeSpan.FromSeconds(HandCollectionTimeoutSeconds));

            // Submit CPU hands after waiters are registered so they are counted.
            foreach (var id in _clientIds)
            {
                if (IsCpuId(id))
                {
                    var cpuHand = HandExtensions.RandomHand();
                    eventChannel.RaiseChoiceSelectedForPlayer(id, cpuHand);
                }
            }

            var choices = await choicesTask;

            // Ensure every player has a hand (use random for missing entries and CPUs if needed).
            foreach (var id in _clientIds)
            {
                if (IsCpuId(id) || !choices.ContainsKey(id))
                {
                    var fillerHand = HandExtensions.RandomHand();
                    eventChannel.RaiseChoiceSelectedForPlayer(id, fillerHand);
                    choices[id] = fillerHand;
                }
            }

            if (choices.Count < _clientIds.Length)
            {
                Debug.LogWarning("[ServerRoundCoordinator] Hand collection timed out; filling missing hands.");
            }

            return logic.ResolveRound(choices);
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
        private async Task<bool> NotifyResultsAndAwaitExitAsync(RpsResult result, TimeSpan timeout)
        {
            var pending = new HashSet<ulong>(_clientIds.Where(id => !IsCpuId(id)));

            if (pending.Count == 0)
            {
                return false;
            }

            var continueVotes = new Dictionary<ulong, bool>();

            var canContinue = ShouldAllowContinue();
            BroadcastRoundResult(result, canContinue);

            var votes = await eventChannel.WaitForConfirmationsAsync(pending, timeout);
            foreach (var kvp in votes)
            {
                continueVotes[kvp.Key] = kvp.Value;
            }

            var allResponded = pending.All(id => continueVotes.ContainsKey(id));
            return allResponded && continueVotes.Count > 0 && continueVotes.Values.All(v => v);
        }

        private static bool IsCpuId(ulong clientId) => clientId >= CpuPlayerBaseId;

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

            if (_connectionStack != null &&
                _connectionStack.TryGetPlayerPayloadValue(firstId, "gameMode", out string mode) &&
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
    }
}
#endif
