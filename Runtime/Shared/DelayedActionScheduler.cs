#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Shared
{
    /// <summary>
    /// Runs an action after a configurable delay while exposing whether a run is currently scheduled.
    /// </summary>
    public sealed class DelayedActionScheduler : IDisposable
    {
        private CancellationTokenSource _cts;

        /// <summary>
        /// True when an action is pending execution (i.e. delay timer is currently running).
        /// </summary>
        public bool IsScheduled => _cts is { IsCancellationRequested: false };

        /// <summary>
        /// Schedules <paramref name="action"/> to run after <paramref name="delaySeconds"/>. Cancels
        /// any previous schedule before starting the new one.
        /// </summary>
        public void Schedule(float delaySeconds, Action action)
        {
            Schedule(TimeSpan.FromSeconds(delaySeconds), action);
        }

        /// <summary>
        /// Schedules <paramref name="action"/> to run after <paramref name="delay"/>. Cancels
        /// any previous schedule before starting the new one.
        /// </summary>
        public void Schedule(TimeSpan delay, Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var clampedDelay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(clampedDelay, token).ConfigureAwait(false);
                    if (!token.IsCancellationRequested)
                    {
                        action();
                    }
                }
                catch (TaskCanceledException)
                {
                    // expected when rescheduled
                }
                finally
                {
                    DisposeCurrentCts();
                }
            });
        }

        /// <summary>
        /// Cancels any pending action.
        /// </summary>
        public void Cancel()
        {
            if (_cts == null)
            {
                return;
            }

            _cts.Cancel();
            DisposeCurrentCts();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Cancel();
        }

        private void DisposeCurrentCts()
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
}
#endif
