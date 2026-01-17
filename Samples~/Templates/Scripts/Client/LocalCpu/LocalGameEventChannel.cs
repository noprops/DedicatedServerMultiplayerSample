using DedicatedServerMultiplayerSample.Samples.Shared;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.LocalCpu
{
    /// <summary>
    /// Client-side implementation of the event channel for local (CPU) games.
    /// Bypasses networking and dispatches notifications locally.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalGameEventChannel : RpsGameEventChannel
    {
        private void Awake()
        {
            Debug.Log("[LocalGameEventChannel] Awake");
        }

        private void Start()
        {
            InvokeChannelReady();
            Debug.Log("[LocalGameEventChannel] Channel ready (local mode).");
        }

        // ==== UI -> Game Logic ====
        public override void RaiseChoiceSelected(Hand choice)
        {
            InvokeChoiceSelected(LocalMatchIds.LocalPlayerId, choice);
        }

        public override void RaiseChoiceSelectedForPlayer(ulong playerId, Hand hand)
        {
            InvokeChoiceSelected(playerId, hand);
        }

        public override void RaiseRoundResultConfirmed(bool continueGame)
        {
            InvokeRoundResultConfirmed(LocalMatchIds.LocalPlayerId, continueGame);
        }

        // ==== Game Logic -> UI ====
        public override void RaisePlayersReady(ulong player1Id, string player1Name, ulong player2Id, string player2Name)
        {
            InvokePlayersReady(player1Name, player2Name);
        }

        public override void RaiseRoundResult(
            ulong player1Id,
            RoundOutcome player1Outcome,
            Hand player1Hand,
            ulong player2Id,
            RoundOutcome player2Outcome,
            Hand player2Hand,
            bool canContinue)
        {
            // Always treat the local player as player1.
            InvokeRoundResult(player1Outcome, player1Hand, player2Hand, canContinue);
        }

        public override void RaiseGameAborted(string message)
        {
            InvokeGameAborted(message);
        }

        public override void RaiseRoundStarted()
        {
            InvokeRoundStarted();
        }

        public override void RaiseContinueDecision(bool continueGame)
        {
            InvokeContinueDecision(continueGame);
        }
    }
}
