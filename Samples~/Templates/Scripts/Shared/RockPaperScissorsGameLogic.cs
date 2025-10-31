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

        /// <summary>
        /// Runs a round from start to finish: waits for both player tasks, substitutes missing hands on timeout, and returns the result.
        /// </summary>
        public async Task<RpsResult> RunRoundAsync(
            ulong player1Id,
            ulong player2Id,
            TimeSpan timeout,
            Task<Hand?> player1HandTask,
            Task<Hand?> player2HandTask,
            CancellationToken cancellation = default)
        {
            if (player1HandTask == null) throw new ArgumentNullException(nameof(player1HandTask));
            if (player2HandTask == null) throw new ArgumentNullException(nameof(player2HandTask));
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeoutCts.CancelAfter(timeout);

            try
            {
                var combined = Task.WhenAll(player1HandTask, player2HandTask);
                await combined.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellation.IsCancellationRequested)
            {
                // timeout -> fall through to substitution logic
            }
            catch
            {
                if (!timeoutCts.IsCancellationRequested)
                {
                    throw;
                }
            }

            var hand1 = ResolveHand(player1HandTask);
            var hand2 = ResolveHand(player2HandTask);
            return Resolve(player1Id, player2Id, hand1, hand2);
        }

        /// <summary>
        /// Forms an <see cref="RpsResult"/> for the supplied identifiers and hands by running the deterministic outcome table.
        /// </summary>
        public static RpsResult Resolve(ulong player1Id, ulong player2Id, Hand player1Hand, Hand player2Hand)
        {
            return new RpsResult
            {
                Player1Id = player1Id,
                Player2Id = player2Id,
                Player1Hand = player1Hand,
                Player2Hand = player2Hand,
                Player1Outcome = DetermineOutcome(player1Hand, player2Hand),
                Player2Outcome = DetermineOutcome(player2Hand, player1Hand)
            };
        }

        /// <summary>
        /// Produces the effective hand for a player. If the task did not complete with a valid hand, a random hand is assigned instead of leaving it empty.
        /// </summary>
        private static Hand ResolveHand(Task<Hand?> task)
        {
            if (task != null && task.IsCompletedSuccessfully && task.Result is Hand value && value != Hand.None)
            {
                return value;
            }

            return HandExtensions.RandomHand();
        }

        /// <summary>
        /// Computes the round outcome (win/draw/lose) for <paramref name="me"/> against <paramref name="opponent"/>.
        /// </summary>
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
    }
}
