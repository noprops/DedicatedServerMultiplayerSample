using System;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Samples.Client.UI.Common;
using DedicatedServerMultiplayerSample.Samples.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Game
{
    /// <summary>
    /// Rock-paper-scissors UI that drives the local player's flow through a single async routine.
    /// </summary>
    public sealed class RockPaperScissorsUI : MonoBehaviour
    {
        [Header("Name and Status")]
        [SerializeField] private TMP_Text myNameText;
        [SerializeField] private TMP_Text yourNameText;
        [SerializeField] private TMP_Text statusText;

        [Header("Choice Panel")]
        [SerializeField] private GameObject choicePanel;
        [SerializeField] private Button rockButton;
        [SerializeField] private Button paperButton;
        [SerializeField] private Button scissorsButton;

        [Header("Result Panel")]
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private TMP_Text myHandText;
        [SerializeField] private TMP_Text yourHandText;
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private CountdownButton endButton;
        [SerializeField] private float endButtonCountdownSeconds = 10f;
        [SerializeField] private ModalLayerUI modalLayer;
        [SerializeField] private float abortPromptDurationSeconds = 5f;

        [SerializeField] private RpsGameEventChannel eventChannel;

        // Cancels the ongoing async UI loop (used when abort notifications arrive or the object is destroyed).
        private CancellationTokenSource _lifecycleCts;

        /// <summary>
        /// Initializes UI widgets so nothing is visible/interactable before the first round begins.
        /// </summary>
        private void Awake()
        {
            choicePanel.SetActive(false);
            resultPanel.SetActive(false);
            statusText.text = "Waiting for players…";
            modalLayer?.Hide();
        }

        /// <summary>
        /// Starts the asynchronous UI loop once the dispatcher is assigned.
        /// </summary>
        private void Start()
        {
            if (_lifecycleCts != null)
            {
                return;
            }

            if (eventChannel == null)
            {
                Debug.LogError("[RockPaperScissorsUI] Event channel must be assigned.");
                enabled = false;
                return;
            }

            if (endButton == null)
            {
                Debug.LogError("[RockPaperScissorsUI] Countdown/OK button is not assigned.");
                enabled = false;
                return;
            }

            _lifecycleCts = new CancellationTokenSource();
            _ = RunUiLoopAsync(_lifecycleCts.Token);
        }

        /// <summary>
        /// Cancels the running loop and clears any leftover listeners.
        /// </summary>
        private void OnDestroy()
        {
            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = null;
            DetachButtonHandlers();
        }

        /// <summary>
        /// Main sequential UI routine (channel ready → show round → collect input → show results).
        /// </summary>
        private async Task RunUiLoopAsync(CancellationToken token)
        {
            try
            {
                await eventChannel.WaitUntilReadyAsync(token);
                eventChannel.GameAborted += HandleGameAborted;

                var intro = await WaitForRoundStartedAsync(token);
                ShowChoicePanel(intro.myName, intro.opponentName);

                var selected = await WaitForLocalChoiceAsync(token);
                statusText.text = "Waiting for opponent to select...";
                eventChannel.RaiseChoiceSelected(selected);

                var result = await WaitForRoundResultAsync(token);
                ShowResult(result.myOutcome, result.myHand, result.opponentHand);

                await WaitForEndButtonAsync(token);
                eventChannel.RaiseRoundResultConfirmed();
            }
            catch (OperationCanceledException)
            {
                // Aborts handled separately in HandleGameAborted.
            }
            finally
            {
                if (eventChannel != null)
                {
                    eventChannel.GameAborted -= HandleGameAborted;
                }

                DetachButtonHandlers();
            }
        }

        private void HandleGameAborted(string reason)
        {
            _lifecycleCts?.Cancel();
            ShowAbortPrompt(string.IsNullOrWhiteSpace(reason) ? "Game aborted." : reason);
        }

        private void ShowChoicePanel(string myName, string opponentName)
        {
            choicePanel.SetActive(true);
            resultPanel.SetActive(false);
            SetChoiceButtonsInteractable(true);

            myNameText.text = myName;
            yourNameText.text = opponentName;
            statusText.text = "Make your choice!";
        }

        private void ShowResult(RoundOutcome outcome, Hand myHand, Hand opponentHand)
        {
            choicePanel.SetActive(false);
            resultPanel.SetActive(true);

            myHandText.text = myHand.ToString();
            yourHandText.text = opponentHand.ToString();
            resultText.text = outcome.ToString();

            statusText.text = "Round resolved";
        }

        private async Task<(string myName, string opponentName)> WaitForRoundStartedAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(string myName, string opponentName)
            {
                eventChannel.RoundStarted -= Handler;
                tcs.TrySetResult((myName, opponentName));
            }

            eventChannel.RoundStarted += Handler;
            using (token.Register(() =>
                   {
                       eventChannel.RoundStarted -= Handler;
                       tcs.TrySetCanceled(token);
                   }))
            {
                return await tcs.Task;
            }
        }

        private async Task<Hand> WaitForLocalChoiceAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<Hand>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(Hand hand)
            {
                Debug.Log($"[RockPaperScissorsUI] Local selection: {hand}");
                SetChoiceButtonsInteractable(false);
                DetachButtonHandlers();
                tcs.TrySetResult(hand);
            }

            AttachButtonHandlers(Handler);
            using (token.Register(() =>
                   {
                       DetachButtonHandlers();
                       tcs.TrySetCanceled(token);
                   }))
            {
                return await tcs.Task;
            }
        }

        private void AttachButtonHandlers(Action<Hand> handler)
        {
            rockButton.onClick.AddListener(() => handler(Hand.Rock));
            paperButton.onClick.AddListener(() => handler(Hand.Paper));
            scissorsButton.onClick.AddListener(() => handler(Hand.Scissors));
        }

        private void DetachButtonHandlers()
        {
            rockButton.onClick.RemoveAllListeners();
            paperButton.onClick.RemoveAllListeners();
            scissorsButton.onClick.RemoveAllListeners();
        }

        private void SetChoiceButtonsInteractable(bool value)
        {
            rockButton.interactable = value;
            paperButton.interactable = value;
            scissorsButton.interactable = value;
        }

        private async Task<(RoundOutcome myOutcome, Hand myHand, Hand opponentHand)> WaitForRoundResultAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<(RoundOutcome, Hand, Hand)>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(RoundOutcome outcome, Hand myHand, Hand opponentHand)
            {
                eventChannel.RoundResultReady -= Handler;
                tcs.TrySetResult((outcome, myHand, opponentHand));
            }

            eventChannel.RoundResultReady += Handler;
            using (token.Register(() =>
                   {
                       eventChannel.RoundResultReady -= Handler;
                       tcs.TrySetCanceled(token);
                   }))
            {
                return await tcs.Task;
            }
        }

        private async Task WaitForEndButtonAsync(CancellationToken token)
        {
            using (token.Register(endButton.Cancel))
            {
                await endButton.RunAsync(endButtonCountdownSeconds);
            }
        }

        private void ShowAbortPrompt(string reason)
        {
            var callback = eventChannel != null
                ? () => eventChannel.RaiseGameAbortConfirmed()
                : (Action)null;

            modalLayer?.Show(
                string.IsNullOrWhiteSpace(reason) ? "Game aborted." : reason,
                callback,
                true,
                abortPromptDurationSeconds);
        }
    }
}
