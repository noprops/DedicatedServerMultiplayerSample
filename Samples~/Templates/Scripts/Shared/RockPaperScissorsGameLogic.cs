using System;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Pure rock-paper-scissors rules engine that orchestrates round flow independent of transport or UI.
    /// </summary>
    public sealed class RockPaperScissorsGameLogic
    {
        /// <summary>
        /// Runs a full round by awaiting hand submissions provided by the caller and applying a timeout fallback.
        /// The caller supplies asynchronous waiters for each participant, a random hand generator, and cancellation.
        /// </summary>
        public async Task<RpsResult> RunRoundAsync(
            ulong player0,
            ulong player1,
            TimeSpan timeout,
            Func<CancellationToken, Task<Hand?>> waitPlayer0Async,
            Func<CancellationToken, Task<Hand?>> waitPlayer1Async,
            Func<Hand> handProvider,
            CancellationToken cancellationToken = default)
        {
            if (waitPlayer0Async == null)
            {
                throw new ArgumentNullException(nameof(waitPlayer0Async));
            }

            if (waitPlayer1Async == null)
            {
                throw new ArgumentNullException(nameof(waitPlayer1Async));
            }

            if (handProvider == null)
            {
                throw new ArgumentNullException(nameof(handProvider));
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var task0 = waitPlayer0Async(timeoutCts.Token);
            var task1 = waitPlayer1Async(timeoutCts.Token);

            Hand hand0;
            Hand hand1;

            try
            {
                await Task.WhenAll(task0, task1).ConfigureAwait(false);
                hand0 = ResolveHand(task0, handProvider);
                hand1 = ResolveHand(task1, handProvider);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // timeout path
                hand0 = ResolveHand(task0, handProvider);
                hand1 = ResolveHand(task1, handProvider);
            }

            return Resolve(player0, player1, hand0, hand1);
        }

        /// <summary>
        /// Resolves the round outcome for the provided hands and participants.
        /// </summary>
        public static RpsResult Resolve(ulong player0, ulong player1, Hand hand0, Hand hand1)
        {
            var outcome0 = DetermineOutcome(hand0, hand1);
            var outcome1 = DetermineOutcome(hand1, hand0);

            return new RpsResult
            {
                P1 = player0,
                P2 = player1,
                H1 = hand0,
                H2 = hand1,
                P1Outcome = (byte)outcome0,
                P2Outcome = (byte)outcome1
            };
        }

        private static Hand ResolveHand(Task<Hand?> task, Func<Hand> handProvider)
        {
            if (task.IsCompletedSuccessfully && task.Result.HasValue && task.Result.Value is Hand.Rock or Hand.Paper or Hand.Scissors)
            {
                return task.Result.Value;
            }

            return handProvider();
        }

        /// <summary>
        /// Computes the outcome for the first hand against the opponent hand.
        /// </summary>
        public static RoundOutcome DetermineOutcome(Hand myHand, Hand opponentHand)
        {
            if (myHand == opponentHand)
            {
                return RoundOutcome.Draw;
            }

            return ((myHand == Hand.Rock && opponentHand == Hand.Scissors) ||
                    (myHand == Hand.Paper && opponentHand == Hand.Rock) ||
                    (myHand == Hand.Scissors && opponentHand == Hand.Paper))
                ? RoundOutcome.Win
                : RoundOutcome.Lose;
        }
    }
}
