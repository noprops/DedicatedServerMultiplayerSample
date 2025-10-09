using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client
{
    /// <summary>
    /// Ensures a NetworkManager plus UnityTransport instance exists at runtime.
    /// Keeps the bootstrap simple when importing the sample via Package Manager.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class NetworkManagerBootstrapper : MonoBehaviour
    {
        [SerializeField]
        private bool m_DontDestroyOnLoad = true;

        [SerializeField]
        private ushort m_DefaultPort = 7777;

        private void Awake()
        {
            if (NetworkManager.Singleton != null)
            {
                EnsureTransport(NetworkManager.Singleton);
                return;
            }

            var networkManagerGO = new GameObject("NetworkManager");
            var networkManager = networkManagerGO.AddComponent<NetworkManager>();

            var transport = networkManagerGO.AddComponent<UnityTransport>();
            transport.SetConnectionData("0.0.0.0", m_DefaultPort);
            networkManager.NetworkConfig.NetworkTransport = transport;

            if (m_DontDestroyOnLoad)
            {
                DontDestroyOnLoad(networkManagerGO);
            }
        }

        private void EnsureTransport(NetworkManager manager)
        {
            if (manager.NetworkConfig.NetworkTransport != null)
            {
                return;
            }

            var transport = manager.gameObject.GetComponent<UnityTransport>();
            if (transport == null)
            {
                transport = manager.gameObject.AddComponent<UnityTransport>();
            }

            transport.SetConnectionData("0.0.0.0", m_DefaultPort);
            manager.NetworkConfig.NetworkTransport = transport;
        }
    }
}
