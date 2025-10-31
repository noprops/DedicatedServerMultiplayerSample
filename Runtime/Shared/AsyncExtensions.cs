using System;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Shared
{
    /// <summary>
    /// Helper extensions to await tasks while honouring cancellation tokens.
    /// </summary>
    public static class AsyncExtensions
    {
        /// <summary>
        /// Waits for a one-shot signal raised via subscribe/unsubscribe with an optional timeout.
        /// Returns true when the signal fires, false when the timeout elapses, and throws OperationCanceledException if the token is cancelled.
        /// </summary>
        public static async Task<bool> WaitSignalAsync(
            Action<Action> subscribe,
            Action<Action> unsubscribe,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler() => tcs.TrySetResult(true);

            subscribe(Handler);
            try
            {
                var timeoutTask = timeout <= TimeSpan.Zero
                    ? Task.Delay(Timeout.InfiniteTimeSpan, ct)
                    : Task.Delay(timeout, ct);

                var completed = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
                if (completed == tcs.Task)
                {
                    // Await again so any exception/cancellation raised inside the handler surfaces here.
                    await tcs.Task.ConfigureAwait(false);
                    return true;
                }

                // If the timeout finished first, honour cancellation (throws OperationCanceledException) or return false.
                ct.ThrowIfCancellationRequested();
                return false;
            }
            finally
            {
                unsubscribe(Handler); // ensure handler removal
            }
        }

        /// <summary>
        /// Waits for a one-shot signal without applying a timeout. Throws OperationCanceledException when cancelled.
        /// </summary>
        public static async Task<bool> WaitSignalAsync(
            Action<Action> subscribe,
            Action<Action> unsubscribe,
            CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler() => tcs.TrySetResult(true);

            subscribe(Handler);
            var reg = ct.CanBeCanceled ? ct.Register(() => tcs.TrySetCanceled(ct)) : default;

            try
            {
                await tcs.Task;
                return true;
            }
            finally
            {
                reg.Dispose();
                unsubscribe(Handler);
            }
        }
    }
}
