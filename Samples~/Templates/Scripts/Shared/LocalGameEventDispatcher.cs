using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Local-only dispatcher that bridges the UI and CPU game logic without Netcode.
    /// </summary>
    public sealed class LocalGameEventDispatcher : RpsGameEventChannel
    {
        private void Start()
        {
            NotifyChannelReady();
        }

        [SerializeField] private ulong localPlayerId = 1;

        public override void RaiseChoiceSelected(Hand choice)
        {
            InvokeChoiceSelected(localPlayerId, choice);
        }

        public override void RaiseRoundResultConfirmed()
        {
            InvokeRoundResultConfirmed(localPlayerId);
            SceneManager.LoadScene("loading", LoadSceneMode.Single);
        }

        public override void RaiseRoundStarted(ulong targetClientId, string myName, string opponentName)
        {
            InvokeRoundStarted(myName, opponentName);
        }

        public override void RaiseRoundResult(ulong targetClientId, RoundOutcome myOutcome, Hand myHand, Hand opponentHand)
        {
            InvokeRoundResult(myOutcome, myHand, opponentHand);
        }

        public override void RaiseGameAborted(ulong targetClientId, string message)
        {
            InvokeGameAborted(message);
        }

        public override void RaiseGameAbortAcknowledged()
        {
            SceneManager.LoadScene("loading", LoadSceneMode.Single);
        }

    }
}
