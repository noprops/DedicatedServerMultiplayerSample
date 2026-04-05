using System;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Client
{
    internal static class SessionEndpointResolver
    {
        private const string NetworkPropertyKey = "_session_network";

        private static readonly string[] IpKeys =
        {
            "serverIp",
            "serverIP",
            "hostIp",
            "hostIP",
            "vmIp",
            "vmIP"
        };

        private static readonly string[] PortKeys =
        {
            "serverPort",
            "hostPort",
            "vmPort"
        };

        [Serializable]
        private struct NetworkMetadataPayload
        {
            public string Ip;
            public ushort Port;
        }

        public static bool TryResolve(ISession session, out string ipAddress, out ushort port)
        {
            ipAddress = null;
            port = 0;

            if (session?.Properties == null)
            {
                return false;
            }

            if (TryResolveFromNetworkProperty(session, out ipAddress, out port))
            {
                return true;
            }

            return TryResolveFromExplicitProperties(session, out ipAddress, out port);
        }

        private static bool TryResolveFromNetworkProperty(ISession session, out string ipAddress, out ushort port)
        {
            ipAddress = null;
            port = 0;

            if (!session.Properties.TryGetValue(NetworkPropertyKey, out var networkProperty) ||
                string.IsNullOrWhiteSpace(networkProperty?.Value))
            {
                return false;
            }

            try
            {
                var payload = JsonUtility.FromJson<NetworkMetadataPayload>(networkProperty.Value);
                if (!string.IsNullOrWhiteSpace(payload.Ip) && payload.Port > 0)
                {
                    ipAddress = payload.Ip;
                    port = payload.Port;
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SessionEndpointResolver] Failed to parse network metadata: {exception.Message}");
            }

            return false;
        }

        private static bool TryResolveFromExplicitProperties(ISession session, out string ipAddress, out ushort port)
        {
            ipAddress = null;
            port = 0;

            for (int ipIndex = 0; ipIndex < IpKeys.Length; ipIndex++)
            {
                if (session.Properties.TryGetValue(IpKeys[ipIndex], out var ipProperty) &&
                    !string.IsNullOrWhiteSpace(ipProperty?.Value))
                {
                    ipAddress = ipProperty.Value;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                return false;
            }

            for (int portIndex = 0; portIndex < PortKeys.Length; portIndex++)
            {
                if (session.Properties.TryGetValue(PortKeys[portIndex], out var portProperty) &&
                    ushort.TryParse(portProperty?.Value, out port) &&
                    port > 0)
                {
                    return true;
                }
            }

            ipAddress = null;
            port = 0;
            return false;
        }
    }
}
