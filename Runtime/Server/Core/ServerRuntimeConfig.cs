using System;
using System.Collections.Generic;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Captures and exposes server runtime configuration sourced from command line arguments.
    /// Stores the resolved ports, identifiers, and log paths so other systems can read a consistent view of VM-hosted deployment data.
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

        public string GeneratedServerName { get; }
        public string MatchId { get; }
        public int ExpectedPlayerCount { get; }
        public IReadOnlyList<string> ExpectedAuthIds { get; }

        private ServerRuntimeConfig(
            IReadOnlyList<string> args,
            ushort gamePort,
            bool gamePortFromCommandLine,
            ushort queryPort,
            bool queryPortFromCommandLine,
            string logFilePath,
            string generatedServerName,
            string matchId,
            int expectedPlayerCount,
            IReadOnlyList<string> expectedAuthIds)
        {
            CommandLineArgs = args;
            GamePort = gamePort;
            GamePortFromCommandLine = gamePortFromCommandLine;
            QueryPort = queryPort;
            QueryPortFromCommandLine = queryPortFromCommandLine;
            LogFilePath = logFilePath;
            GeneratedServerName = generatedServerName;
            MatchId = matchId;
            ExpectedPlayerCount = Mathf.Max(1, expectedPlayerCount);
            ExpectedAuthIds = expectedAuthIds ?? Array.Empty<string>();
        }

        public static ServerRuntimeConfig Capture()
        {
            var args = Environment.GetCommandLineArgs();

            ushort gamePort = k_DefaultGamePort;
            bool gamePortFromCommandLine = false;
            ushort queryPort = k_DefaultQueryPort;
            bool queryPortFromCommandLine = false;
            string logFilePath = null;
            string matchId = null;
            int expectedPlayerCount = 2;
            var expectedAuthIds = new List<string>();

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

                if ((string.Equals(current, "-expectedPlayers", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(current, "-teamCount", StringComparison.OrdinalIgnoreCase)) &&
                    next != null &&
                    int.TryParse(next, out var parsedExpectedPlayers))
                {
                    expectedPlayerCount = Mathf.Max(1, parsedExpectedPlayers);
                }

                if ((string.Equals(current, "-expectedAuthIds", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(current, "-matchAuthIds", StringComparison.OrdinalIgnoreCase)) &&
                    next != null)
                {
                    expectedAuthIds.Clear();
                    var tokens = next.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
                    {
                        var authId = tokens[tokenIndex]?.Trim();
                        if (!string.IsNullOrWhiteSpace(authId))
                        {
                            expectedAuthIds.Add(authId);
                        }
                    }
                }

                if ((string.Equals(current, "-matchId", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(current, "-assignmentId", StringComparison.OrdinalIgnoreCase)) &&
                    next != null &&
                    !string.IsNullOrWhiteSpace(next))
                {
                    matchId = next.Trim();
                }
            }

            string nameSeed = !string.IsNullOrEmpty(matchId)
                ? matchId
                : Guid.NewGuid().ToString("N");
            string generatedServerName = $"RPS-{nameSeed.Substring(0, Math.Min(8, nameSeed.Length))}";

            return new ServerRuntimeConfig(
                args,
                gamePort,
                gamePortFromCommandLine,
                queryPort,
                queryPortFromCommandLine,
                logFilePath,
                generatedServerName,
                matchId,
                expectedPlayerCount,
                expectedAuthIds);
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

            Debug.Log($"[ServerRuntimeConfig] GeneratedServerName: {GeneratedServerName}");
            Debug.Log($"[ServerRuntimeConfig] MatchId: {(string.IsNullOrWhiteSpace(MatchId) ? "(none)" : MatchId)}");
            Debug.Log($"[ServerRuntimeConfig] ExpectedPlayerCount: {ExpectedPlayerCount}");
            Debug.Log($"[ServerRuntimeConfig] ExpectedAuthIds: {(ExpectedAuthIds.Count > 0 ? string.Join(", ", ExpectedAuthIds) : "(none)")}");
        }
    }
}
