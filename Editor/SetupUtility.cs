using System.IO;
using UnityEditor;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Editor
{
    public static class SetupUtility
    {
        private const string TemplatesRoot = "Packages/info.mygames888.dedicatedservermultiplayersample/Samples~/Templates";
        private const string TargetRoot = "Assets";

        [MenuItem("Tools/Dedicated Server Multiplayer Sample/Run Setup", priority = 0)]
        public static void RunSetup()
        {
            if (!Directory.Exists(TemplatesRoot))
            {
                EditorUtility.DisplayDialog(
                    "Templates Not Found",
                    "Template assets could not be located. Please ensure the package is installed correctly.",
                    "OK");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                CopyFolder("Scenes");
                CopyFolder("Prefabs");
                CopyFolder("Configurations");
                CopyFolder("Scripts/Client");
                CopyFolder("Scripts/Shared");

                // Copy GameConfig into Assets/Resources/Config
                CopyFolder("Resources/Config");

                SetupScenes();

                EditorUtility.DisplayDialog(
                    "Setup Complete",
                    "Project assets have been generated. Review Build Settings and adjust GameConfig as needed.",
                    "OK");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DedicatedServerMultiplayerSample] Setup failed: {e.Message}");
                EditorUtility.DisplayDialog("Setup Failed", e.Message, "OK");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        private static void CopyFolder(string relativePath)
        {
            string source = Path.Combine(TemplatesRoot, relativePath);
            if (!Directory.Exists(source))
            {
                return;
            }

            string destination = Path.Combine(TargetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? TargetRoot);

            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string dirTarget = directory.Replace(source, destination);
                Directory.CreateDirectory(dirTarget);
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string fileTarget = file.Replace(source, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(fileTarget) ?? destination);
                File.Copy(file, fileTarget, overwrite: true);
            }
        }

        private static void SetupScenes()
        {
            string[] defaultScenes =
            {
                "Assets/Scenes/bootStrap.unity",
                "Assets/Scenes/loading.unity",
                "Assets/Scenes/menu.unity",
                "Assets/Scenes/game.unity"
            };

            var sceneList = new EditorBuildSettingsScene[defaultScenes.Length];
            for (int i = 0; i < defaultScenes.Length; i++)
            {
                sceneList[i] = new EditorBuildSettingsScene(defaultScenes[i], true);
            }
            EditorBuildSettings.scenes = sceneList;
        }
    }
}
