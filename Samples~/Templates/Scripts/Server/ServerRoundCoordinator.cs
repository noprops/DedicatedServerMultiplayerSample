#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Server.Bootstrap;
using DedicatedServerMultiplayerSample.Server.Core;
using DedicatedServerMultiplayerSample.Shared;
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

        private readonly ulong[] _clientIds = new ulong[RequiredGamePlayers];
        private readonly Dictionary<ulong, string> _playerNames = new();
        private ServerGameManager _gameManager;
        [SerializeField] private RpsGameEventChannel dispatcher;

        /// <summary>
        /// Kicks off the server-side orchestration once the scene loads.
        /// </summary>
        private void Start()
        {
            _gameManager = ServerSingleton.Instance?.GameManager;
            if (_gameManager == null)
            {
                Debug.LogError("[ServerRoundCoordinator] No ServerGameManager; shutting down");
                return;
            }

            _ = RunRoundAsync();
        }

        /// <summary>
        /// Cleans dispatcher hooks and round state when the coordinator is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            _gameManager = null;
        }

        /// <summary>
        /// Full round lifecycle: wait for readiness, gather players, resolve, and shutdown.
        /// </summary>
        private async Task RunRoundAsync()
        {
            try
            {
                if (!await WaitForDispatcherReadyAsync())
                {
                    return;
                }

                Debug.Log("[ServerRoundCoordinator] RunRoundAsync starting");
                var (connected, connectedIds) = await WaitForAllClientsConnectedAsync();
                if (!connected || connectedIds == null || connectedIds.Length == 0)
                {
                    Debug.LogWarning("[ServerRoundCoordinator] Failed to gather required clients.");
                    BroadcastGameAbort("Failed to start the game.");
                    _gameManager?.RequestShutdown(ShutdownKind.StartTimeout, "Clients did not join in time", 0f);
                    return;
                }

                SetPlayerSlots(connectedIds);

                var result = await CollectHandsAndResolveRoundAsync();
                Debug.LogFormat("[ServerRoundCoordinator] Round resolved. P1={0}({1}), P2={2}({3})",
                    result.Player1Id, result.Player1Hand, result.Player2Id, result.Player2Hand);
                await NotifyResultsAndAwaitExitAsync(result, TimeSpan.FromSeconds(20));
                Debug.Log("[ServerRoundCoordinator] Round acknowledgements satisfied; requesting shutdown");
                _gameManager?.RequestShutdown(ShutdownKind.Normal, "Game completed");
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested elsewhere; nothing additional to do.
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerRoundCoordinator] Unexpected error: {e.Message}");
                _gameManager?.RequestShutdown(ShutdownKind.Error, e.Message, 5f);
            }
        }

        /// <summary>
        /// Awaits the dispatcher becoming ready so events can safely flow.
        /// </summary>
        private async Task<bool> WaitForDispatcherReadyAsync()
        {
            if (dispatcher.IsChannelReady)
            {
                return true;
            }

            var tcs = new TaskCompletionSource<bool>();

            void Handler()
            {
                dispatcher.ChannelReady -= Handler;
                tcs.TrySetResult(true);
            }

            dispatcher.ChannelReady += Handler;
            return await tcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Waits until all expected clients have connected to the server build.
        /// </summary>
        private async Task<(bool ok, ulong[] ids)> WaitForAllClientsConnectedAsync(CancellationToken ct = default)
        {
            if (_gameManager == null)
            {
                Debug.LogWarning("[ServerRoundCoordinator] WaitForAllClientsConnectedAsync called without game manager");
                return (false, Array.Empty<ulong>());
            }

            if (_gameManager.AreAllClientsConnected)
            {
                Debug.Log("[ServerRoundCoordinator] All clients already connected");
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
                Debug.LogFormat("[ServerRoundCoordinator] WaitForAllClientsConnectedAsync signalled={0}", signalled);
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
        }

        /// <summary>
        /// Sends per-client round start notifications via the dispatcher.
        /// </summary>
        /// <summary>
        /// Registers choice handlers, notifies clients to start, and waits for both hands before resolving the round.
        /// </summary>
        private async Task<RpsResult> CollectHandsAndResolveRoundAsync()
        {
            var logic = new RockPaperScissorsGameLogic(_clientIds);

            // Callback invoked whenever a player submits their hand.
            void ChoiceHandler(ulong playerId, Hand hand)
            {
                if (IsCpuId(playerId) || Array.IndexOf(_clientIds, playerId) < 0)
                {
                    Debug.LogWarningFormat("[ServerRoundCoordinator] Rejecting submit from {0} (cpu or unknown)", playerId);
                    return;
                }

                if (hand == Hand.None)
                {
                    Debug.LogWarningFormat("[ServerRoundCoordinator] Rejecting empty hand from {0}", playerId);
                    return;
                }

                var accepted = logic.SubmitHand(playerId, hand);
                Debug.LogFormat("[ServerRoundCoordinator] Submit from {0} hand={1} accepted={2}", playerId, hand, accepted);
            }

            dispatcher.ChoiceSelected += ChoiceHandler;

            try
            {
                BroadcastRoundStart();

                // Auto-submit a random hand on behalf of CPU participants.
                foreach (var id in _clientIds)
                {
                    if (IsCpuId(id))
                    {
                        logic.SubmitHand(id, HandExtensions.RandomHand());
                    }
                }

                return await logic.RunAsync();
            }
            finally
            {
                dispatcher.ChoiceSelected -= ChoiceHandler;
            }
        }

        /// <summary>
        /// Sends per-client round start notifications via the dispatcher.
        /// </summary>
        private void BroadcastRoundStart()
        {
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

            Debug.LogFormat("[ServerRoundCoordinator] Broadcasting round start. P1={0}({1}), P2={2}({3})",
                player1Id, player1Name, player2Id, player2Name);

            foreach (var clientId in _clientIds)
            {
                if (IsCpuId(clientId))
                {
                    continue;
                }

                string myName = clientId == player1Id ? player1Name : player2Name;
                string opponentName = clientId == player1Id ? player2Name : player1Name;

                dispatcher.RaiseRoundStarted(clientId, myName, opponentName);
            }
        }
        /// <summary>
        /// Informs each human client of their personal round results.
        /// </summary>
        private void BroadcastRoundResult(RpsResult result)
        {
            foreach (var clientId in _clientIds)
            {
                if (IsCpuId(clientId))
                {
                    continue;
                }

                var isPlayerOne = clientId == result.Player1Id;
                var myHand = isPlayerOne ? result.Player1Hand : result.Player2Hand;
                var opponentHand = isPlayerOne ? result.Player2Hand : result.Player1Hand;
                var myOutcome = isPlayerOne ? result.Player1Outcome : result.Player2Outcome;

                dispatcher.RaiseRoundResult(clientId, myOutcome, myHand, opponentHand);

                Debug.LogFormat("[ServerRoundCoordinator] Sent RoundEnded to {0}: myHand={1}, oppHand={2}, outcome={3}",
                    clientId, myHand, opponentHand, myOutcome);
            }
        }

        /// <summary>
        /// Notifies every human client that the match aborted abnormally.
        /// </summary>
        private void BroadcastGameAbort(string message)
        {
            foreach (var clientId in _clientIds)
            {
                if (IsCpuId(clientId))
                {
                    continue;
                }

                dispatcher.RaiseGameAborted(clientId, message);
            }
        }

        /// <summary>
        /// Resolves a friendly display name for the provided client ID.
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

        /// <summary>
        /// Broadcasts the round outcome and waits until every human player confirms or the timeout elapses.
        /// </summary>
        private async Task NotifyResultsAndAwaitExitAsync(RpsResult result, TimeSpan timeout)
        {
            var pending = new HashSet<ulong>(_clientIds.Where(id => !IsCpuId(id)));

            if (pending.Count == 0)
            {
                return;
            }

            using var awaiter = new SimpleSignalAwaiter(timeout);

            void Handler(ulong playerId)
            {
                if (!pending.Remove(playerId))
                {
                    return;
                }

                if (pending.Count == 0)
                {
                    awaiter.OnSignal();
                }
            }

            dispatcher.RoundResultConfirmed += Handler;

            try
            {
                BroadcastRoundResult(result);

                try
                {
                    await awaiter.WaitAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Timeout expired; continue shutdown anyway.
                }
            }
            finally
            {
                dispatcher.RoundResultConfirmed -= Handler;
            }
        }

        private static bool IsCpuId(ulong clientId) => clientId >= CpuPlayerBaseId;
    }
}
#endif
