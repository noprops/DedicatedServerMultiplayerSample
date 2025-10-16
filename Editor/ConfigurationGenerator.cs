using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Editor
{
    internal static class ConfigurationGenerator
    {
        private const string QueueFileName = "MatchmakerQueue.mmq";
        private const string EnvironmentFileName = "MatchmakerEnvironment.mme";
        private const string MultiplayFileName = "MultiplayConfiguration.gsh";

        private const string QueueName = "default-queue";
        private const string DefaultPoolName = "default-pool";
        private const string FilteredPoolName = "high-version-pool";
        private const string FleetName = "fleet";
        private const string BuildConfigurationName = "build-config";
        private const string DefaultRegion = "Asia";
        private const string MultiplayBuildName = "server-build";
        private const string MultiplayExecutableName = "DedicatedServer.x86_64";
        private const string MultiplayBuildPath = "Builds/LinuxServer";
        private const string MultiplayBinaryPath = "DedicatedServer.x86_64";

        public static void UpdateFromGameConfig(GameConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[ConfigurationGenerator] GameConfig is null.");
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("[ConfigurationGenerator] Could not resolve GameConfig asset path.");
                return;
            }

            var rootFolder = FindSampleRootFolder(assetPath);
            if (string.IsNullOrEmpty(rootFolder))
            {
                Debug.LogError("[ConfigurationGenerator] Failed to locate the sample root folder from GameConfig path: " + assetPath);
                return;
            }

            var configurationsFolder = Path.Combine(rootFolder, "Configurations");
            if (!Directory.Exists(configurationsFolder))
            {
                Directory.CreateDirectory(configurationsFolder);
            }

            try
            {
                UpdateMatchmakerQueue(configurationsFolder, config);
                UpdateMatchmakerEnvironment(configurationsFolder);
                UpdateMultiplayConfig(configurationsFolder);
                AssetDatabase.Refresh();
                Debug.Log("[ConfigurationGenerator] Configurations updated.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[ConfigurationGenerator] Failed to update configurations: " + ex.Message);
            }
        }

        private static string FindSampleRootFolder(string gameConfigAssetPath)
        {
            var directory = Path.GetDirectoryName(gameConfigAssetPath);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var resourcesFolder = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(resourcesFolder))
            {
                return null;
            }

            var rootFolder = Path.GetDirectoryName(resourcesFolder);
            return string.IsNullOrEmpty(rootFolder) ? null : rootFolder.Replace('\\', '/');
        }

        private static void UpdateMatchmakerQueue(string configurationsFolder, GameConfig gameConfig)
        {
            var path = Path.Combine(configurationsFolder, QueueFileName);
            EnsureQueueFileExists(path);

            var queueConfig = JsonUtility.FromJson<MatchmakerQueueConfig>(File.ReadAllText(path)) ?? CreateDefaultQueueConfig();

            var minTeams = Mathf.Max(1, gameConfig.MinTeams);
            var maxTeams = Mathf.Max(minTeams, gameConfig.MaxTeams);
            var minPlayersPerTeam = Mathf.Max(1, gameConfig.MinPlayersPerTeam);
            var maxPlayersPerTeam = Mathf.Max(minPlayersPerTeam, gameConfig.MaxPlayersPerTeam);

            queueConfig.name = QueueName;
            queueConfig.enabled = true;
            queueConfig.maxPlayersPerTicket = maxPlayersPerTeam;

            queueConfig.defaultPool ??= new Pool();
            queueConfig.defaultPool.name = DefaultPoolName;
            ConfigurePool(queueConfig.defaultPool, "Rules", minTeams, maxTeams, minPlayersPerTeam, maxPlayersPerTeam);

            var filteredPool = queueConfig.filteredPools != null && queueConfig.filteredPools.Length > 0
                ? queueConfig.filteredPools[0]
                : new Pool();
            filteredPool.name = FilteredPoolName;
            filteredPool.filters = new[]
            {
                new Filter { attribute = "gameVersion", @operator = "GreaterThan", value = 200 }
            };
            ConfigurePool(filteredPool, "Rules", minTeams, maxTeams, minPlayersPerTeam, maxPlayersPerTeam);
            queueConfig.filteredPools = new[] { filteredPool };

            File.WriteAllText(path, JsonUtility.ToJson(queueConfig, true));
        }

        private static void EnsureQueueFileExists(string path)
        {
            if (File.Exists(path))
            {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonUtility.ToJson(CreateDefaultQueueConfig(), true));
        }

        private static MatchmakerQueueConfig CreateDefaultQueueConfig()
        {
            var config = new MatchmakerQueueConfig
            {
                name = QueueName,
                defaultPool = new Pool { name = DefaultPoolName }
            };

            ConfigurePool(config.defaultPool, "Rules", 2, 2, 1, 1);

            var filtered = new Pool { name = FilteredPoolName };
            filtered.filters = new[] { new Filter { attribute = "gameVersion", @operator = "GreaterThan", value = 200 } };
            ConfigurePool(filtered, "Rules", 2, 2, 1, 1);
            config.filteredPools = new[] { filtered };

            return config;
        }

        private static void ConfigurePool(Pool pool, string logicName, int minTeams, int maxTeams, int minPlayersPerTeam, int maxPlayersPerTeam)
        {
            pool.enabled = true;
            pool.timeoutSeconds = 300;
            pool.matchLogic ??= new MatchLogic();
            pool.matchLogic.name = logicName;
            pool.matchLogic.matchDefinition ??= new MatchDefinition();
            ApplyTeamSettings(pool.matchLogic.matchDefinition.teams, minTeams, maxTeams, minPlayersPerTeam, maxPlayersPerTeam);

            pool.matchHosting ??= new MatchHosting();
            pool.matchHosting.type = "Multiplay";
            pool.matchHosting.fleetName = FleetName;
            pool.matchHosting.buildConfigurationName = BuildConfigurationName;
            pool.matchHosting.defaultQoSRegionName = DefaultRegion;
        }

        private static void ApplyTeamSettings(TeamDefinition[] teams, int minTeams, int maxTeams, int minPlayersPerTeam, int maxPlayersPerTeam)
        {
            if (teams == null)
            {
                return;
            }

            foreach (var team in teams)
            {
                team.teamCount ??= new Count();
                team.teamCount.min = minTeams;
                team.teamCount.max = maxTeams;
                team.teamCount.relaxations = Array.Empty<Count>();

                team.playerCount ??= new Count();
                team.playerCount.min = minPlayersPerTeam;
                team.playerCount.max = maxPlayersPerTeam;
                team.playerCount.relaxations = Array.Empty<Count>();
            }
        }

        private static void UpdateMatchmakerEnvironment(string configurationsFolder)
        {
            var path = Path.Combine(configurationsFolder, EnvironmentFileName);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = "{\n" +
                       "  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/matchmaker/matchmaker-environment-config.schema.json\",\n" +
                       "  \"enabled\": true,\n" +
                       $"  \"defaultQueueName\": \"{QueueName}\"\n" +
                       "}\n";

            File.WriteAllText(path, json);
        }

        private static void UpdateMultiplayConfig(string configurationsFolder)
        {
            var path = Path.Combine(configurationsFolder, MultiplayFileName);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var yaml =
                $"version: 1.0\\n" +
                "builds:\\n" +
                $"  {MultiplayBuildName}:\\n" +
                $"    executableName: {MultiplayExecutableName}\\n" +
                $"    buildPath: {MultiplayBuildPath}\\n" +
                "    excludePaths: []\\n" +
                "buildConfigurations:\\n" +
                $"  {BuildConfigurationName}:\\n" +
                $"    build: {MultiplayBuildName}\\n" +
                "    queryType: sqp\\n" +
                $"    binaryPath: {MultiplayBinaryPath}\\n" +
                "    commandLine: -nographics -batchmode -port $$port$$ -queryport $$query_port$$ -logFile $$log_dir$$/server.log\\n" +
                "    variables: {}\\n" +
                "    readiness: true\\n" +
                "fleets:\\n" +
                $"  {FleetName}:\\n" +
                "    buildConfigurations:\\n" +
                $"      - {BuildConfigurationName}\\n" +
                "    regions:\\n" +
                $"      {DefaultRegion}:\\n" +
                "        minAvailable: 1\\n" +
                "        maxServers: 2\\n" +
                "    usageSettings:\\n" +
                "      - hardwareType: CLOUD\\n" +
                "        machineType: GCP-N2\\n" +
                "        maxServersPerMachine: 4\\n";

            File.WriteAllText(path, yaml);
        }

        [Serializable]
        private class MatchmakerQueueConfig
        {
            public string name = QueueName;
            public bool enabled = true;
            public int maxPlayersPerTicket = 1;
            public Pool defaultPool = new Pool { name = DefaultPoolName };
            public Pool[] filteredPools = Array.Empty<Pool>();
        }

        [Serializable]
        private class Pool
        {
            public string name = DefaultPoolName;
            public bool enabled = true;
            public float timeoutSeconds = 300;
            public Variant[] variants = Array.Empty<Variant>();
            public MatchLogic matchLogic = new MatchLogic();
            public MatchHosting matchHosting = new MatchHosting();
            public Filter[] filters = Array.Empty<Filter>();
        }

        [Serializable]
        private class Variant { }

        [Serializable]
        private class Filter
        {
            public string attribute;
            public string @operator;
            public int value;
        }

        [Serializable]
        private class MatchLogic
        {
            public string name = "Rules";
            public MatchDefinition matchDefinition = new MatchDefinition();
        }

        [Serializable]
        private class MatchDefinition
        {
            public TeamDefinition[] teams = { new TeamDefinition() };
            public MatchRule[] matchRules =
            {
                new MatchRule { name = "GameModeRule", type = "Equality", source = "Players.CustomData.gameMode", enableRule = true },
                new MatchRule { name = "MapRule", type = "Equality", source = "Players.CustomData.map", enableRule = true }
            };
        }

        [Serializable]
        private class TeamDefinition
        {
            public string name = "Players";
            public Count teamCount = new Count { min = 2, max = 2 };
            public Count playerCount = new Count { min = 1, max = 1 };
            public TeamRule[] teamRules = Array.Empty<TeamRule>();
        }

        [Serializable]
        private class TeamRule { }

        [Serializable]
        private class Count
        {
            public int min;
            public int max;
            public Count[] relaxations = Array.Empty<Count>();
        }

        [Serializable]
        private class MatchRule
        {
            public string name;
            public string type;
            public string source;
            public bool enableRule;
        }

        [Serializable]
        private class MatchHosting
        {
            public string type = "Multiplay";
            public string fleetName = FleetName;
            public string buildConfigurationName = BuildConfigurationName;
            public string defaultQoSRegionName = DefaultRegion;
        }
    }
}
