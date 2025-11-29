#if !UNITY_SERVER && !ENABLE_UCS_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Samples.Client;
using DedicatedServerMultiplayerSample.Samples.Shared;
using DedicatedServerMultiplayerSample.Shared;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.Local
{
    /// <summary>
    /// Lightweight round coordinator that runs an offline match against a CPU opponent.
    /// </summary>
    public sealed class LocalRoundCoordinator : MonoBehaviour
    {
        private const ulong LocalPlayerId = 1;
        private const ulong CpuPlayerId = 2;
        private static readonly ulong[] PlayerOrder = { LocalPlayerId, CpuPlayerId };

        [SerializeField] private RpsGameEventChannel eventChannel;

        private RockPaperScissorsGameLogic _logic;
        private string _localPlayerName;
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

            _localPlayerName = ClientData.Instance?.PlayerName;
            Debug.Log("[LocalRoundCoordinator] Initialized and starting local round.");
            _ = RunRoundAsync();
        }

        private void OnDestroy()
        {
            _logic = null;
        }

        /// <summary>
        /// Full local round lifecycle: wait for channel readiness, collect hands, resolve, and notify UI.
        /// </summary>
        private async Task RunRoundAsync()
        {
            try
            {
                await eventChannel.WaitUntilReadyAsync();
                _logic = new RockPaperScissorsGameLogic(PlayerOrder);

                var result = await CollectHandsAndResolveRoundAsync();
                eventChannel.RaiseRoundResult(result.Player1Id, result.Player1Outcome, result.Player1Hand,
                    result.Player2Id, result.Player2Outcome, result.Player2Hand);
                await WaitForResultConfirmationAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalRoundCoordinator] Fatal error: {ex.Message}");
                eventChannel.RaiseGameAborted("Local match failed.");
            }
        }

        private void SubmitCpuHand()
        {
            _logic?.SubmitHand(CpuPlayerId, HandExtensions.RandomHand());
        }

        private async Task<RpsResult> CollectHandsAndResolveRoundAsync()
        {
            void OnChoiceSelected(ulong playerId, Hand hand)
            {
                if (playerId != LocalPlayerId || hand == Hand.None)
                {
                    return;
                }

                _logic?.SubmitHand(LocalPlayerId, hand);
            }

            eventChannel.ChoiceSelected += OnChoiceSelected;

            try
            {
                eventChannel.RaiseRoundStarted(LocalPlayerId, _localPlayerName, CpuPlayerId, CpuDisplayName);
                SubmitCpuHand();
                return await _logic.RunAsync();
            }
            finally
            {
                eventChannel.ChoiceSelected -= OnChoiceSelected;
            }
        }

        private async Task WaitForResultConfirmationAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(ulong playerId)
            {
                if (playerId != LocalPlayerId)
                {
                    return;
                }

                eventChannel.RoundResultConfirmed -= Handler;
                tcs.TrySetResult(true);
            }

            eventChannel.RoundResultConfirmed += Handler;

            using (var cts = new CancellationTokenSource(timeout))
            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                try
                {
                    await tcs.Task;
                }
                catch (TaskCanceledException)
                {
                    Debug.LogWarning("[LocalRoundCoordinator] Result confirmation timed out.");
                }
                finally
                {
                    eventChannel.RoundResultConfirmed -= Handler;
                }
            }
        }

    }
}
#endif
