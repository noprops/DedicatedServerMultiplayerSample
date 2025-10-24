using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Shared
{
    /// <summary>
    /// Delays execution of a provided action with cancellation support. Subsequent scheduling replaces previous request.
    /// </summary>
    public sealed class DeferredActionScheduler : IDisposable
    {
        private readonly Action _onExecute;
        private CancellationTokenSource _cts;
        private Task _scheduledTask;

        public DeferredActionScheduler(Action onExecute)
        {
            _onExecute = onExecute ?? throw new ArgumentNullException(nameof(onExecute));
        }

        public async Task ScheduleAsync(string reason, float delaySeconds)
        {
            CancelCurrent();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Debug.Log($"[DeferredAction] scheduled in {delaySeconds}s : {reason}");
            _scheduledTask = RunDelayAsync(reason, delaySeconds, token);
            await _scheduledTask.ConfigureAwait(false);
        }

        private async Task RunDelayAsync(string reason, float delaySeconds, CancellationToken token)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                {
                    Debug.Log($"[DeferredAction] executing: {reason}");
                    _onExecute();
                }
            }
            catch (TaskCanceledException)
            {
                // expected when rescheduled
            }
        }

        public void Cancel()
        {
            CancelCurrent();
        }

        private void CancelCurrent()
        {
            if (_cts == null)
                return;

            try
            {
                _cts.Cancel();
                var task = _scheduledTask;
                _scheduledTask = null;

                if (task != null)
                {
                    try
                    {
                        task.Wait();
                    }
                    catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
                    {
                        // swallow cancellation
                    }
                }
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
            }
        }

        public void Dispose()
        {
            CancelCurrent();
        }
    }
}
