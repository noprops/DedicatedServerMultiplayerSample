using System;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI
{
    /// <summary>
    /// Minimal UI helper that exposes clear entry points for waiting on player input and showing results.
    /// </summary>
    public sealed class RockPaperScissorsUI : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button rockButton;
        [SerializeField] private Button paperButton;
        [SerializeField] private Button scissorsButton;

        [Header("Status")]
        [SerializeField] private TMP_Text statusText;

        [Header("Result")]
        [SerializeField] private TMP_Text resultText;

        /// <summary>
        /// Waits for the local player to choose a hand. Returns null if the operation is cancelled.
        /// </summary>
        public async Task<Hand?> WaitForPlayerHandAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<Hand?>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnRock() => tcs.TrySetResult(Hand.Rock);
            void OnPaper() => tcs.TrySetResult(Hand.Paper);
            void OnScissors() => tcs.TrySetResult(Hand.Scissors);

            rockButton.onClick.AddListener(OnRock);
            paperButton.onClick.AddListener(OnPaper);
            scissorsButton.onClick.AddListener(OnScissors);

            try
            {
                using (ct.Register(() => tcs.TrySetResult(null)))
                {
                    SetStatus("手を選んでください");
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                rockButton.onClick.RemoveListener(OnRock);
                paperButton.onClick.RemoveListener(OnPaper);
                scissorsButton.onClick.RemoveListener(OnScissors);
            }
        }

        /// <summary>
        /// Displays the round outcome using simple emoji and text so that the player understands what happened.
        /// </summary>
        public void ShowResult(RpsResult result)
        {
            if (resultText != null)
            {
                resultText.text = $"あなた: {ToEmoji(result.H1)} / 相手: {ToEmoji(result.H2)} → {ToOutcomeText((RoundOutcome)result.P1Outcome)}";
            }

            SetStatus(string.Empty);
        }

        /// <summary>
        /// Updates the status label with a short message.
        /// </summary>
        public void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        private static string ToEmoji(Hand hand) => hand switch
        {
            Hand.Rock => "✊",
            Hand.Paper => "🖐",
            Hand.Scissors => "✌",
            _ => "？"
        };

        private static string ToOutcomeText(RoundOutcome outcome) => outcome switch
        {
            RoundOutcome.Win => "勝ち",
            RoundOutcome.Draw => "引き分け",
            RoundOutcome.Lose => "負け",
            _ => string.Empty
        };
    }
}
