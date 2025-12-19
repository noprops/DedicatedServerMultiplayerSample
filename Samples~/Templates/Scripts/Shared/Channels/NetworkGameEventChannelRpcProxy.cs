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

        public void ConfirmRoundResult(bool continueGame)
        {
            ConfirmRoundResultServerRpc(continueGame);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitChoiceServerRpc(Hand choice, ServerRpcParams rpcParams = default)
        {
            channel?.ReceiveChoice(rpcParams.Receive.SenderClientId, choice);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ConfirmRoundResultServerRpc(bool continueGame, ServerRpcParams rpcParams = default)
        {
            channel?.InvokeRoundResultConfirmed(rpcParams.Receive.SenderClientId, continueGame);
        }

        // === Server → Client ===
        public void SendPlayersReady(ulong player1Id, string player1Name, ulong player2Id, string player2Name)
        {
            PlayersReadyClientRpc(player1Id, player1Name, player2Id, player2Name);
        }

        public void SendRoundResult(ulong player1Id, RoundOutcome player1Outcome, Hand player1Hand,
            ulong player2Id, RoundOutcome player2Outcome, Hand player2Hand, bool canContinue)
        {
            RoundEndedClientRpc(player1Id, player1Outcome, player1Hand, player2Id, player2Outcome, player2Hand, canContinue);
        }

        public void SendGameAborted(string message)
        {
            NotifyClientOfAbortClientRpc(string.IsNullOrWhiteSpace(message) ? "Match aborted" : message);
        }

        [ClientRpc]
        private void PlayersReadyClientRpc(ulong player1Id, string player1Name, ulong player2Id, string player2Name,
            ClientRpcParams rpcParams = default)
        {
            if (channel == null)
            {
                return;
            }

            var localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : player1Id;
            if (localId == player1Id)
            {
                channel.InvokePlayersReady(player1Name, player2Name);
            }
            else if (localId == player2Id)
            {
                channel.InvokePlayersReady(player2Name, player1Name);
            }
            else
            {
                channel.InvokePlayersReady(player1Name, player2Name);
            }
        }

        [ClientRpc]
        private void RoundEndedClientRpc(ulong player1Id, RoundOutcome player1Outcome, Hand player1Hand,
            ulong player2Id, RoundOutcome player2Outcome, Hand player2Hand, bool canContinue, ClientRpcParams rpcParams = default)
        {
            if (channel == null)
            {
                return;
            }

            var localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : player1Id;
            if (localId == player1Id)
            {
                channel.InvokeRoundResult(player1Outcome, player1Hand, player2Hand, canContinue);
            }
            else if (localId == player2Id)
            {
                channel.InvokeRoundResult(player2Outcome, player2Hand, player1Hand, canContinue);
            }
            else
            {
                channel.InvokeRoundResult(player1Outcome, player1Hand, player2Hand, canContinue);
            }
        }

        [ClientRpc]
        private void NotifyClientOfAbortClientRpc(string message, ClientRpcParams rpcParams = default)
        {
            channel?.InvokeGameAborted(message);
        }

    }
}
