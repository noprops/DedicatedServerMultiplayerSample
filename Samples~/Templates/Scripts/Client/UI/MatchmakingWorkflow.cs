using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Client;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI
{
    public sealed class MatchmakingWorkflow
    {
        private readonly ClientMatchmaker matchmaker;
        private readonly StartCancelStatusView controls;

        public MatchmakingWorkflow(ClientMatchmaker matchmaker, StartCancelStatusView controls)
        {
            this.matchmaker = matchmaker ?? throw new ArgumentNullException(nameof(matchmaker));
            this.controls = controls ?? throw new ArgumentNullException(nameof(controls));
        }

        public async Task RunAsync(
            string queueName,
            Func<MatchmakingPayload> payloadFactory,
            CancellationToken ct = default)
        {
            string lastResult = null;

            while (!ct.IsCancellationRequested)
            {
                controls.SetStatus(lastResult ?? "Ready to start matchmaking");
                await WaitForStartAsync(ct);
                lastResult = null;
                controls.SetStatus("Starting matchmaking...");

                var payload = payloadFactory?.Invoke() ?? MatchmakingPayload.Empty;
                var result = await ExecuteMatchmakingAsync(queueName, payload, ct);

                switch (result)
                {
                    case MatchResult.Success:
                        controls.SetStatus("Connected!");
                        return;
                    case MatchResult.UserCancelled:
                        lastResult = "Cancelled by user";
                        break;
                    case MatchResult.Failed:
                        lastResult = "Connection failed";
                        break;
                    case MatchResult.Timeout:
                        lastResult = "Connection timeout";
                        break;
                }
            }
        }

        private async Task<MatchResult> ExecuteMatchmakingAsync(
            string queueName,
            MatchmakingPayload payload,
            CancellationToken ct)
        {
            controls.SetStatus("Searching for match...");
            void HandleStateChanged(ClientConnectionState state)
            {
                controls.SetStatus(state switch
                {
                    ClientConnectionState.SearchingMatch => "Searching for match...",
                    ClientConnectionState.MatchFound => "Match found! Preparing...",
                    ClientConnectionState.ConnectingToServer => "Connecting to server...",
                    ClientConnectionState.Connected => "Connected!",
                    ClientConnectionState.Cancelling => "Cancelling...",
                    ClientConnectionState.Cancelled => "Cancelled",
                    ClientConnectionState.Failed => "Connection failed",
                    _ => "Ready"
                });
            }

            matchmaker.StateChanged += HandleStateChanged;

            try
            {
                var cancelTask = WaitForCancelAsync(ct);
                var matchTask = matchmaker.MatchmakeAsync(
                    queueName,
                    payload.PlayerProperties,
                    payload.TicketAttributes,
                    payload.ConnectionPayload,
                    payload.SessionProperties);

                var completed = await Task.WhenAny(cancelTask, matchTask);
                if (completed == cancelTask)
                {
                    await matchmaker.CancelMatchmakingAsync();
                    return MatchResult.UserCancelled;
                }

                return await matchTask;
            }
            finally
            {
                matchmaker.StateChanged -= HandleStateChanged;
                controls.SetCancelInteractable(false);
                controls.ShowStartState();
            }
        }

        private Task WaitForStartAsync(CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler()
            {
                controls.StartPressed -= Handler;
                tcs.TrySetResult(true);
            }

            controls.ShowStartState();
            controls.StartPressed += Handler;

            return AttachCancellation(tcs, () => controls.StartPressed -= Handler, ct);
        }

        private Task WaitForCancelAsync(CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler()
            {
                controls.CancelPressed -= Handler;
                tcs.TrySetResult(true);
            }

            controls.ShowCancelState();
            controls.CancelPressed += Handler;

            return AttachCancellation(tcs, () => controls.CancelPressed -= Handler, ct);
        }

        private static async Task AttachCancellation(
            TaskCompletionSource<bool> tcs,
            Action unsubscribe,
            CancellationToken ct)
        {
            using var registration = ct.Register(() =>
            {
                unsubscribe();
                tcs.TrySetCanceled(ct);
            });

            await tcs.Task;
        }
    }

    public sealed class MatchmakingPayload
    {
        public static MatchmakingPayload Empty { get; } = new MatchmakingPayload();

        public MatchmakingPayload(
            Dictionary<string, object> playerProperties = null,
            Dictionary<string, object> ticketAttributes = null,
            Dictionary<string, object> connectionPayload = null,
            Dictionary<string, object> sessionProperties = null)
        {
            PlayerProperties = playerProperties;
            TicketAttributes = ticketAttributes;
            ConnectionPayload = connectionPayload;
            SessionProperties = sessionProperties;
        }

        public Dictionary<string, object> PlayerProperties { get; }
        public Dictionary<string, object> TicketAttributes { get; }
        public Dictionary<string, object> ConnectionPayload { get; }
        public Dictionary<string, object> SessionProperties { get; }
    }
}
