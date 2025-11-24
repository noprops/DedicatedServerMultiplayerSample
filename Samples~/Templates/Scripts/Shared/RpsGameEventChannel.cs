using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Base behaviour that exposes the complete set of notifications shared between the UI and gameplay layers.
    /// Derived classes decide how the notifications travel (e.g., Netcode RPC vs. local call), while the UI
    /// always binds to this common surface.
    /// </summary>
    public abstract class RpsGameEventChannel : MonoBehaviour
    {
        /// <summary>
        /// Raised once the channel is ready for UI/gameplay components to subscribe.
        /// </summary>
        public event Action ChannelReady;

        private TaskCompletionSource<bool> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Indicates that <see cref="NotifyChannelReady"/> has already run.
        /// </summary>
        public bool IsChannelReady { get; private set; }

        /// <summary>
        /// Provides an awaitable that completes once the channel reports readiness.
        /// </summary>
        public Task WaitUntilReadyAsync(CancellationToken token = default)
        {
            if (IsChannelReady)
            {
                return Task.CompletedTask;
            }

            if (!token.CanBeCanceled)
            {
                return _readyTcs.Task;
            }

            if (token.IsCancellationRequested)
            {
                return Task.FromCanceled(token);
            }

            return WaitUntilReadyWithCancellationAsync(token);
        }

        /// <summary>
        /// Marks the channel as ready and notifies listeners exactly once.
        /// </summary>
        public virtual void NotifyChannelReady()
        {
            if (IsChannelReady)
            {
                return;
            }

            IsChannelReady = true;
            _readyTcs.TrySetResult(true);
            ChannelReady?.Invoke();
        }

        /// <summary>
        /// Resets the ready flag (call when despawning or reinitialising the dispatcher).
        /// </summary>
        protected internal void ResetChannelReadiness()
        {
            IsChannelReady = false;
            if (_readyTcs.Task.IsCompleted)
            {
                _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private async Task WaitUntilReadyWithCancellationAsync(CancellationToken token)
        {
            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelTcs.TrySetCanceled(token)))
            {
                var completed = await Task.WhenAny(_readyTcs.Task, cancelTcs.Task).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
            }
        }

        // ==== UI -> Game Logic ====

        /// <summary>
        /// Called by the UI when the local player selects a hand.
        /// Implementations must relay the request to the authoritative gameplay logic.
        /// </summary>
        public abstract void RaiseChoiceSelected(Hand choice);

        /// <summary>
        /// Fired when the gameplay layer receives a confirmed hand for a specific player.
        /// </summary>
        public event Action<ulong, Hand> ChoiceSelected;

        /// <summary>
        /// Notifies subscribers that a player's hand has been captured.
        /// </summary>
        protected internal void InvokeChoiceSelected(ulong playerId, Hand hand)
        {
            ChoiceSelected?.Invoke(playerId, hand);
        }

        /// <summary>
        /// Called by the UI when the player presses OK on the result dialog.
        /// </summary>
        public abstract void RaiseRoundResultConfirmed();

        /// <summary>
        /// Fired when the gameplay layer receives the confirmation signal from a client.
        /// </summary>
        public event Action<ulong> RoundResultConfirmed;

        /// <summary>
        /// Notifies subscribers that a player has acknowledged the round result.
        /// </summary>
        protected internal void InvokeRoundResultConfirmed(ulong playerId)
        {
            RoundResultConfirmed?.Invoke(playerId);
        }

        // ==== Game Logic -> UI ====

        /// <summary>
        /// Called by gameplay logic to instruct a specific client to show the choice UI with the supplied names.
        /// </summary>
        public abstract void RaiseRoundStarted(ulong targetClientId, string myName, string opponentName);

        /// <summary>
        /// Fired when the UI should display the round-start panel.
        /// </summary>
        public event Action<string, string> RoundStarted;

        /// <summary>
        /// Notifies subscribers that a round has officially begun.
        /// </summary>
        protected internal void InvokeRoundStarted(string myName, string opponentName)
        {
            RoundStarted?.Invoke(myName, opponentName);
        }

        /// <summary>
        /// Called by gameplay logic to deliver the final outcome to a specific client.
        /// </summary>
        public abstract void RaiseRoundResult(ulong targetClientId, RoundOutcome myOutcome, Hand myHand, Hand opponentHand);

        /// <summary>
        /// Fired when the UI should switch to the result view with the supplied data.
        /// </summary>
        public event Action<RoundOutcome, Hand, Hand> RoundResultReady;

        /// <summary>
        /// Notifies subscribers that a round result has been computed.
        /// </summary>
        protected internal void InvokeRoundResult(RoundOutcome myOutcome, Hand myHand, Hand opponentHand)
        {
            RoundResultReady?.Invoke(myOutcome, myHand, opponentHand);
        }

        /// <summary>
        /// Called by gameplay logic when the match must abort abnormally (including pre-start failures).
        /// Implementations should surface the provided message and prompt the client to exit.
        /// </summary>
        public abstract void RaiseGameAborted(ulong targetClientId, string message);

        /// <summary>
        /// Called by the UI once the player has confirmed the abort prompt.
        /// Implementations decide how to tear down the session (disconnect vs. scene reload).
        /// </summary>
        public abstract void RaiseGameAbortConfirmed();

        /// <summary>
        /// Fired when the UI needs to inform the player that the match ended abnormally.
        /// </summary>
        public event Action<string> GameAborted;

        /// <summary>
        /// Notifies subscribers that the match cannot continue.
        /// </summary>
        protected internal void InvokeGameAborted(string message)
        {
            GameAborted?.Invoke(message);
        }
    }
}
