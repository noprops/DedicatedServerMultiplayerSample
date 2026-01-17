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
        [SerializeField] private CountdownMultiButton choiceButtonsCountdown;
        [SerializeField] private float choiceButtonCountdownSeconds = 10f;

        [Header("Result Panel")]
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private TMP_Text myHandText;
        [SerializeField] private TMP_Text yourHandText;
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private CountdownMultiButton continueQuitButtons;
        [SerializeField] private Button continueButton;
        [SerializeField] private float endButtonCountdownSeconds = 10f;
        [SerializeField] private ModalLayerUI modalLayer;
        [SerializeField] private float abortPromptDurationSeconds = 5f;

        [SerializeField] private RpsGameEventChannel eventChannel;

        // Cancels the ongoing async UI loop (used when abort notifications arrive or the object is destroyed).
        private CancellationTokenSource _lifecycleCts;

        private void Awake()
        {
            choicePanel.SetActive(false);
            resultPanel.SetActive(false);
            statusText.text = "Waiting for players…";
            modalLayer?.Hide();
        }

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

            if (continueQuitButtons == null || continueButton == null)
            {
                Debug.LogError("[RockPaperScissorsUI] Buttons must be assigned.");
                enabled = false;
                return;
            }
            if (choiceButtonsCountdown == null)
            {
                Debug.LogError("[RockPaperScissorsUI] Choice countdown must be assigned.");
                enabled = false;
                return;
            }
            // Choice countdown buttons must map to rock/paper/scissors.
            if (choiceButtonsCountdown.ButtonCount < 3)
            {
                Debug.LogError("[RockPaperScissorsUI] Choice buttons must include rock/paper/scissors.");
                enabled = false;
                return;
            }

            _lifecycleCts = new CancellationTokenSource();
            _ = RunUiLoopAsync(_lifecycleCts.Token);
        }

        private void OnDestroy()
        {
            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = null;
        }

        /// <summary>
        /// Main UI loop: wait for round start → collect input → show result → send continue/quit decision. Loops until quit/timeout.
        /// </summary>
        private async Task RunUiLoopAsync(CancellationToken token)
        {
            try
            {
                await eventChannel.WaitForChannelReadyAsync(token);
                eventChannel.GameAborted += HandleGameAborted;

                var (myName, opponentName) = await eventChannel.WaitForPlayersReadyAsync(token);

                while (!token.IsCancellationRequested)
                {
                    statusText.text = "Waiting for round start...";
                    await eventChannel.WaitForRoundStartedAsync(token);
                    ShowChoicePanel(myName, opponentName);

                    var selected = await WaitForLocalChoiceAsync(token);
                    statusText.text = "Waiting for opponent to select...";
                    eventChannel.RaiseChoiceSelected(selected);

                    var result = await eventChannel.WaitForRoundResultAsync(token);
                    ShowResult(result.outcome, result.myHand, result.opponentHand, result.canContinue);

                    var selection = await continueQuitButtons.RunAsync(endButtonCountdownSeconds);
                    var continueGame = selection.Reason == CountdownCompletionReason.Clicked
                        && selection.ClickedButton == continueButton;
                    eventChannel.RaiseRoundResultConfirmed(continueGame);

                    if (!continueGame)
                    {
                        eventChannel.RaiseGameEndRequested();
                        break;
                    }

                    statusText.text = "Waiting for opponent to continue...";
                    var continueDecision = await eventChannel.WaitForContinueDecisionAsync(token);
                    if (!continueDecision)
                    {
                        eventChannel.RaiseGameEndRequested();
                        break;
                    }
                }
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
            choiceButtonsCountdown.SetButtonsActive(true);
            choiceButtonsCountdown.SetButtonsInteractable(true);

            myNameText.text = myName;
            yourNameText.text = opponentName;
            statusText.text = "Make your choice!";
        }

        private void ShowResult(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)
        {
            choicePanel.SetActive(false);
            resultPanel.SetActive(true);
            ResetResultButtons();
            continueButton.gameObject.SetActive(canContinue);
            continueQuitButtons.gameObject.SetActive(true);

            myHandText.text = myHand.ToString();
            yourHandText.text = opponentHand.ToString();
            resultText.text = outcome.ToString();

            statusText.text = "Round resolved";
        }

        private void ResetResultButtons()
        {
            // Re-enable and show all buttons before deciding visibility.
            continueQuitButtons.SetButtonsActive(true);
            continueQuitButtons.SetButtonsInteractable(true);
        }

        private async Task<Hand> WaitForLocalChoiceAsync(CancellationToken token)
        {
            // Ensure buttons are visible/enabled before starting the countdown.
            choiceButtonsCountdown.SetButtonsActive(true);
            choiceButtonsCountdown.SetButtonsInteractable(true);

            var countdownTask = choiceButtonsCountdown.RunAsync(choiceButtonCountdownSeconds);
            using (token.Register(choiceButtonsCountdown.Cancel))
            {
                var result = await countdownTask;
                token.ThrowIfCancellationRequested();

                choiceButtonsCountdown.SetButtonsInteractable(false);

                return result.Reason == CountdownCompletionReason.Clicked
                    ? IndexToHand(result.ClickedIndex)
                    : HandExtensions.RandomHand();
            }
        }

        private Hand IndexToHand(int index)
        {
            // 0:rock, 1:paper, 2:scissors の想定で対応付け
            return index switch
            {
                0 => Hand.Rock,
                1 => Hand.Paper,
                2 => Hand.Scissors,
                _ => HandExtensions.RandomHand()
            };
        }

        private void ShowAbortPrompt(string reason)
        {
            var callback = eventChannel != null
                ? new Action(eventChannel.RaiseGameEndRequested)
                : null;

            modalLayer?.Show(
                string.IsNullOrWhiteSpace(reason) ? "Game aborted." : reason,
                callback,
                true,
                abortPromptDurationSeconds);
        }

    }
}
