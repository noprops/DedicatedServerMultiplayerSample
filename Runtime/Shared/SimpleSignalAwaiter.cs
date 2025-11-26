using System;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Shared
{
    /// <summary>
    /// Awaitable helper that completes when <see cref="OnSignal"/> is invoked, a timeout elapses, or a cancellation token fires.
    /// Designed for scenarios where you want to manually wire an event into an async flow without juggling TaskCompletionSource yourself.
    /// </summary>
    public sealed class SimpleSignalAwaiter : IDisposable
    {
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly object _gate = new();
        private bool _signalled;
        private bool _disposed;

        public SimpleSignalAwaiter(TimeSpan timeout, CancellationToken cancellation = default)
        {
            TimeoutDuration = timeout;

            if (cancellation.CanBeCanceled)
            {
                _cancellationRegistration = cancellation.Register(() =>
                {
                    lock (_gate)
                    {
                        if (_signalled || _disposed)
                        {
                            return;
                        }
                    }
                    _completion.TrySetCanceled(cancellation);
                });
            }
        }

        public TimeSpan TimeoutDuration { get; }

        public SimpleSignalAwaiter(CancellationToken cancellation = default)
            : this(TimeSpan.Zero, cancellation)
        {
        }

        /// <summary>
        /// Call this from the event you want to await.
        /// </summary>
        public void OnSignal()
        {
            lock (_gate)
            {
                if (_signalled || _disposed)
                {
                    return;
                }

                _signalled = true;
            }

            _completion.TrySetResult(true);
        }

        /// <summary>
        /// Waits until <see cref="OnSignal"/> is called, the timeout expires, or the token cancels.
        /// Returns true if signalled, false if timed out, and throws when cancellation is requested.
        /// </summary>
        public async Task<bool> WaitAsync(CancellationToken cancellation = default)
        {
            ThrowIfDisposed();

            Task timeoutTask = TimeoutDuration > TimeSpan.Zero
                ? Task.Delay(TimeoutDuration, cancellation)
                : Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellation);

            var completed = await Task.WhenAny(_completion.Task, timeoutTask);

            if (completed == _completion.Task)
            {
                await _completion.Task;
                return true;
            }

            cancellation.ThrowIfCancellationRequested();
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SimpleSignalAwaiter));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellationRegistration.Dispose();
        }
    }
}
