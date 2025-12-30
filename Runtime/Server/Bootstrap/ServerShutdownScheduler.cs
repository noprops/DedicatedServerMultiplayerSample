using System;
using DedicatedServerMultiplayerSample.Server.Core;
using DedicatedServerMultiplayerSample.Shared;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Bootstrap
{
    public sealed class ServerShutdownScheduler
    {
        private readonly DelayedActionScheduler _scheduler = new();

        public void Schedule(ShutdownKind kind, string reason, TimeSpan delay)
        {
            _scheduler.Cancel();
            Debug.Log($"[ServerShutdownScheduler] Shutdown ({kind}) in {delay.TotalSeconds}s : {reason}");
            _scheduler.Schedule(delay, () =>
            {
                Debug.Log($"[ServerShutdownScheduler] Executing shutdown ({kind}) : {reason}");
                Application.Quit();
            });
        }

        public void Cancel() => _scheduler.Cancel();
    }
}
