using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public partial class RockPaperScissorsGame : NetworkBehaviour
    {
        public const int RequiredGamePlayers = 2;
        public const ulong CpuPlayerBaseId = 100;

        // ========== Static Instance ==========
        public static RockPaperScissorsGame Instance { get; private set; }

        // ========== Shared Network State ==========
        public NetworkVariable<GamePhase> Phase { get; } = new(
            GamePhase.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<RpsResult> LastResult { get; } = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkList<ulong> PlayerIds { get; } = new(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        public NetworkList<FixedString64Bytes> PlayerNames { get; } = new(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        // ========== Server State ==========
        private readonly Dictionary<ulong, Hand> m_PlayerChoices = new();
        private TaskCompletionSource<bool> m_AllPlayersChosenTcs;
        private bool m_GameInProgress = false;

        // ========== Unity Lifecycle ==========

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Debug.Log("[RockPaperScissorsGame] Instance set in Awake");
        }

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[RockPaperScissorsGame] Spawned - IsServer: {IsServer}, IsClient: {IsClient}");
            OnServerSpawn();  // Call partial method for server initialization
        }

        public override void OnNetworkDespawn()
        {
            OnServerDespawn();  // Call partial method for server cleanup
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            PlayerIds.Dispose();
            PlayerNames.Dispose();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ========== Partial Methods (Server hooks) ==========
        partial void OnServerSpawn();
        partial void OnServerDespawn();
        partial void HandleSubmitChoice(ulong clientId, Hand choice);

        // ========== Server RPC (Client callable) ==========

        [ServerRpc(RequireOwnership = false)]
        public void SubmitChoiceServerRpc(Hand choice, ServerRpcParams rpcParams = default)
        {
            HandleSubmitChoice(rpcParams.Receive.SenderClientId, choice);
        }
    }
}
