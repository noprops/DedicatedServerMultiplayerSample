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

        partial void OnServerSpawn();
        partial void OnServerDespawn();
        partial void HandleSubmitChoice(ulong clientId, Hand choice);
    }
}
