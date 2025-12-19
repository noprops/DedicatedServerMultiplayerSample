using System;
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

        /// <summary>
        /// Indicates that <see cref="NotifyChannelReady"/> has already run.
        /// </summary>
        public bool IsChannelReady { get; private set; }

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
            ChannelReady?.Invoke();
        }

        /// <summary>
        /// Resets the ready flag (call when despawning or reinitialising the dispatcher).
        /// </summary>
        protected internal void ResetChannelReadiness()
        {
            IsChannelReady = false;
        }

        // ==== UI -> Game Logic ====

        /// <summary>
        /// Called by the UI when the local player selects a hand.
        /// Implementations must relay the request to the authoritative gameplay logic.
        /// </summary>
        public abstract void RaiseChoiceSelected(Hand choice);

        /// <summary>
        /// Used by gameplay systems that need to relay a choice on behalf of a specific player
        /// (e.g., CPU submissions in local mode).
        /// </summary>
        public abstract void RaiseChoiceSelectedForPlayer(ulong playerId, Hand hand);

        /// <summary>
        /// Fired when the gameplay layer receives a confirmed hand for a specific player.
        /// </summary>
        public event Action<ulong, Hand> ChoiceSelected;

        /// <summary>
        /// Notifies subscribers that a player's hand has been captured.
        /// </summary>
        protected void InvokeChoiceSelected(ulong playerId, Hand hand)
        {
            ChoiceSelected?.Invoke(playerId, hand);
        }

        /// <summary>
        /// Called by the UI when the player presses OK on the result dialog.
        /// </summary>
        public abstract void RaiseRoundResultConfirmed(bool continueGame);

        /// <summary>
        /// Fired when the gameplay layer receives the confirmation signal from a client.
        /// </summary>
        public event Action<ulong, bool> RoundResultConfirmed;

        /// <summary>
        /// Notifies subscribers that a player has acknowledged the round result.
        /// </summary>
        protected internal void InvokeRoundResultConfirmed(ulong playerId, bool continueGame)
        {
            RoundResultConfirmed?.Invoke(playerId, continueGame);
        }

        /// <summary>
        /// Called by the UI once the player has confirmed the abort prompt.
        /// Implementations decide how to tear down the session (disconnect vs. scene reload).
        /// </summary>
        public abstract void RaiseGameAbortConfirmed();

        // ==== Game Logic -> UI ====

        /// <summary>
        /// Called by gameplay logic once, to inform clients that all players are ready and provide identities.
        /// </summary>
        public abstract void RaisePlayersReady(ulong player1Id, string player1Name, ulong player2Id, string player2Name);

        /// <summary>
        /// Fired when the UI should initialize with player identities.
        /// </summary>
        public event Action<string, string> PlayersReady;

        /// <summary>
        /// Notifies subscribers that a round has officially begun.
        /// </summary>
        protected internal void InvokePlayersReady(string myName, string opponentName)
        {
            PlayersReady?.Invoke(myName, opponentName);
        }

        /// <summary>
        /// Called by gameplay logic to deliver the final outcome to all clients.
        /// </summary>
        public abstract void RaiseRoundResult(
            ulong player1Id,
            RoundOutcome player1Outcome,
            Hand player1Hand,
            ulong player2Id,
            RoundOutcome player2Outcome,
            Hand player2Hand,
            bool canContinue);

        /// <summary>
        /// Fired when the UI should switch to the result view with the supplied data.
        /// </summary>
        public event Action<RoundOutcome, Hand, Hand, bool> RoundResultReady;

        /// <summary>
        /// Notifies subscribers that a round result has been computed.
        /// </summary>
        protected internal void InvokeRoundResult(RoundOutcome myOutcome, Hand myHand, Hand opponentHand, bool canContinue)
        {
            RoundResultReady?.Invoke(myOutcome, myHand, opponentHand, canContinue);
        }

        /// <summary>
        /// Called by gameplay logic when the match must abort abnormally (including pre-start failures).
        /// Implementations should surface the provided message and prompt the client to exit.
        /// </summary>
        public abstract void RaiseGameAborted(string message);

        /// <summary>
        /// Fired when the UI needs to inform the player that the match ended abnormally.
        /// </summary>
        public event Action<string> GameAborted;

        /// <summary>
        /// Fired when the player acknowledged the abort prompt.
        /// </summary>
        public event Action GameAbortConfirmed;

        /// <summary>
        /// Notifies subscribers that the match cannot continue.
        /// </summary>
        protected internal void InvokeGameAborted(string message)
        {
            GameAborted?.Invoke(message);
        }

        /// <summary>
        /// Notifies subscribers that the player confirmed abort.
        /// </summary>
        protected internal void InvokeGameAbortConfirmed()
        {
            GameAbortConfirmed?.Invoke();
        }
    }
}
