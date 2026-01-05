using System;
using System.Collections.Generic;
using UnityEngine;

#if (UNITY_SERVER || ENABLE_UCS_SERVER) && UNITY_SERVICES_MULTIPLAY
using Unity.Services.Multiplay;
#endif

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Captures and exposes server runtime configuration sourced from command line arguments and Multiplay server.json.
    /// Stores the resolved ports, identifiers, and log paths so other systems can read a consistent view of deployment data.
    /// </summary>
    public class ServerRuntimeConfig
    {
        private const ushort k_DefaultGamePort = 7777;
        private const ushort k_DefaultQueryPort = 7787;

        public IReadOnlyList<string> CommandLineArgs { get; }
        public ushort GamePort { get; }
        public bool GamePortFromCommandLine { get; }
        public ushort QueryPort { get; }
        public bool QueryPortFromCommandLine { get; }
        public string LogFilePath { get; }

        public bool ServerConfigAvailable { get; }
        public long? ServerId { get; }
        public string AllocationId { get; }
        public ushort? ServerConfigPort { get; }
        public ushort? ServerConfigQueryPort { get; }
        public string IpAddress { get; }
        public string ServerLogDirectory { get; }

        public string GeneratedServerName { get; }

        private ServerRuntimeConfig(
            IReadOnlyList<string> args,
            ushort gamePort,
            bool gamePortFromCommandLine,
            ushort queryPort,
            bool queryPortFromCommandLine,
            string logFilePath,
            bool serverConfigAvailable,
            long? serverId,
            string allocationId,
            ushort? serverConfigPort,
            ushort? serverConfigQueryPort,
            string ipAddress,
            string serverLogDirectory,
            string generatedServerName)
        {
            CommandLineArgs = args;
            GamePort = gamePort;
            GamePortFromCommandLine = gamePortFromCommandLine;
            QueryPort = queryPort;
            QueryPortFromCommandLine = queryPortFromCommandLine;
            LogFilePath = logFilePath;
            ServerConfigAvailable = serverConfigAvailable;
            ServerId = serverId;
            AllocationId = allocationId;
            ServerConfigPort = serverConfigPort;
            ServerConfigQueryPort = serverConfigQueryPort;
            IpAddress = ipAddress;
            ServerLogDirectory = serverLogDirectory;
            GeneratedServerName = generatedServerName;
        }

        public static ServerRuntimeConfig Capture()
        {
            var args = Environment.GetCommandLineArgs();

            ushort gamePort = k_DefaultGamePort;
            bool gamePortFromCommandLine = false;
            ushort queryPort = k_DefaultQueryPort;
            bool queryPortFromCommandLine = false;
            string logFilePath = null;

            for (int i = 0; i < args.Length; i++)
            {
                string current = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;

                if (string.Equals(current, "-port", StringComparison.OrdinalIgnoreCase) && next != null)
                {
                    if (ushort.TryParse(next, out var parsedPort))
                    {
                        gamePort = parsedPort;
                        gamePortFromCommandLine = true;
                    }
                }

                if (string.Equals(current, "-queryport", StringComparison.OrdinalIgnoreCase) && next != null)
                {
                    if (ushort.TryParse(next, out var parsedQueryPort))
                    {
                        queryPort = parsedQueryPort;
                        queryPortFromCommandLine = true;
                    }
                }

                if (string.Equals(current, "-logFile", StringComparison.OrdinalIgnoreCase) && next != null)
                {
                    logFilePath = next;
                }
            }

            bool serverConfigAvailable = false;
            long? serverId = null;
            string allocationId = null;
            ushort? serverConfigPort = null;
            ushort? serverConfigQueryPort = null;
            string ipAddress = null;
            string serverLogDirectory = null;

#if (UNITY_SERVER || ENABLE_UCS_SERVER) && UNITY_SERVICES_MULTIPLAY
            try
            {
                var serverConfig = MultiplayService.Instance?.ServerConfig;
                if (serverConfig != null)
                {
                    serverConfigAvailable = true;
                    serverId = serverConfig.ServerId;
                    allocationId = serverConfig.AllocationId;
                    serverConfigPort = serverConfig.Port;
                    serverConfigQueryPort = serverConfig.QueryPort;
                    ipAddress = serverConfig.IpAddress;
                    serverLogDirectory = serverConfig.ServerLogDirectory;

                    if (!gamePortFromCommandLine && serverConfigPort.HasValue)
                    {
                        gamePort = serverConfigPort.Value;
                    }

                    if (!queryPortFromCommandLine && serverConfigQueryPort.HasValue)
                    {
                        queryPort = serverConfigQueryPort.Value;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerRuntimeConfig] Failed to read server.json: {e.Message}");
            }
#endif

            string nameSeed = !string.IsNullOrEmpty(allocationId)
                ? allocationId
                : Guid.NewGuid().ToString("N");
            string generatedServerName = $"RPS-{nameSeed.Substring(0, Math.Min(8, nameSeed.Length))}";

            return new ServerRuntimeConfig(
                args,
                gamePort,
                gamePortFromCommandLine,
                queryPort,
                queryPortFromCommandLine,
                logFilePath,
                serverConfigAvailable,
                serverId,
                allocationId,
                serverConfigPort,
                serverConfigQueryPort,
                ipAddress,
                serverLogDirectory,
                generatedServerName);
        }

        public void LogSummary()
        {
            Debug.Log($"[ServerRuntimeConfig] Command line arguments ({CommandLineArgs.Count}):");
            for (int i = 0; i < CommandLineArgs.Count; i++)
            {
                Debug.Log($"  [{i}] {CommandLineArgs[i]}");
            }

            Debug.Log($"[ServerRuntimeConfig] GamePort: {GamePort} (FromArgs: {GamePortFromCommandLine})");
            Debug.Log($"[ServerRuntimeConfig] QueryPort: {QueryPort} (FromArgs: {QueryPortFromCommandLine})");
            if (!string.IsNullOrEmpty(LogFilePath))
            {
                Debug.Log($"[ServerRuntimeConfig] LogFile: {LogFilePath}");
            }

            if (ServerConfigAvailable)
            {
                Debug.Log("[ServerRuntimeConfig] server.json values:");
                Debug.Log($"  - ServerId: {ServerId}");
                Debug.Log($"  - AllocationId: {AllocationId}");
                Debug.Log($"  - Port: {ServerConfigPort}");
                Debug.Log($"  - QueryPort: {ServerConfigQueryPort}");
                Debug.Log($"  - IpAddress: {IpAddress}");
                Debug.Log($"  - LogDirectory: {ServerLogDirectory}");
            }
            else
            {
                Debug.Log("[ServerRuntimeConfig] server.json not available");
            }

            Debug.Log($"[ServerRuntimeConfig] GeneratedServerName: {GeneratedServerName}");
        }
    }
}
