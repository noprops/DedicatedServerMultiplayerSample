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
        public override void RaiseRoundStarted(ulong player1Id, string player1Name, ulong player2Id, string player2Name)
        {
            rpcProxy.SendRoundStarted(player1Id, player1Name, player2Id, player2Name);
        }

        public override void RaiseRoundResult(ulong player1Id, RoundOutcome player1Outcome, Hand player1Hand,
            ulong player2Id, RoundOutcome player2Outcome, Hand player2Hand)
        {
            rpcProxy.SendRoundResult(player1Id, player1Outcome, player1Hand, player2Id, player2Outcome, player2Hand);
        }

        public override void RaiseGameAborted(string message)
        {
            rpcProxy.SendGameAborted(message);
        }

        // Partial hooks for client-specific behavior
        partial void HandleClientChoiceSelected(Hand choice);
        partial void HandleClientRoundResultConfirmed();
        partial void HandleClientAbortConfirmed();
    }
}
