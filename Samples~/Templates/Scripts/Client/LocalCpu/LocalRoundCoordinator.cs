using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Samples.Client.Data;
using DedicatedServerMultiplayerSample.Samples.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Samples.Client.LocalCpu
{
    /// <summary>
    /// Lightweight round coordinator that runs an offline match against a CPU opponent.
    /// </summary>
    public sealed class LocalRoundCoordinator : MonoBehaviour
    {
        [SerializeField] private RpsGameEventChannel eventChannel;
#if !UNITY_SERVER && !ENABLE_UCS_SERVER
        private static readonly ulong[] PlayerOrder = { LocalMatchIds.LocalPlayerId, LocalMatchIds.CpuPlayerId };
        private const int HandCollectionTimeoutSeconds = 15;
        private const int ResultConfirmTimeoutSeconds = 20;

        private string _localPlayerName;
        private const string CpuDisplayName = "CPU";

        private void Awake()
        {
            Debug.Log("[LocalRoundCoordinator] Awake");
        }

        private async void Start()
        {
            if (eventChannel == null)
            {
                Debug.LogError("[LocalRoundCoordinator] Event channel is not assigned.");
                enabled = false;
                return;
            }

            _localPlayerName = ClientData.Instance?.PlayerName;
            Debug.Log("[LocalRoundCoordinator] Initialized and starting local round.");
            eventChannel.GameAbortConfirmed += HandleGameAbortConfirmed;
            await InitializeAndRunAsync();
        }

        private void OnDestroy()
        {
            if (eventChannel != null)
            {
                eventChannel.GameAbortConfirmed -= HandleGameAbortConfirmed;
            }

        }

        /// <summary>
        /// Ensures channel readiness, sends initial identity info, and begins the first round.
        /// </summary>
        private async Task InitializeAndRunAsync()
        {
            try
            {
                await eventChannel.WaitForChannelReadyAsync(CancellationToken.None);
                eventChannel.RaisePlayersReady(LocalMatchIds.LocalPlayerId, _localPlayerName, LocalMatchIds.CpuPlayerId, CpuDisplayName);
                await RunRoundLoopAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalRoundCoordinator] Fatal error: {ex.Message}");
                eventChannel.RaiseGameAborted("Local match failed.");
            }
        }

        /// <summary>
        /// Full local round lifecycle: collect hands, resolve, and notify UI.
        /// </summary>
        private async Task RunRoundLoopAsync()
        {
            var logic = new RockPaperScissorsGameLogic(PlayerOrder);
            var cpuIds = new[] { LocalMatchIds.CpuPlayerId };
            var expectedChoices = new HashSet<ulong>(PlayerOrder);
            var expectedConfirmations = new HashSet<ulong> { LocalMatchIds.LocalPlayerId };

            while (true)
            {
                eventChannel.RaiseRoundStarted();

                var choices = await CollectChoicesAsync(expectedChoices, cpuIds, TimeSpan.FromSeconds(HandCollectionTimeoutSeconds));
                var result = logic.ResolveRound(choices);
                eventChannel.RaiseRoundResult(result.Player1Id, result.Player1Outcome, result.Player1Hand,
                    result.Player2Id, result.Player2Outcome, result.Player2Hand, true);

                var continueGame = await CollectConfirmationsAsync(
                    expectedConfirmations,
                    TimeSpan.FromSeconds(ResultConfirmTimeoutSeconds));
                eventChannel.RaiseContinueDecision(continueGame);
                if (!continueGame)
                {
                    break;
                }
            }

            SceneManager.LoadScene("loading", LoadSceneMode.Single);
        }

        private void HandleGameAbortConfirmed()
        {
            SceneManager.LoadScene("loading", LoadSceneMode.Single);
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
                        Debug.LogWarning("[LocalRoundCoordinator] Hand collection timed out; filling missing hands.");
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
                        Debug.LogWarning("[LocalRoundCoordinator] Result confirmation timed out. Treating as quit.");
                        return false;
                    }
                }
            }
        }
#endif
    }
}
