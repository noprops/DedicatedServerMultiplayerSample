#if !UNITY_SERVER && !ENABLE_UCS_SERVER
using DedicatedServerMultiplayerSample.Client;
#endif
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Netcode-backed implementation of <see cref="RpsGameEventChannel"/> bridging UI and server logic.
    /// </summary>
    public sealed class NetworkGameEventDispatcher : RpsGameEventChannel
    {
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            NotifyChannelReady();
        }

        public override void OnNetworkDespawn()
        {
            ResetChannelReadiness();
            base.OnNetworkDespawn();
        }

        // ==== UI → Game Logic ====

        /// <summary>
        /// Client-side entry point used by the UI when the player picks a hand.
        /// Relays the request to the server via RPC, where the sender ID is resolved.
        /// </summary>
        public override void RaiseChoiceSelected(Hand choice)
        {
            if (!IsClient)
            {
                Debug.LogWarning("[NetworkGameEventDispatcher] RaiseChoiceSelected invoked on a non-client instance.");
                return;
            }

            if (!IsSpawned)
            {
                Debug.LogWarning("[NetworkGameEventDispatcher] RaiseChoiceSelected invoked before spawn.");
                return;
            }

            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                Debug.LogWarning("[NetworkGameEventDispatcher] NetworkManager missing when raising choice.");
                return;
            }

            SubmitChoiceServerRpc(choice);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitChoiceServerRpc(Hand choice, ServerRpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            InvokeChoiceSelected(sender, choice);
        }

        /// <summary>
        /// Client-side acknowledgement that the round result screen has been confirmed.
        /// </summary>
        public override void RaiseRoundResultConfirmed()
        {
            if (!IsClient)
            {
                Debug.LogWarning("[NetworkGameEventDispatcher] RaiseRoundResultConfirmed invoked on a non-client instance.");
                return;
            }

            if (!IsSpawned)
            {
                Debug.LogWarning("[NetworkGameEventDispatcher] RaiseRoundResultConfirmed invoked before spawn.");
                return;
            }

            ConfirmRoundResultServerRpc();

#if !UNITY_SERVER && !ENABLE_UCS_SERVER
            ClientSingleton.Instance?.GameManager?.Disconnect();
#endif
        }

        [ServerRpc(RequireOwnership = false)]
        private void ConfirmRoundResultServerRpc(ServerRpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            InvokeRoundResultConfirmed(sender);
        }

        // ==== Game Logic → UI ====

        /// <summary>
        /// Server-side notification to a specific client that a round has begun.
        /// </summary>
        public override void RaiseRoundStarted(ulong targetClientId, string myName, string opponentName)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[NetworkGameEventDispatcher] RaiseRoundStarted invoked on a non-server instance.");
                return;
            }

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { targetClientId }
                }
            };

            RoundStartedClientRpc(myName, opponentName, rpcParams);
        }

        /// <summary>
        /// Server-side notification delivering the computed round result to a client.
        /// </summary>
        public override void RaiseRoundResult(ulong targetClientId, RoundOutcome myOutcome, Hand myHand, Hand opponentHand)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[NetworkGameEventDispatcher] RaiseRoundResult invoked on a non-server instance.");
                return;
            }

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { targetClientId }
                }
            };

            RoundEndedClientRpc(myOutcome, myHand, opponentHand, rpcParams);
        }

        /// <summary>
        /// Server-side instruction informing a client that the match aborted abnormally.
        /// </summary>
        public override void RaiseGameAborted(ulong targetClientId, string message)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[NetworkGameEventDispatcher] RaiseGameAborted invoked on a non-server instance.");
                return;
            }

            var safeMessage = string.IsNullOrWhiteSpace(message) ? "Match aborted" : message;

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { targetClientId }
                }
            };

            NotifyClientOfAbortClientRpc(safeMessage, rpcParams);
        }

        public override void RaiseGameAbortAcknowledged()
        {
#if !UNITY_SERVER && !ENABLE_UCS_SERVER
            ClientSingleton.Instance?.GameManager?.Disconnect();
#else
            Debug.LogWarning("[NetworkGameEventDispatcher] Game abort acknowledged on a server-only build.");
#endif
        }

        [ClientRpc]
        private void RoundStartedClientRpc(
            string myName,
            string opponentName,
            ClientRpcParams rpcParams = default)
        {
            InvokeRoundStarted(myName, opponentName);
        }

        [ClientRpc]
        private void RoundEndedClientRpc(
            RoundOutcome myOutcome,
            Hand myHand,
            Hand opponentHand,
            ClientRpcParams rpcParams = default)
        {
            InvokeRoundResult(myOutcome, myHand, opponentHand);
        }

        [ClientRpc]
        private void NotifyClientOfAbortClientRpc(
            string message,
            ClientRpcParams rpcParams = default)
        {
            InvokeGameAborted(message);
        }
    }
}
