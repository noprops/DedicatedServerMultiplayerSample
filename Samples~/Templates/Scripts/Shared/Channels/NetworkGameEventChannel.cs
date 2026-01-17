using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Netcode-backed implementation of <see cref="RpsGameEventChannel"/> bridging UI and server logic.
    /// A companion <see cref="NetworkGameEventChannelRpcProxy"/> component handles the RPC surface while
    /// this class keeps the transport-agnostic API that UI / gameplay code consumes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkGameEventChannelRpcProxy))]
    public sealed class NetworkGameEventChannel : RpsGameEventChannel
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

        public override void RaiseChoiceSelectedForPlayer(ulong playerId, Hand hand)
        {
            InvokeChoiceSelected(playerId, hand);
        }

        public override void RaiseRoundResultConfirmed(bool continueGame)
        {
            HandleClientRoundResultConfirmed(continueGame);
        }

        // ==== Game Logic → UI ====
        public override void RaisePlayersReady(ulong player1Id, string player1Name, ulong player2Id, string player2Name)
        {
            rpcProxy.SendPlayersReady(player1Id, player1Name, player2Id, player2Name);
        }

        public override void RaiseRoundResult(ulong player1Id, RoundOutcome player1Outcome, Hand player1Hand,
            ulong player2Id, RoundOutcome player2Outcome, Hand player2Hand, bool canContinue)
        {
            rpcProxy.SendRoundResult(player1Id, player1Outcome, player1Hand, player2Id, player2Outcome, player2Hand, canContinue);
        }

        public override void RaiseGameAborted(string message)
        {
            rpcProxy.SendGameAborted(message);
        }

        public override void RaiseRoundStarted()
        {
            rpcProxy.SendRoundStarted();
        }

        public override void RaiseContinueDecision(bool continueGame)
        {
            rpcProxy.SendContinueDecision(continueGame);
        }

        // Client-only behavior (server builds are no-ops).
        private void HandleClientChoiceSelected(Hand choice)
        {
#if !UNITY_SERVER && !ENABLE_UCS_SERVER
            rpcProxy.SubmitChoice(choice);
#endif
        }

        private void HandleClientRoundResultConfirmed(bool continueGame)
        {
#if !UNITY_SERVER && !ENABLE_UCS_SERVER
            // Notify local listeners without relying on a client id (server will include sender id via RPC).
            InvokeRoundResultConfirmed(0, continueGame);
            // Forward the selection to the server via RPC proxy.
            rpcProxy.ConfirmRoundResult(continueGame);
#endif
        }
    }
}
