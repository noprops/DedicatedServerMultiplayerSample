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
        public void SendRoundStarted(ulong player1Id, string player1Name, ulong player2Id, string player2Name)
        {
            RoundStartedClientRpc(player1Id, player1Name, player2Id, player2Name);
        }

        public void SendRoundResult(ulong player1Id, RoundOutcome player1Outcome, Hand player1Hand,
            ulong player2Id, RoundOutcome player2Outcome, Hand player2Hand)
        {
            RoundEndedClientRpc(player1Id, player1Outcome, player1Hand, player2Id, player2Outcome, player2Hand);
        }

        public void SendGameAborted(string message)
        {
            NotifyClientOfAbortClientRpc(string.IsNullOrWhiteSpace(message) ? "Match aborted" : message);
        }

        [ClientRpc]
        private void RoundStartedClientRpc(ulong player1Id, string player1Name, ulong player2Id, string player2Name,
            ClientRpcParams rpcParams = default)
        {
            if (channel == null)
            {
                return;
            }

            var localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : player1Id;
            if (localId == player1Id)
            {
                channel.InvokeRoundStarted(player1Name, player2Name);
            }
            else if (localId == player2Id)
            {
                channel.InvokeRoundStarted(player2Name, player1Name);
            }
            else
            {
                channel.InvokeRoundStarted(player1Name, player2Name);
            }
        }

        [ClientRpc]
        private void RoundEndedClientRpc(ulong player1Id, RoundOutcome player1Outcome, Hand player1Hand,
            ulong player2Id, RoundOutcome player2Outcome, Hand player2Hand, ClientRpcParams rpcParams = default)
        {
            if (channel == null)
            {
                return;
            }

            var localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : player1Id;
            if (localId == player1Id)
            {
                channel.InvokeRoundResult(player1Outcome, player1Hand, player2Hand);
            }
            else if (localId == player2Id)
            {
                channel.InvokeRoundResult(player2Outcome, player2Hand, player1Hand);
            }
            else
            {
                channel.InvokeRoundResult(player1Outcome, player1Hand, player2Hand);
            }
        }

        [ClientRpc]
        private void NotifyClientOfAbortClientRpc(string message, ClientRpcParams rpcParams = default)
        {
            channel?.InvokeGameAborted(message);
        }
    }
}
