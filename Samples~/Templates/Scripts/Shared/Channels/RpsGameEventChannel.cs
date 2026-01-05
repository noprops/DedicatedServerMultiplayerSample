using System;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Base behaviour exposing notifications shared between UI and gameplay layers.
    /// Derived classes decide how notifications travel (e.g., Netcode RPC vs. local call),
    /// while UI/gameplay code binds to this common surface.
    /// </summary>
    public abstract partial class RpsGameEventChannel : MonoBehaviour
    {
        // ==== Channel readiness ====

        /// <summary>
        /// Raised once the channel is ready for UI/gameplay components to subscribe.
        /// </summary>
        public event Action ChannelReady;

        internal virtual void InvokeChannelReady()
        {
            if (_channelReadyTcs.Task.IsCompleted)
            {
                return;
            }

            _channelReadyTcs.TrySetResult(true);
            ChannelReady?.Invoke();
        }

        // ==== UI -> Game logic ====

        // UI calls (implemented by derived channels)
        /// <summary>
        /// UI -> game notifications (raised by the UI, consumed by gameplay systems).
        /// </summary>

        // Choice selected.
        public abstract void RaiseChoiceSelected(Hand choice);
        public abstract void RaiseChoiceSelectedForPlayer(ulong playerId, Hand hand);
        public event Action<ulong, Hand> ChoiceSelected;
        internal void InvokeChoiceSelected(ulong playerId, Hand hand)
        {
            ChoiceSelected?.Invoke(playerId, hand);
        }

        // Result confirmation.
        public abstract void RaiseRoundResultConfirmed(bool continueGame);
        public event Action<ulong, bool> RoundResultConfirmed;
        protected internal void InvokeRoundResultConfirmed(ulong playerId, bool continueGame)
        {
            RoundResultConfirmed?.Invoke(playerId, continueGame);
        }

        // Abort confirmation.
        public abstract void RaiseGameAbortConfirmed();
        public event Action GameAbortConfirmed;
        protected internal void InvokeGameAbortConfirmed()
        {
            GameAbortConfirmed?.Invoke();
        }

        // ==== Game logic -> UI ====

        /// <summary>
        /// Game -> UI notifications (raised by gameplay, consumed by UI).
        /// </summary>

        // Players ready.
        public abstract void RaisePlayersReady(ulong player1Id, string player1Name, ulong player2Id, string player2Name);
        public event Action<string, string> PlayersReady;
        protected internal void InvokePlayersReady(string myName, string opponentName)
        {
            _playersReadyTcs.TrySetResult((myName, opponentName));
            PlayersReady?.Invoke(myName, opponentName);
        }

        // Round started.
        public abstract void RaiseRoundStarted();
        public event Action RoundStarted;
        protected internal void InvokeRoundStarted()
        {
            _roundStartedTcs.TrySetResult(true);
            RoundStarted?.Invoke();
        }

        // Continue decision.
        public abstract void RaiseContinueDecision(bool continueGame);
        public event Action<bool> ContinueDecision;
        protected internal void InvokeContinueDecision(bool continueGame)
        {
            _continueDecisionTcs.TrySetResult(continueGame);
            ContinueDecision?.Invoke(continueGame);
        }

        // Round result.
        public abstract void RaiseRoundResult(
            ulong player1Id,
            RoundOutcome player1Outcome,
            Hand player1Hand,
            ulong player2Id,
            RoundOutcome player2Outcome,
            Hand player2Hand,
            bool canContinue);
        public event Action<RoundOutcome, Hand, Hand, bool> RoundResultReady;
        protected internal void InvokeRoundResult(RoundOutcome myOutcome, Hand myHand, Hand opponentHand, bool canContinue)
        {
            _roundResultTcs.TrySetResult((myOutcome, myHand, opponentHand, canContinue));
            RoundResultReady?.Invoke(myOutcome, myHand, opponentHand, canContinue);
        }

        // Game aborted.
        public abstract void RaiseGameAborted(string message);
        public event Action<string> GameAborted;
        protected internal void InvokeGameAborted(string message)
        {
            GameAborted?.Invoke(message);
        }

    }
}
