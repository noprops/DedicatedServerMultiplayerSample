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
        private const int ResultConfirmTimeoutSeconds = 20;
        private const int HandCollectionTimeoutSeconds = 15;

        [SerializeField] private RpsGameEventChannel eventChannel;

        private string _localPlayerName;
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

            _logic = null;
        }

        /// <summary>
        /// Ensures channel readiness, sends initial identity info, and begins the first round.
        /// </summary>
        private async Task InitializeAndRunAsync()
        {
            try
            {
                await eventChannel.WaitForChannelReadyAsync();
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
            eventChannel.ResetRoundAwaiters();
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
            var choicesTask = eventChannel.WaitForChoicesAsync(awaitedIds, timeout);

            // Submit CPU hand after waiters are registered so it is counted.
            eventChannel.RaiseChoiceSelectedForPlayer(LocalMatchIds.CpuPlayerId, cpuHand);

            var choices = await choicesTask;

            if (!choices.ContainsKey(LocalMatchIds.CpuPlayerId))
            {
                choices[LocalMatchIds.CpuPlayerId] = cpuHand;
            }

            if (!choices.ContainsKey(LocalMatchIds.LocalPlayerId))
            {
                Debug.LogWarning("[LocalRoundCoordinator] Hand collection timed out; filling missing hands.");
            }

            return choices;
        }

        private async Task WaitForResultConfirmationAsync(TimeSpan timeout)
        {
            bool continueGame = false;

            var confirmations = await eventChannel.WaitForConfirmationsAsync(new[] { LocalMatchIds.LocalPlayerId }, timeout);
            continueGame = confirmations.TryGetValue(LocalMatchIds.LocalPlayerId, out var vote) && vote;

            if (!confirmations.ContainsKey(LocalMatchIds.LocalPlayerId))
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
