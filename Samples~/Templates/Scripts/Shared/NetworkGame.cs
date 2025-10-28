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

        /// <summary>
        /// Establishes server hooks when this behaviour spawns on the server.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                OnServerSpawn();
            }
        }

        /// <summary>
        /// Tears down server hooks and clears the singleton when despawned.
        /// </summary>
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

        /// <summary>
        /// Receives hand submissions from clients and forwards them to the server-only logic.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void SubmitChoiceServerRpc(Hand choice, ServerRpcParams rpcParams = default)
        {
            HandleSubmitChoice(rpcParams.Receive.SenderClientId, choice);
        }

        /// <summary>
        /// Tells each client that the round has started and provides the name pairing.
        /// </summary>
        [ClientRpc]
        private void RoundStartedClientRpc(string myName, string yourName, ClientRpcParams rpcParams = default)
        {
            if (ui == null)
            {
                Debug.LogWarning("[NetworkGame] RockPaperScissorsUI is not assigned.");
                return;
            }

            ui.ShowChoicePanel(myName, yourName);
        }

        /// <summary>
        /// Tells each client that the round has ended and shares the outcome.
        /// </summary>
        [ClientRpc]
        private void RoundEndedClientRpc(RoundOutcome myOutcome, Hand myHand, Hand yourHand, ClientRpcParams rpcParams = default)
        {
            if (ui == null)
            {
                Debug.LogWarning("[NetworkGame] RockPaperScissorsUI is not assigned.");
                return;
            }

            ui.ShowResult(myOutcome, myHand, yourHand);
        }

        /// <summary>
        /// Server-side setup hook implemented in the partial server class.
        /// </summary>
        partial void OnServerSpawn();

        /// <summary>
        /// Server-side teardown hook implemented in the partial server class.
        /// </summary>
        partial void OnServerDespawn();

        /// <summary>
        /// Handles a hand submission on the server for the specified client.
        /// </summary>
        partial void HandleSubmitChoice(ulong clientId, Hand choice);
    }
}
