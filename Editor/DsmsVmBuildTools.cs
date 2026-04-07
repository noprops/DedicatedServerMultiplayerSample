using System.IO;
using System.Linq;
using Unity.Multiplayer;
using Unity.Multiplayer.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Editor
{
    public static class DsmsVmBuildTools
    {
        private const string DefaultOutputDirectory = "Builds/LinuxServer";
        private const string DefaultOutputExecutable = "Builds/LinuxServer/DedicatedServer.x86_64";

        [MenuItem("DSMS/Build/Build Linux Dedicated Server")]
        public static void BuildLinuxDedicatedServer()
        {
            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (enabledScenes.Length == 0)
            {
                throw new BuildFailedException("No enabled scenes found in EditorBuildSettings.");
            }

            var outputDirectory = DefaultOutputDirectory;
            var outputExecutable = DefaultOutputExecutable;
            Directory.CreateDirectory(outputDirectory);

            var originalTarget = EditorUserBuildSettings.activeBuildTarget;
            var originalSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            var originalRole = EditorMultiplayerRolesManager.ActiveMultiplayerRoleMask;

            try
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildTargetGroup.Standalone,
                        BuildTarget.StandaloneLinux64))
                {
                    throw new BuildFailedException("Failed to switch active build target to StandaloneLinux64.");
                }

                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
                EditorMultiplayerRolesManager.EnableMultiplayerRoles = true;
                EditorMultiplayerRolesManager.ActiveMultiplayerRoleMask = MultiplayerRoleFlags.Server;
                EditorMultiplayerRolesManager.SetMultiplayerRoleForClassicTarget(
                    BuildTarget.StandaloneLinux64,
                    StandaloneBuildSubtarget.Server,
                    MultiplayerRoleFlags.Server);

                var activeBuildProfile = BuildProfile.GetActiveBuildProfile();
                if (activeBuildProfile != null)
                {
                    EditorMultiplayerRolesManager.SetMultiplayerRoleForBuildProfile(
                        activeBuildProfile,
                        MultiplayerRoleFlags.Server);
                }

                Debug.Log(
                    $"[DsmsVmBuildTools] target={EditorUserBuildSettings.activeBuildTarget} " +
                    $"subtarget={EditorUserBuildSettings.standaloneBuildSubtarget} " +
                    $"role={EditorMultiplayerRolesManager.ActiveMultiplayerRoleMask}");

                var options = new BuildPlayerOptions
                {
                    scenes = enabledScenes,
                    locationPathName = outputExecutable,
                    target = BuildTarget.StandaloneLinux64,
                    subtarget = (int)StandaloneBuildSubtarget.Server,
                    options = BuildOptions.None
                };

                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException(
                        $"Linux dedicated server build failed: {report.summary.result}");
                }

                Debug.Log($"[DsmsVmBuildTools] Build succeeded: {report.summary.outputPath}");
            }
            finally
            {
                EditorMultiplayerRolesManager.ActiveMultiplayerRoleMask = originalRole;

                if (EditorUserBuildSettings.activeBuildTarget != originalTarget)
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildTargetGroup.Standalone,
                        originalTarget);
                }

                EditorUserBuildSettings.standaloneBuildSubtarget = originalSubtarget;
            }
        }

        [MenuItem("DSMS/VM/Build Linux Dedicated Server")]
        public static void BuildLinuxDedicatedServerLegacyMenu()
        {
            BuildLinuxDedicatedServer();
        }
    }
}
