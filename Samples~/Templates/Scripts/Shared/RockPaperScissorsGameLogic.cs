using System;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Pure rock-paper-scissors rules engine; call <see cref="RunRoundAsync"/> to execute a single round.
    /// </summary>
    public sealed class RockPaperScissorsGameLogic
    {
        private static readonly Random Random = new();

        /// <summary>
        /// Runs a round from start to finish: waits for both player tasks, substitutes missing hands on timeout, and returns the result.
        /// </summary>
        public async Task<RpsResult> RunRoundAsync(
            ulong player0Id,
            ulong player1Id,
            TimeSpan timeout,
            Task<Hand?> player0HandTask,
            Task<Hand?> player1HandTask,
            CancellationToken cancellation = default)
        {
            if (player0HandTask == null) throw new ArgumentNullException(nameof(player0HandTask));
            if (player1HandTask == null) throw new ArgumentNullException(nameof(player1HandTask));
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await Task.WhenAll(player0HandTask, player1HandTask).WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellation.IsCancellationRequested)
            {
                // timeout -> fall through to substitution logic
            }

            var hand0 = ResolveHand(player0HandTask);
            var hand1 = ResolveHand(player1HandTask);
            return Resolve(player0Id, player1Id, hand0, hand1);
        }

        /// <summary>
        /// Pure function that evaluates outcomes given both hands.
        /// </summary>
        public static RpsResult Resolve(ulong player0Id, ulong player1Id, Hand hand0, Hand hand1)
        {
            return new RpsResult
            {
                P1 = player0Id,
                P2 = player1Id,
                H1 = hand0,
                H2 = hand1,
                P1Outcome = (byte)DetermineOutcome(hand0, hand1),
                P2Outcome = (byte)DetermineOutcome(hand1, hand0)
            };
        }

        private static Hand ResolveHand(Task<Hand?> task)
        {
            if (task != null && task.IsCompletedSuccessfully && task.Result is Hand value && value != Hand.None)
            {
                return value;
            }

            return GetRandomHand();
        }

        private static RoundOutcome DetermineOutcome(Hand me, Hand opponent)
        {
            if (me == opponent)
            {
                return RoundOutcome.Draw;
            }

            return ((me == Hand.Rock && opponent == Hand.Scissors) ||
                    (me == Hand.Paper && opponent == Hand.Rock) ||
                    (me == Hand.Scissors && opponent == Hand.Paper))
                ? RoundOutcome.Win
                : RoundOutcome.Lose;
        }

        private static Hand GetRandomHand()
        {
            var value = Random.Next(1, 4); // Rock/Paper/Scissors
            return (Hand)value;
        }
    }
}
