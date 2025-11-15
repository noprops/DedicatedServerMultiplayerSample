#if UNITY_SERVER || ENABLE_UCS_SERVER
using DedicatedServerMultiplayerSample.Server.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Allocation
{
    /// <summary>
    /// Applies server runtime configuration to the Netcode transport.
    /// </summary>
    internal static class ServerTransportConfigurator
    {
        public static void Configure(NetworkManager networkManager, ServerRuntimeConfig runtimeConfig)
        {
            if (networkManager == null || runtimeConfig == null)
            {
                return;
            }

            var transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[ServerTransportConfigurator] UnityTransport component not found.");
                return;
            }

            transport.SetConnectionData("0.0.0.0", runtimeConfig.GamePort);
            networkManager.NetworkConfig.NetworkTransport = transport;
            Debug.Log($"[ServerTransportConfigurator] Listening on port {runtimeConfig.GamePort}");
        }
    }
}
#endif
