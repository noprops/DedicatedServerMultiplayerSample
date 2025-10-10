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
        private const string BuildProfileFileName = "LinuxServer.buildprofile";

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
                EnsureMultiplayConfigExists(configurationsFolder);
                UpdateBuildProfile(configurationsFolder, rootFolder);
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
            // GameConfig asset is typically at ".../Basic Scene Setup/Resources/Config/GameConfig.asset"
            var directory = Path.GetDirectoryName(gameConfigAssetPath);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var resourcesFolder = Path.GetDirectoryName(directory); // Resources
            if (string.IsNullOrEmpty(resourcesFolder))
            {
                return null;
            }

            var rootFolder = Path.GetDirectoryName(resourcesFolder); // Sample root
            return string.IsNullOrEmpty(rootFolder) ? null : rootFolder.Replace('\\', '/');
        }

        private static void UpdateMatchmakerQueue(string configurationsFolder, GameConfig gameConfig)
        {
            var path = Path.Combine(configurationsFolder, QueueFileName);
            EnsureQueueFileExists(path);

            var json = File.ReadAllText(path);
            var queueConfig = JsonUtility.FromJson<MatchmakerQueueConfig>(json);
            if (queueConfig == null)
            {
                Debug.LogError("[ConfigurationGenerator] Unable to parse MatchmakerQueue configuration.");
                return;
            }

            var minTeams = Mathf.Max(1, gameConfig.MinTeams);
            var maxTeams = Mathf.Max(minTeams, gameConfig.MaxTeams);
            var minPlayersPerTeam = Mathf.Max(1, gameConfig.MinPlayersPerTeam);
            var maxPlayersPerTeam = Mathf.Max(minPlayersPerTeam, gameConfig.MaxPlayersPerTeam);

            queueConfig.maxPlayersPerTicket = maxPlayersPerTeam;

            void ApplyTeamSettings(TeamDefinition[] teams)
            {
                if (teams == null)
                {
                    return;
                }

                foreach (var team in teams)
                {
                    if (team.teamCount != null)
                    {
                        team.teamCount.min = minTeams;
                        team.teamCount.max = maxTeams;
                    }

                    if (team.playerCount != null)
                    {
                        team.playerCount.min = minPlayersPerTeam;
                        team.playerCount.max = maxPlayersPerTeam;
                    }
                }
            }

            ApplyTeamSettings(queueConfig.defaultPool?.matchLogic?.matchDefinition?.teams);

            if (queueConfig.filteredPools != null)
            {
                foreach (var pool in queueConfig.filteredPools)
                {
                    ApplyTeamSettings(pool.matchLogic?.matchDefinition?.teams);
                }
            }

            var updatedJson = JsonUtility.ToJson(queueConfig, true);
            File.WriteAllText(path, updatedJson);
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

            var queue = JsonUtility.ToJson(new MatchmakerQueueConfig(), true);
            File.WriteAllText(path, queue);
        }

        private static void UpdateMatchmakerEnvironment(string configurationsFolder)
        {
            var path = Path.Combine(configurationsFolder, EnvironmentFileName);
            if (!File.Exists(path))
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, "{\n  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/matchmaker/matchmaker-environment-config.schema.json\",\n  \"enabled\": true,\n  \"defaultQueueName\": \"default-queue\"\n}\n");
                return;
            }

            // No dynamic content to update right now; keep placeholder as-is.
        }

        private static void EnsureMultiplayConfigExists(string configurationsFolder)
        {
            var path = Path.Combine(configurationsFolder, MultiplayFileName);
            if (File.Exists(path))
            {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            const string defaultYaml = "version: 1.0\nbuilds:\n  dedicated-server-sample:\n    executableName: DedicatedServerMultiplayerSample.x86_64\n    buildPath: Builds/LinuxServer\n    excludePaths: []\nbuildConfigurations:\n  dedicated-server-config:\n    build: dedicated-server-sample\n    queryType: sqp\n    binaryPath: DedicatedServerMultiplayerSample.x86_64\n    commandLine: -nographics -batchmode -port $$port$$ -queryport $$query_port$$ -logFile $$log_dir$$/server.log\n    variables: {}\n    readiness: true\nfleets:\n  dedicated-server-fleet:\n    buildConfigurations:\n      - dedicated-server-config\n    regions:\n      Asia:\n        minAvailable: 1\n        maxServers: 2\n    usageSettings:\n      - hardwareType: CLOUD\n        machineType: GCP-N2\n        maxServersPerMachine: 4\n";
            File.WriteAllText(path, defaultYaml);
        }

        private static void UpdateBuildProfile(string configurationsFolder, string rootFolder)
        {
            var path = Path.Combine(configurationsFolder, BuildProfileFileName);
            BuildProfileDefinition profile;
            if (File.Exists(path))
            {
                profile = JsonUtility.FromJson<BuildProfileDefinition>(File.ReadAllText(path));
                if (profile == null)
                {
                    profile = new BuildProfileDefinition();
                }
            }
            else
            {
                profile = new BuildProfileDefinition();
            }

            profile.name = string.IsNullOrEmpty(profile.name) ? "LinuxServer" : profile.name;
            profile.buildTarget = string.IsNullOrEmpty(profile.buildTarget) ? "DedicatedServer" : profile.buildTarget;
            profile.outputPath = string.IsNullOrEmpty(profile.outputPath) ? "Builds/LinuxServer" : profile.outputPath;

            var bootStrapPath = CombineAndNormalize(rootFolder, "Scenes/bootStrap.unity");
            var gamePath = CombineAndNormalize(rootFolder, "Scenes/game.unity");
            profile.scenes = new[] { bootStrapPath, gamePath };

            var json = JsonUtility.ToJson(profile, true);
            File.WriteAllText(path, json);
        }

        private static string CombineAndNormalize(string left, string right)
        {
            return Path.Combine(left, right).Replace('\\', '/');
        }

        [Serializable]
        private class MatchmakerQueueConfig
        {
            public string name = "default-queue";
            public bool enabled = true;
            public int maxPlayersPerTicket = 1;
            public Pool defaultPool = new Pool();
            public Pool[] filteredPools = Array.Empty<Pool>();
        }

        [Serializable]
        private class Pool
        {
            public string name = "default-pool";
            public bool enabled = true;
            public float timeoutSeconds = 90;
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
            public Count teamCount = new Count { min = 2, max = 2, relaxations = Array.Empty<Count>() };
            public Count playerCount = new Count { min = 1, max = 1, relaxations = Array.Empty<Count>() };
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
            public string fleetName = "rps-fleet";
            public string buildConfigurationName = "rps-server-config";
            public string defaultQoSRegionName = "Asia";
        }

        [Serializable]
        private class BuildProfileDefinition
        {
            public string name = "LinuxServer";
            public string buildTarget = "DedicatedServer";
            public string outputPath = "Builds/LinuxServer";
            public bool development = false;
            public string[] scenes = Array.Empty<string>();
        }
    }
}
