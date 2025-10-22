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
        /// Awaits the task while allowing cancellation. Returns the result if completed; throws if cancelled first.
        /// </summary>
        public static async Task<T> WaitOrCancel<T>(this Task<T> task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
            {
                return await task.ConfigureAwait(false);
            }

            var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return await task.ConfigureAwait(false);
        }

        /// <summary>
        /// Awaits the task without a return value while allowing cancellation.
        /// </summary>
        public static async Task WaitOrCancel(this Task task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            await task.ConfigureAwait(false);
        }

        /// <summary>
        /// Waits for a signal to be raised via subscription. Returns immediately if the condition is already met.
        /// </summary>
        public static async Task<bool> WaitSignalAsync(
            Func<bool> isAlreadyTrue,
            Action<Action> subscribe,
            Action<Action> unsubscribe,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            if (isAlreadyTrue())
            {
                return true;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler() => tcs.TrySetResult(true);

            subscribe(Handler);

            try
            {
                Task timeoutTask = timeout <= TimeSpan.Zero
                    ? Task.Delay(Timeout.InfiniteTimeSpan, ct)
                    : Task.Delay(timeout, ct);

                var completed = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
                if (completed == tcs.Task)
                {
                    await tcs.Task.ConfigureAwait(false);
                    return true;
                }

                ct.ThrowIfCancellationRequested();
                return false;
            }
            finally
            {
                unsubscribe(Handler);
            }
        }
    }
}
