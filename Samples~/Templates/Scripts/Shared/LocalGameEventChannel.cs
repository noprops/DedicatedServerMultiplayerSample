using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Local-only game event channel that bridges the UI and CPU logic without Netcode.
    /// </summary>
    public sealed class LocalGameEventChannel : RpsGameEventChannel
    {
        private void Awake()
        {
            Debug.Log("[LocalGameEventChannel] Awake");
        }

        private void Start()
        {
            Debug.Log("[LocalGameEventChannel] Channel ready (local mode).");
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

        public override void RaiseGameAbortConfirmed()
        {
            SceneManager.LoadScene("loading", LoadSceneMode.Single);
        }

    }
}
