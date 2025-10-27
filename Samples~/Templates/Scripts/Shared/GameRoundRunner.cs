using System;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Simple helper that drives a single round of rock-paper-scissors using the shared game logic.
    /// </summary>
    public sealed class GameRoundRunner : IDisposable
    {
        private readonly RockPaperScissorsGameLogic _logic;
        private TaskCompletionSource<RpsResult> _roundCompletion;
        private bool _started;
        private bool _finished;

        public GameRoundRunner(RockPaperScissorsGameLogic logic)
        {
            _logic = logic ?? throw new ArgumentNullException(nameof(logic));
            _logic.RoundResolved += OnRoundResolved;
        }

        /// <summary>
        /// Initializes the round for the supplied participants. Safe to call multiple times; only the first call has an effect.
        /// </summary>
        public void Start(ulong participant0, ulong participant1)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _finished = false;
            _roundCompletion = new TaskCompletionSource<RpsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _logic.InitializeRound(participant0, participant1);
        }

        /// <summary>
        /// Attempts to submit a hand for the given slot (0 or 1). Returns false when the slot already chose or the round finished.
        /// </summary>
        public bool Submit(int slot, Hand hand)
        {
            if (!_started || _finished)
            {
                return false;
            }

            return _logic.TrySubmit(slot, hand);
        }

        /// <summary>
        /// Waits until the round resolves. When the timeout elapses, missing hands are generated via <paramref name="handProvider"/>.
        /// </summary>
        public async Task<RpsResult> RunAsync(TimeSpan timeout, Func<Hand> handProvider, CancellationToken cancellationToken)
        {
            if (!_started)
            {
                throw new InvalidOperationException("Start must be called before RunAsync.");
            }

            if (handProvider == null)
            {
                throw new ArgumentNullException(nameof(handProvider));
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            using var cancelRegistration = cancellationToken.Register(() =>
            {
                if (_finished)
                {
                    return;
                }

                _finished = true;
                _roundCompletion?.TrySetCanceled(cancellationToken);
            });

            using var timeoutRegistration = timeoutCts.Token.Register(() =>
            {
                if (_finished)
                {
                    return;
                }

                _logic.AssignIfMissing(handProvider);
                if (_logic.TryGetResult(out var resolved))
                {
                    _finished = true;
                    _roundCompletion?.TrySetResult(resolved);
                }
            });

            return await _roundCompletion.Task.ConfigureAwait(false);
        }

        private void OnRoundResolved(RpsResult result)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _roundCompletion?.TrySetResult(result);
        }

        public void Dispose()
        {
            _logic.RoundResolved -= OnRoundResolved;
        }
    }
}
