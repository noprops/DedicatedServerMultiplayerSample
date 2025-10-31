using DedicatedServerMultiplayerSample.Shared;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Minimal Netcode coordinator that bridges client RPC/UI calls with server-side round management.
    /// </summary>
    public partial class NetworkGame : NetworkBehaviour
    {
        public const int RequiredGamePlayers = 2;
        public const ulong CpuPlayerBaseId = 100;

        public static NetworkGame Instance { get; private set; }
        [SerializeField] private RockPaperScissorsUI ui;

        /// <summary>
        /// Ensures a single instance survives in the scene.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                OnServerSpawn();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                OnServerDespawn();
            }

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnNetworkDespawn();
        }

        [ServerRpc(RequireOwnership = false)]
        public void SubmitChoiceServerRpc(Hand choice, ServerRpcParams rpcParams = default)
        {
            HandleSubmitChoice(rpcParams.Receive.SenderClientId, choice);
        }

        /// <summary>
        /// Tells each client that the round has started and provides the name pairing.
        /// </summary>
        [ClientRpc]
        private void RoundStartedClientRpc(ulong player1Id, ulong player2Id, string player1Name, string player2Name, ClientRpcParams rpcParams = default)
        {
            if (ui == null)
            {
                Debug.LogWarning("[NetworkGame] RockPaperScissorsUI is not assigned.");
                return;
            }

            var localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            string myName;
            string yourName;

            if (localId == player1Id)
            {
                myName = player1Name;
                yourName = player2Name;
            }
            else if (localId == player2Id)
            {
                myName = player2Name;
                yourName = player1Name;
            }
            else
            {
                // Spectator or desynced client; fallback to primary ordering.
                myName = player1Name;
                yourName = player2Name;
            }

            ui.ShowChoicePanel(myName, yourName);
        }

        /// <summary>
        /// Informs clients that the round has ended, providing their personal hand, the opponent hand, and the outcome.
        /// </summary>
        [ClientRpc]
        private void RoundEndedClientRpc(RoundOutcome myOutcome, Hand myHand, Hand opponentHand, ClientRpcParams rpcParams = default)
        {
            if (ui == null)
            {
                Debug.LogWarning("[NetworkGame] RockPaperScissorsUI is not assigned.");
                return;
            }

            ui.ShowResult(myOutcome, myHand, opponentHand);
        }

        /// <summary>
        /// Requests clients to disconnect, showing a modal message first.
        /// </summary>
        [ClientRpc]
        private void RequestClientDisconnectClientRpc(string message, ClientRpcParams rpcParams = default)
        {
            if (ui == null)
            {
                Debug.LogWarning("[NetworkGame] RockPaperScissorsUI is not assigned.");
                return;
            }

            ui.ShowDisconnectPrompt(message ?? "Disconnected", 2f);
        }

        partial void OnServerSpawn();

        partial void OnServerDespawn();

        partial void HandleSubmitChoice(ulong clientId, Hand choice);
    }
}
