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
        private readonly Action m_OnExecute;
        private CancellationTokenSource m_Cts;
        private Task m_ScheduledTask;

        public DeferredActionScheduler(Action onExecute)
        {
            m_OnExecute = onExecute ?? throw new ArgumentNullException(nameof(onExecute));
        }

        public async Task ScheduleAsync(string reason, float delaySeconds)
        {
            CancelCurrent();

            m_Cts = new CancellationTokenSource();
            var token = m_Cts.Token;

            Debug.Log($"[DeferredAction] scheduled in {delaySeconds}s : {reason}");
            m_ScheduledTask = RunDelayAsync(reason, delaySeconds, token);
            await m_ScheduledTask.ConfigureAwait(false);
        }

        private async Task RunDelayAsync(string reason, float delaySeconds, CancellationToken token)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                {
                    Debug.Log($"[DeferredAction] executing: {reason}");
                    m_OnExecute();
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
            if (m_Cts == null)
                return;

            try
            {
                m_Cts.Cancel();
                var task = m_ScheduledTask;
                m_ScheduledTask = null;

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
                m_Cts.Dispose();
                m_Cts = null;
            }
        }

        public void Dispose()
        {
            CancelCurrent();
        }
    }
}
