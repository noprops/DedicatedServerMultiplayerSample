using System;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Samples.Shared;
using UnityEngine;

/// <summary>
/// Local player versus CPU game loop that reuses the shared game logic and UI.
/// </summary>
public sealed class LocalCpuGame : MonoBehaviour
{
    private const ulong HumanId = 1;
    private const ulong CpuId = 100;

    [SerializeField] private LocalGameEventDispatcher eventChannel;
    [SerializeField] private float roundTimeoutSeconds = 30f;
    [SerializeField] private float cpuDecisionDelaySeconds = 0.5f;

    private RpsGameEventChannel _eventChannel;
    private RockPaperScissorsGameLogic _logic;
    private bool _roundActive;

    private void Awake()
    {
        if (eventChannel == null)
        {
            throw new InvalidOperationException("Assign LocalGameEventDispatcher to LocalCpuGame.");
        }

        _eventChannel = eventChannel;
        _eventChannel.ChannelReady += HandleChannelReady;
        _eventChannel.ChoiceSelected += HandleLocalChoice;
        _eventChannel.RoundResultConfirmed += HandleRoundResultConfirmed;
    }

    private void OnEnable()
    {
        if (_eventChannel.IsChannelReady)
        {
            StartNewRound();
        }
    }

    private void OnDisable()
    {
        EndRound();
    }

    private void OnDestroy()
    {
        if (_eventChannel != null)
        {
            _eventChannel.ChannelReady -= HandleChannelReady;
            _eventChannel.ChoiceSelected -= HandleLocalChoice;
            _eventChannel.RoundResultConfirmed -= HandleRoundResultConfirmed;
        }
        EndRound();
    }

    private void StartNewRound()
    {
        EndRound();

        _logic = new RockPaperScissorsGameLogic(
            new[] { HumanId, CpuId },
            TimeSpan.FromSeconds(roundTimeoutSeconds));

        _eventChannel.RaiseRoundStarted(HumanId, "You", "CPU");

        _roundActive = true;
        _ = RunRoundAsync();
    }

    private void EndRound()
    {
        _logic = null;
        _roundActive = false;
    }

    private async Task RunRoundAsync()
    {
        try
        {
            SubmitCpuHand();
            var result = await _logic.RunAsync();

            if (!_roundActive)
            {
                return;
            }

            var myHand = result.Player1Hand;
            var opponentHand = result.Player2Hand;
            var myOutcome = result.Player1Outcome;
            _eventChannel.RaiseRoundResult(HumanId, myOutcome, myHand, opponentHand);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LocalCpuGame] Unexpected error: {ex.Message}");
            _eventChannel.RaiseGameAborted(HumanId, "An error occurred");
        }
    }

    private void SubmitCpuHand()
    {
        if (_roundActive)
        {
            _logic?.SubmitHand(CpuId, HandExtensions.RandomHand());
        }
    }

    private void HandleChannelReady()
    {
        if (isActiveAndEnabled)
        {
            StartNewRound();
        }
    }

    private void HandleLocalChoice(ulong playerId, Hand hand)
    {
        if (playerId != HumanId)
        {
            return;
        }

        _logic?.SubmitHand(HumanId, hand);
    }

    private void HandleRoundResultConfirmed(ulong playerId)
    {
        if (playerId == HumanId && isActiveAndEnabled)
        {
            StartNewRound();
        }
    }
}
