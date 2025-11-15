using System;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Samples.Client.UI;
using DedicatedServerMultiplayerSample.Samples.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    [SerializeField] private float abortPromptDurationSeconds = 2f;

    [SerializeField] private RpsGameEventChannel eventChannel;

    private CancellationTokenSource _lifecycleCts;
    private string _abortReason;


    /// <summary>
    /// Initializes UI widgets so nothing is visible/interactable before the first round begins.
    /// </summary>
    private void Awake()
    {
        choicePanel.SetActive(false);
        resultPanel.SetActive(false);
        statusText.text = string.Empty;
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
        _abortReason = null;
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
            await eventChannel.WaitUntilReadyAsync(token).ConfigureAwait(false);
            eventChannel.GameAborted += HandleGameAborted;

            var intro = await WaitForRoundStartedAsync(token);
            ShowChoicePanel(intro.myName, intro.opponentName);

            var selected = await WaitForLocalChoiceAsync(token);
            eventChannel.RaiseChoiceSelected(selected);

            var result = await WaitForRoundResultAsync(token);
            ShowResult(result.myOutcome, result.myHand, result.opponentHand);

            await WaitForOkAsync(token);
            eventChannel.RaiseRoundResultConfirmed();
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(_abortReason))
            {
                ShowAbortPrompt(_abortReason);
                _abortReason = null;
            }
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

    // === Await helpers ======================================================

    /// <summary>
    /// Awaits the next RoundStarted notification and returns the provided names.
    /// </summary>
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

    /// <summary>
    /// Awaits the next RoundResult notification and returns the final outcome payload.
    /// </summary>
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

    /// <summary>
    /// Enables the choice buttons and waits until the player picks a hand (or the token cancels).
    /// </summary>
    private async Task<Hand> WaitForLocalChoiceAsync(CancellationToken token)
    {
        var tcs = new TaskCompletionSource<Hand>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetChoiceButtonsInteractable(true);

        void Resolve(Hand hand)
        {
            if (tcs.Task.IsCompleted)
            {
                return;
            }

            SetChoiceButtonsInteractable(false);
            SetStatus("Waiting for opponent...");
            tcs.TrySetResult(hand);
        }

        rockButton.onClick.AddListener(() => Resolve(Hand.Rock));
        paperButton.onClick.AddListener(() => Resolve(Hand.Paper));
        scissorsButton.onClick.AddListener(() => Resolve(Hand.Scissors));

        using (token.Register(() => tcs.TrySetCanceled(token)))
        {
            try
            {
                return await tcs.Task;
            }
            finally
            {
                rockButton.onClick.RemoveAllListeners();
                paperButton.onClick.RemoveAllListeners();
                scissorsButton.onClick.RemoveAllListeners();
                SetChoiceButtonsInteractable(false);
            }
        }
    }

    /// <summary>
    /// Enables the OK button and waits until the player confirms the result screen.
    /// </summary>
    private async Task WaitForOkAsync(CancellationToken token)
    {
        using (token.Register(() => endButton.Cancel()))
        {
            await endButton.RunAsync(endButtonCountdownSeconds);
        }
    }


    /// <summary>
    /// Cancels the main loop when the dispatcher reports an abnormal termination.
    /// </summary>
    private void HandleGameAborted(string message)
    {
        _abortReason = string.IsNullOrWhiteSpace(message) ? "Match aborted" : message;
        _lifecycleCts?.Cancel();
    }

    // === UI helpers =========================================================

    /// <summary>
    /// Shows the choice panel with the supplied player names.
    /// </summary>
    private void ShowChoicePanel(string myName, string opponentName)
    {
        myNameText.text = myName ?? string.Empty;
        yourNameText.text = opponentName ?? string.Empty;
        statusText.text = "Choose your hand";

        resultPanel.SetActive(false);
        choicePanel.SetActive(true);
        SetChoiceButtonsInteractable(true);
    }

    /// <summary>
    /// Shows the final round result and hides the choice panel.
    /// </summary>
    private void ShowResult(RoundOutcome myResult, Hand myHand, Hand yourHand)
    {
        choicePanel.SetActive(false);
        resultPanel.SetActive(true);
        SetChoiceButtonsInteractable(false);

        myHandText.text = myHand.ToString();
        yourHandText.text = yourHand.ToString();
        resultText.text = myResult.ToString();
        statusText.text = "Round finished";
    }

    /// <summary>
    /// Updates the status label with the provided text.
    /// </summary>
    private void SetStatus(string message)
    {
        statusText.text = message ?? string.Empty;
    }

    /// <summary>
    /// Toggles all three choice buttons at once.
    /// </summary>
    private void SetChoiceButtonsInteractable(bool enabled)
    {
        rockButton.interactable = enabled;
        paperButton.interactable = enabled;
        scissorsButton.interactable = enabled;
    }

    /// <summary>
    /// Presents the abort prompt and notifies the dispatcher once the user dismisses it.
    /// </summary>
    private void ShowAbortPrompt(string message)
    {
        void CompleteAbort()
        {
            eventChannel.RaiseGameAbortConfirmed();
        }

        if (modalLayer != null)
        {
            modalLayer.Show(message, CompleteAbort, true, abortPromptDurationSeconds);
        }
        else
        {
            SetStatus(message);
            CompleteAbort();
        }
    }

    /// <summary>
    /// Removes every listener we may have registered on the buttons and resets interactability.
    /// </summary>
    private void DetachButtonHandlers()
    {
        rockButton.onClick.RemoveAllListeners();
        paperButton.onClick.RemoveAllListeners();
        scissorsButton.onClick.RemoveAllListeners();
        endButton.Cancel();
    }
}
