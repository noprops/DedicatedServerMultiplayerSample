using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace DedicatedServerMultiplayerSample.Editor
{
    public static class DsmsAutoMatchBuildTools
    {
        private const string DefaultOutputPath = "Builds/MacAutoMatchClient/DSMSAutoMatchClient.app";

        [MenuItem("DSMS/Test/Build macOS Auto-Match Client")]
        public static void BuildMacAutoMatchClient()
        {
            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (enabledScenes.Length == 0)
            {
                throw new BuildFailedException("No enabled scenes found in EditorBuildSettings.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DefaultOutputPath)!);

            var options = new BuildPlayerOptions
            {
                scenes = enabledScenes,
                locationPathName = DefaultOutputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
                extraScriptingDefines = new[]
                {
                    "DSMS_AUTO_MATCH_TEST"
                }
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"macOS auto-match build failed: {report.summary.result}");
            }
        }

        [MenuItem("DSMS/Test Build/Build macOS Auto-Match Client")]
        public static void BuildMacAutoMatchClientLegacyMenu()
        {
            BuildMacAutoMatchClient();
        }
    }
}
