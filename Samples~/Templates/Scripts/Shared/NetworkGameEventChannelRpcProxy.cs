using DedicatedServerMultiplayerSample.Shared;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Handles Netcode RPC traffic on behalf of <see cref="NetworkGameEventChannel"/>.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkGameEventChannelRpcProxy : NetworkBehaviour
    {
        private NetworkGameEventChannel channel;
        public void Initialize(NetworkGameEventChannel owner)
        {
            channel = owner;
        }

        public void Cleanup()
        {
            channel = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            channel?.NotifyChannelReady();
        }

        public override void OnNetworkDespawn()
        {
            channel?.ResetChannelReadiness();
            base.OnNetworkDespawn();
        }

        // === Client → Server ===
        public void SubmitChoice(Hand choice)
        {
            SubmitChoiceServerRpc(choice);
        }

        public void ConfirmRoundResult()
        {
            ConfirmRoundResultServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitChoiceServerRpc(Hand choice, ServerRpcParams rpcParams = default)
        {
            channel?.InvokeChoiceSelected(rpcParams.Receive.SenderClientId, choice);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ConfirmRoundResultServerRpc(ServerRpcParams rpcParams = default)
        {
            channel?.InvokeRoundResultConfirmed(rpcParams.Receive.SenderClientId);
        }

        // === Server → Client ===
        public void SendRoundStarted(ulong targetClientId, string myName, string opponentName)
        {
            RoundStartedClientRpc(myName, opponentName, BuildClientParams(targetClientId));
        }

        public void SendRoundResult(ulong targetClientId, RoundOutcome outcome, Hand myHand, Hand opponentHand)
        {
            RoundEndedClientRpc(outcome, myHand, opponentHand, BuildClientParams(targetClientId));
        }

        public void SendGameAborted(ulong targetClientId, string message)
        {
            NotifyClientOfAbortClientRpc(string.IsNullOrWhiteSpace(message) ? "Match aborted" : message, BuildClientParams(targetClientId));
        }

        private ClientRpcParams BuildClientParams(ulong targetClientId)
        {
            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { targetClientId }
                }
            };
        }

        [ClientRpc]
        private void RoundStartedClientRpc(string myName, string opponentName, ClientRpcParams rpcParams = default)
        {
            channel?.InvokeRoundStarted(myName, opponentName);
        }

        [ClientRpc]
        private void RoundEndedClientRpc(RoundOutcome outcome, Hand myHand, Hand opponentHand, ClientRpcParams rpcParams = default)
        {
            channel?.InvokeRoundResult(outcome, myHand, opponentHand);
        }

        [ClientRpc]
        private void NotifyClientOfAbortClientRpc(string message, ClientRpcParams rpcParams = default)
        {
            channel?.InvokeGameAborted(message);
        }
    }
}
