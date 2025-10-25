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
    public partial class RockPaperScissorsGame
    {
        private const float RoundTimeoutSeconds = 30f;

        private sealed class RoundState
        {
            public List<ulong> Players { get; } = new();
            public Dictionary<ulong, Hand> Choices { get; } = new();
            public TaskCompletionSource<bool> AllChosen { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private RoundState _currentRound;
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
                Debug.LogError("[RockPaperScissorsGame] No ServerGameManager");
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
            _currentRound = null;
            _roundCts?.Cancel();
            _roundCts?.Dispose();
            _roundCts = null;

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
                _roundCts?.Cancel();
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
                    Debug.LogWarning($"[RockPaperScissorsGame] Shutdown due to disconnects: {reason}");
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
                await ExecuteGameRoundAsync(_roundCts.Token);

                _gameManager?.RequestShutdown(ShutdownKind.Normal, "Game completed");
            }
            catch (OperationCanceledException)
            {
                // cancellation requested (shutdown triggered)
            }
            catch (TimeoutException)
            {
                Phase.Value = GamePhase.StartFailed;
                _gameManager?.RequestShutdown(ShutdownKind.Error, "Round timeout", 5f);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RockPaperScissorsGame] Fatal error: {e.Message}");
                _gameManager?.RequestShutdown(ShutdownKind.Error, e.Message, 5f);
            }
            finally
            {
                _roundCts?.Dispose();
                _roundCts = null;
            }
        }

        /// <summary>
        /// Copies the participant list and inserts CPU placeholders until the roster is full.
        /// </summary>
        private void ApplyPlayerIds(IReadOnlyList<ulong> participantIds)
        {
            var ids = participantIds != null ? new List<ulong>(participantIds) : new List<ulong>();
            var seen = new HashSet<ulong>(ids);

            while (ids.Count < RequiredGamePlayers)
            {
                var cpuId = NextCpuId(seen);
                seen.Add(cpuId);
                ids.Add(cpuId);
            }

            PlayerIds.Clear();
            PlayerNames.Clear();

            foreach (var id in ids)
            {
                PlayerIds.Add(id);
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
        private ulong NextCpuId(HashSet<ulong> existing)
        {
            var cpuId = CpuPlayerBaseId;
            while (existing.Contains(cpuId))
            {
                cpuId++;
            }

            return cpuId;
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

            return $"Player{id}";
        }

        /// <summary>
        /// Executes a full Rock-Paper-Scissors round, handling timeouts and disconnects.
        /// </summary>
        private async Task ExecuteGameRoundAsync(CancellationToken ct)
        {
            var round = new RoundState();
            foreach (var id in PlayerIds)
            {
                round.Players.Add(id);
            }

            if (round.Players.Count < RequiredGamePlayers)
            {
                Debug.LogWarning("[RockPaperScissorsGame] Not enough players to start round");
                return;
            }

            _currentRound = round;

            foreach (var playerId in round.Players)
            {
                if (IsCpuId(playerId))
                {
                    AssignRandomHand(round, playerId, "cpu");
                }
            }

            if (round.Choices.Count >= round.Players.Count)
            {
                round.AllChosen.TrySetResult(true);
            }

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(RoundTimeoutSeconds));

                try
                {
                    await round.AllChosen.Task.WaitOrCancel(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning("[RockPaperScissorsGame] Timeout - assigning hands to non-responsive players");

                    foreach (var playerId in round.Players)
                    {
                        if (IsCpuId(playerId) || round.Choices.ContainsKey(playerId))
                        {
                            continue;
                        }

                        AssignRandomHand(round, playerId, "timeout");
                        ServerSingleton.Instance?.GameManager?.DisconnectClient(playerId, "Selection timeout");
                    }
                }
            }

            var resolved = TryResolveRound(round, out var result);
            _currentRound = null;

            if (resolved)
            {
                await ReportGameCompletedAsync(result, ct);
            }
        }

        /// <summary>
        /// Assigns a random hand to the specified player and completes the round if everyone has chosen.
        /// </summary>
        private static void AssignRandomHand(RoundState round, ulong playerId, string reason)
        {
            var randomHand = (Hand)UnityEngine.Random.Range(1, 4);
            round.Choices[playerId] = randomHand;
            Debug.Log($"[RockPaperScissorsGame] Auto-assigned {randomHand} to {reason} player {playerId}");

            if (round.Choices.Count >= round.Players.Count)
            {
                round.AllChosen.TrySetResult(true);
            }
        }

        /// <summary>
        /// Handles disconnects by updating names and auto-selecting hands for absent players.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            RebuildPlayerNames();

            var round = _currentRound;
            if (round == null)
            {
                return;
            }

            if (!round.Players.Contains(clientId))
            {
                return;
            }

            if (round.Choices.ContainsKey(clientId))
            {
                Debug.Log($"[RockPaperScissorsGame] Disconnected player {clientId} already chose {round.Choices[clientId]}");
                return;
            }

            AssignRandomHand(round, clientId, "disconnected");
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
            var round = _currentRound;
            if (round == null)
            {
                return;
            }

            if (!round.Players.Contains(clientId))
            {
                Debug.LogWarning($"[Server] Ignoring choice from non-participant {clientId}");
                return;
            }

            if (round.Choices.ContainsKey(clientId))
            {
                Debug.LogWarning($"[Server] Ignoring duplicate choice from {clientId} (already has {round.Choices[clientId]})");
                return;
            }

            round.Choices[clientId] = choice;
            Debug.Log($"[Server] Choice received: {choice} from {clientId} ({round.Choices.Count}/{round.Players.Count})");

            if (round.Choices.Count >= round.Players.Count)
            {
                round.AllChosen.TrySetResult(true);
            }
        }

        /// <summary>
        /// Determines the round outcome and updates network variables, returning the resolved result.
        /// </summary>
        private bool TryResolveRound(RoundState round, out RpsResult result)
        {
            if (round.Players.Count < 2)
            {
                Debug.LogWarning("[RockPaperScissorsGame] Not enough participants to resolve round");
                result = default;
                return false;
            }

            ulong p1 = round.Players[0];
            ulong p2 = round.Players[1];

            var h1 = round.Choices.TryGetValue(p1, out var choice1) ? choice1 : Hand.None;
            var h2 = round.Choices.TryGetValue(p2, out var choice2) ? choice2 : Hand.None;

            result = new RpsResult
            {
                P1 = p1,
                P2 = p2,
                H1 = h1,
                H2 = h2,
                P1Outcome = (byte)DetermineOutcome(h1, h2),
                P2Outcome = (byte)DetermineOutcome(h2, h1)
            };

            Phase.Value = GamePhase.Resolving;
            LastResult.Value = result;
            Phase.Value = GamePhase.Finished;

            return true;
        }

        /// <summary>
        /// Computes the outcome of a hand against an opponent.
        /// </summary>
        private static RoundOutcome DetermineOutcome(Hand myHand, Hand opponentHand)
        {
            if (myHand == opponentHand)
            {
                return RoundOutcome.Draw;
            }

            return ((myHand == Hand.Rock && opponentHand == Hand.Scissors) ||
                    (myHand == Hand.Paper && opponentHand == Hand.Rock) ||
                    (myHand == Hand.Scissors && opponentHand == Hand.Paper))
                ? RoundOutcome.Win
                : RoundOutcome.Lose;
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
            // Sample project: intentionally left blank.
            // Production project: send <result> to the desired backend service here.
            await Task.Delay(TimeSpan.FromSeconds(0.5f), ct);
        }
    }
}
#endif
