using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public partial class RockPaperScissorsNetworkGame : NetworkBehaviour
    {
        public const int RequiredGamePlayers = 2;
        public const ulong CpuPlayerBaseId = 100;

        public static RockPaperScissorsNetworkGame Instance { get; private set; }

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

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Debug.Log("[RockPaperScissorsNetworkGame] Instance set in Awake");
        }

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[RockPaperScissorsNetworkGame] Spawned - IsServer: {IsServer}, IsClient: {IsClient}");
            OnServerSpawn();
        }

        public override void OnNetworkDespawn()
        {
            OnServerDespawn();
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

        partial void OnServerSpawn();
        partial void OnServerDespawn();
        partial void HandleSubmitChoice(ulong clientId, Hand choice);

        [ServerRpc(RequireOwnership = false)]
        public void SubmitChoiceServerRpc(Hand choice, ServerRpcParams rpcParams = default)
        {
            HandleSubmitChoice(rpcParams.Receive.SenderClientId, choice);
        }
    }
}
