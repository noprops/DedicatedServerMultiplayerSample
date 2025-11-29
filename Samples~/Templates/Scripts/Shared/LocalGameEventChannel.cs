using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Local-only game event channel that bridges the UI and CPU logic without Netcode.
    /// </summary>
    public sealed class LocalGameEventChannel : RpsGameEventChannel
    {
        [SerializeField] private ulong localPlayerId = 1;

        private void Awake()
        {
            Debug.Log("[LocalGameEventChannel] Awake");
        }

        private void Start()
        {
            Debug.Log("[LocalGameEventChannel] Channel ready (local mode).");
            NotifyChannelReady();
        }

        public override void RaiseChoiceSelected(Hand choice)
        {
            InvokeChoiceSelected(localPlayerId, choice);
        }

        public override void RaiseRoundResultConfirmed()
        {
            InvokeRoundResultConfirmed(localPlayerId);
            SceneManager.LoadScene("loading", LoadSceneMode.Single);
        }

        public override void RaiseRoundStarted(ulong player1Id, string player1Name, ulong player2Id, string player2Name)
        {
            var isPlayerOne = localPlayerId == player1Id;
            var myName = isPlayerOne ? player1Name : player2Name;
            var opponentName = isPlayerOne ? player2Name : player1Name;
            InvokeRoundStarted(myName, opponentName);
        }

        public override void RaiseRoundResult(ulong player1Id, RoundOutcome player1Outcome, Hand player1Hand,
            ulong player2Id, RoundOutcome player2Outcome, Hand player2Hand)
        {
            var isPlayerOne = localPlayerId == player1Id;
            var myOutcome = isPlayerOne ? player1Outcome : player2Outcome;
            var myHand = isPlayerOne ? player1Hand : player2Hand;
            var opponentHand = isPlayerOne ? player2Hand : player1Hand;
            InvokeRoundResult(myOutcome, myHand, opponentHand);
        }

        public override void RaiseGameAborted(string message)
        {
            InvokeGameAborted(message);
        }

        public override void RaiseGameAbortConfirmed()
        {
            SceneManager.LoadScene("loading", LoadSceneMode.Single);
        }
    }
}
