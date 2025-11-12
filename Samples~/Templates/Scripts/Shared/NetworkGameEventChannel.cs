using DedicatedServerMultiplayerSample.Shared;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Netcode-backed implementation of <see cref="RpsGameEventChannel"/> bridging UI and server logic.
    /// A companion <see cref="NetworkGameEventChannelRpcProxy"/> component handles the RPC surface while
    /// this class keeps the transport-agnostic API that UI / gameplay code consumes。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkGameEventChannelRpcProxy))]
    public sealed partial class NetworkGameEventChannel : RpsGameEventChannel
    {
        [SerializeField] private NetworkGameEventChannelRpcProxy rpcProxy;

        private void Awake()
        {
            if (rpcProxy == null)
            {
                Debug.LogError("[NetworkGameEventChannel] RpcProxy must be assigned.");
                enabled = false;
                return;
            }

            rpcProxy.Initialize(this);
        }

        private void OnDestroy()
        {
            rpcProxy?.Cleanup();
        }

        // ==== UI → Game Logic ====
        public override void RaiseChoiceSelected(Hand choice)
        {
            HandleClientChoiceSelected(choice);
        }

        public override void RaiseRoundResultConfirmed()
        {
            HandleClientRoundResultConfirmed();
        }

        public override void RaiseGameAbortConfirmed()
        {
            HandleClientAbortConfirmed();
        }

        // ==== Game Logic → UI ====
        public override void RaiseRoundStarted(ulong targetClientId, string myName, string opponentName)
        {
            rpcProxy.SendRoundStarted(targetClientId, myName, opponentName);
        }

        public override void RaiseRoundResult(ulong targetClientId, RoundOutcome myOutcome, Hand myHand, Hand opponentHand)
        {
            rpcProxy.SendRoundResult(targetClientId, myOutcome, myHand, opponentHand);
        }

        public override void RaiseGameAborted(ulong targetClientId, string message)
        {
            rpcProxy.SendGameAborted(targetClientId, message);
        }

        // Partial hooks for client-specific behavior
        partial void HandleClientChoiceSelected(Hand choice);
        partial void HandleClientRoundResultConfirmed();
        partial void HandleClientAbortConfirmed();
    }
}
