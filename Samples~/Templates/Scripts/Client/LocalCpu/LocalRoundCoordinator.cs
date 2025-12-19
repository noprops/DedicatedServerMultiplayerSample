#if !UNITY_SERVER && !ENABLE_UCS_SERVER
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
        private static readonly ulong[] PlayerOrder = { LocalMatchIds.LocalPlayerId, LocalMatchIds.CpuPlayerId };
        private const int ResultConfirmTimeoutSeconds = 10;
        private const int HandCollectionTimeoutSeconds = 10;

        [SerializeField] private RpsGameEventChannel eventChannel;

        private string _localPlayerName;
        private RpsRoundCollectionSink _sink;
        private RockPaperScissorsGameLogic _logic;
        private const string CpuDisplayName = "CPU";

        private void Awake()
        {
            Debug.Log("[LocalRoundCoordinator] Awake");
        }

        private void Start()
        {
            if (eventChannel == null)
            {
                Debug.LogError("[LocalRoundCoordinator] Event channel is not assigned.");
                enabled = false;
                return;
            }

            _sink = new RpsRoundCollectionSink(eventChannel);
            _localPlayerName = ClientData.Instance?.PlayerName;
            Debug.Log("[LocalRoundCoordinator] Initialized and starting local round.");
            eventChannel.GameAbortConfirmed += HandleGameAbortConfirmed;
            _ = InitializeAndRunAsync();
        }

        private void OnDestroy()
        {
            if (eventChannel != null)
            {
                eventChannel.GameAbortConfirmed -= HandleGameAbortConfirmed;
            }

            _sink?.Dispose();
            _logic = null;
        }

        /// <summary>
        /// Ensures channel readiness, sends initial identity info, and begins the first round.
        /// </summary>
        private async Task InitializeAndRunAsync()
        {
            try
            {
                await _sink.WaitForChannelReadyAsync();
                eventChannel.RaisePlayersReady(LocalMatchIds.LocalPlayerId, _localPlayerName, LocalMatchIds.CpuPlayerId, CpuDisplayName);
                await RunRoundAsync();
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
        private async Task RunRoundAsync()
        {
            _sink.ResetForNewRound();
            _logic = new RockPaperScissorsGameLogic(PlayerOrder);

            var choices = await CollectHandsAsync(TimeSpan.FromSeconds(HandCollectionTimeoutSeconds));
            var result = _logic.ResolveRound(choices);
            eventChannel.RaiseRoundResult(result.Player1Id, result.Player1Outcome, result.Player1Hand,
                result.Player2Id, result.Player2Outcome, result.Player2Hand, true);
            await WaitForResultConfirmationAsync(TimeSpan.FromSeconds(ResultConfirmTimeoutSeconds));
        }

        private async Task<Dictionary<ulong, Hand>> CollectHandsAsync(TimeSpan timeout)
        {
            var awaitedIds = new[] { LocalMatchIds.LocalPlayerId, LocalMatchIds.CpuPlayerId };
            var cpuHand = HandExtensions.RandomHand();
            eventChannel.RaiseChoiceSelectedForPlayer(LocalMatchIds.CpuPlayerId, cpuHand);

            try
            {
                using var cts = new CancellationTokenSource(timeout);
                var choices = await _sink.WaitForChoicesAsync(awaitedIds, cts.Token);
                return choices;
            }
            catch (TaskCanceledException)
            {
                Debug.LogWarning("[LocalRoundCoordinator] Hand collection timed out; filling missing hands.");
                return new Dictionary<ulong, Hand>
                {
                    { LocalMatchIds.CpuPlayerId, cpuHand }
                };
            }
        }

        private async Task WaitForResultConfirmationAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            bool continueGame = false;

            try
            {
                var confirmations = await _sink.WaitForConfirmationsAsync(new[] { LocalMatchIds.LocalPlayerId }, cts.Token);
                continueGame = confirmations.TryGetValue(LocalMatchIds.LocalPlayerId, out var vote) && vote;
            }
            catch (TaskCanceledException)
            {
                Debug.LogWarning("[LocalRoundCoordinator] Result confirmation timed out. Treating as quit.");
            }

            if (continueGame)
            {
                _ = RunRoundAsync();
            }
            else
            {
                SceneManager.LoadScene("loading", LoadSceneMode.Single);
            }
        }

        private void HandleGameAbortConfirmed()
        {
            SceneManager.LoadScene("loading", LoadSceneMode.Single);
        }
    }
}
#endif
