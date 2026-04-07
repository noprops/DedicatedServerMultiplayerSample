using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DedicatedServerMultiplayerSample.Editor
{
    internal static class DsmsOperationsEditor
    {
        private const string PackageName = "info.mygames888.dedicatedservermultiplayersample";

        [MenuItem("DSMS/Cloud/Deploy Cloud Code Module A")]
        public static void DeployCloudCodeModuleA()
        {
            var settings = DsmsOperationsSettings.instance;
            settings.EnsureDefaults();
            if (!EnsureCloudDeploySettings(settings))
            {
                return;
            }

            RunPackageScript(
                "Deploy Cloud Code Module A",
                Path.Combine("Tools~", "cloud", "deploy_cloudcode_module.sh"),
                settings.ProjectId,
                settings.EnvironmentName,
                "A",
                ResolvePackageRoot());
        }

        [MenuItem("DSMS/Cloud/Deploy Cloud Code Module B")]
        public static void DeployCloudCodeModuleB()
        {
            var settings = DsmsOperationsSettings.instance;
            settings.EnsureDefaults();
            if (!EnsureCloudDeploySettings(settings))
            {
                return;
            }

            RunPackageScript(
                "Deploy Cloud Code Module B",
                Path.Combine("Tools~", "cloud", "deploy_cloudcode_module.sh"),
                settings.ProjectId,
                settings.EnvironmentName,
                "B",
                ResolvePackageRoot());
        }

        [MenuItem("DSMS/Cloud/Deploy Matchmaker Config")]
        public static void DeployMatchmakerConfig()
        {
            var settings = DsmsOperationsSettings.instance;
            settings.EnsureDefaults();
            if (!EnsureMatchmakerDeploySettings(settings))
            {
                return;
            }

            RunPackageScript(
                "Deploy Matchmaker Config",
                Path.Combine("Tools~", "matchmaker", "deploy_matchmaker_config.sh"),
                settings.ProjectId,
                settings.EnvironmentName,
                ResolveProjectPath(settings.MatchmakerEnvironmentPath),
                ResolveProjectPath(settings.CompetitiveQueuePath),
                ResolveProjectPath(settings.CasualQueuePath));
        }

        [MenuItem("DSMS/VM/Create Lightsail VM")]
        public static void CreateLightsailVm()
        {
            var settings = DsmsOperationsSettings.instance;
            settings.EnsureDefaults();
            if (!EnsureVmCreateSettings(settings))
            {
                return;
            }

            RunPackageScript(
                "Create Lightsail VM",
                Path.Combine("Tools~", "vm", "create_lightsail_vm.sh"),
                settings.VmCreateSlot,
                settings.VmCreateInstanceName,
                settings.VmCreateAvailabilityZone,
                settings.VmCreateBlueprintId,
                settings.VmCreateBundleId);
        }

        [MenuItem("DSMS/VM/Start VM")]
        public static void StartVm()
        {
            RunPackageScript(
                "Start VM",
                Path.Combine("Tools~", "vm", "start_lightsail_vm.sh"));
        }

        [MenuItem("DSMS/VM/Stop VM")]
        public static void StopVm()
        {
            RunPackageScript(
                "Stop VM",
                Path.Combine("Tools~", "vm", "stop_lightsail_vm.sh"));
        }

        [MenuItem("DSMS/VM/Open Lightsail Ports")]
        public static void OpenLightsailPorts()
        {
            RunPackageScript(
                "Open Lightsail Ports",
                Path.Combine("Tools~", "vm", "open_lightsail_ports.sh"));
        }

        [MenuItem("DSMS/VM/Deploy VM Launcher")]
        public static void DeployVmLauncher()
        {
            RunPackageScript(
                "Deploy VM Launcher",
                Path.Combine("Tools~", "vm", "deploy_vm_launcher.sh"),
                ResolvePackageRoot());
        }

        [MenuItem("DSMS/VM/Install VM Launcher Service")]
        public static void InstallVmLauncherService()
        {
            RunPackageScript(
                "Install VM Launcher Service",
                Path.Combine("Tools~", "vm", "install_vm_launcher_service.sh"));
        }

        [MenuItem("DSMS/VM/Upload Server Build")]
        public static void UploadServerBuild()
        {
            var settings = DsmsOperationsSettings.instance;
            settings.EnsureDefaults();
            if (string.IsNullOrWhiteSpace(settings.LinuxServerBuildDirectory))
            {
                OpenOperationsWindow();
                EditorUtility.DisplayDialog(
                    "DSMS",
                    "Linux server build directory is not configured. Opened DSMS Operations settings.",
                    "OK");
                return;
            }

            RunPackageScript(
                "Upload Server Build",
                Path.Combine("Tools~", "vm", "upload_server_build.sh"),
                ResolveProjectPath(settings.LinuxServerBuildDirectory));
        }

        [MenuItem("DSMS/VM/Set Current Work Slot/A")]
        public static void SetCurrentWorkSlotA()
        {
            RunPackageScript(
                "Set Current Work Slot A",
                Path.Combine("Tools~", "vm", "set_current_work_slot.sh"),
                "A");
        }

        [MenuItem("DSMS/VM/Set Current Work Slot/B")]
        public static void SetCurrentWorkSlotB()
        {
            RunPackageScript(
                "Set Current Work Slot B",
                Path.Combine("Tools~", "vm", "set_current_work_slot.sh"),
                "B");
        }

        [MenuItem("DSMS/Test/Run Auto Match Clients")]
        public static void RunAutoMatchClients()
        {
            var settings = DsmsOperationsSettings.instance;
            settings.EnsureDefaults();
            if (string.IsNullOrWhiteSpace(settings.AutoMatchAppPath))
            {
                OpenOperationsWindow();
                EditorUtility.DisplayDialog(
                    "DSMS",
                    "Auto-match app path is not configured. Opened DSMS Operations settings.",
                    "OK");
                return;
            }

            RunPackageScript(
                "Run Auto Match Clients",
                Path.Combine("Tools~", "test", "run_auto_match_clients.sh"),
                ResolveProjectPath(settings.AutoMatchAppPath),
                settings.AutoMatchInstanceCount.ToString(),
                settings.AutoMatchQueueName);
        }

        [MenuItem("DSMS/Utility/Open Operations Settings")]
        public static void OpenOperationsWindow()
        {
            var window = EditorWindow.GetWindow<DsmsOperationsWindow>("DSMS Operations");
            window.minSize = new Vector2(620f, 540f);
            window.Show();
        }

        [MenuItem("DSMS/Utility/Print Active Package Root")]
        public static void PrintActivePackageRoot()
        {
            Debug.Log($"[DSMS] Package root: {ResolvePackageRoot()}");
        }

        [MenuItem("DSMS/Utility/Print Active VM Config")]
        public static void PrintActiveVmConfig()
        {
            var configPath = Path.Combine(ProjectRoot, "dsms-vm.json");
            if (!File.Exists(configPath))
            {
                Debug.LogWarning($"[DSMS] VM config not found: {configPath}");
                return;
            }

            Debug.Log($"[DSMS] VM config path: {configPath}\n{File.ReadAllText(configPath)}");
        }

        private static bool EnsureCloudDeploySettings(DsmsOperationsSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.ProjectId) && !string.IsNullOrWhiteSpace(settings.EnvironmentName))
            {
                return true;
            }

            OpenOperationsWindow();
            EditorUtility.DisplayDialog(
                "DSMS",
                "Project ID and Environment Name are required. Opened DSMS Operations settings.",
                "OK");
            return false;
        }

        private static bool EnsureMatchmakerDeploySettings(DsmsOperationsSettings settings)
        {
            if (!EnsureCloudDeploySettings(settings))
            {
                return false;
            }

            if (!HasProjectPath(settings.MatchmakerEnvironmentPath) ||
                !HasProjectPath(settings.CompetitiveQueuePath) ||
                !HasProjectPath(settings.CasualQueuePath))
            {
                OpenOperationsWindow();
                EditorUtility.DisplayDialog(
                    "DSMS",
                    "Matchmaker environment and queue paths are required. Opened DSMS Operations settings.",
                    "OK");
                return false;
            }

            return true;
        }

        private static bool EnsureVmCreateSettings(DsmsOperationsSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.VmCreateInstanceName) &&
                !string.IsNullOrWhiteSpace(settings.VmCreateAvailabilityZone) &&
                !string.IsNullOrWhiteSpace(settings.VmCreateBlueprintId) &&
                !string.IsNullOrWhiteSpace(settings.VmCreateBundleId))
            {
                return true;
            }

            OpenOperationsWindow();
            EditorUtility.DisplayDialog(
                "DSMS",
                "VM create settings are incomplete. Opened DSMS Operations settings.",
                "OK");
            return false;
        }

        private static bool HasProjectPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return File.Exists(ResolveProjectPath(value)) || Directory.Exists(ResolveProjectPath(value));
        }

        private static string ResolveProjectPath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(ProjectRoot, path));
        }

        private static string ResolvePackageRoot()
        {
            var packageInfo = PackageInfo.FindForAssetPath($"Packages/{PackageName}");
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                throw new InvalidOperationException($"Failed to resolve package root for {PackageName}.");
            }

            return packageInfo.resolvedPath;
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static void RunPackageScript(string label, string packageRelativeScriptPath, params string[] args)
        {
            var scriptPath = Path.Combine(ResolvePackageRoot(), packageRelativeScriptPath);
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"DSMS script not found: {scriptPath}", scriptPath);
            }

            var command = BuildShellCommand(scriptPath, args);
            var output = RunShellCommand(label, command);
            if (!string.IsNullOrWhiteSpace(output))
            {
                Debug.Log($"[DSMS] {label} output:\n{output}");
            }
        }

        private static string BuildShellCommand(string scriptPath, IReadOnlyList<string> args)
        {
            var builder = new StringBuilder();
            builder.Append("/bin/bash ");
            builder.Append(ShellQuote(scriptPath));

            for (var i = 0; i < args.Count; i++)
            {
                builder.Append(' ');
                builder.Append(ShellQuote(args[i]));
            }

            return builder.ToString();
        }

        private static string RunShellCommand(string label, string command)
        {
            EditorUtility.DisplayProgressBar("DSMS", $"{label}...", "Running");
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/zsh",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = ProjectRoot
                };
                startInfo.ArgumentList.Add("-lc");
                startInfo.ArgumentList.Add(command);
                startInfo.Environment["PROJECT_ROOT"] = ProjectRoot;
                startInfo.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = "/tmp";

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                var combined = CombineOutput(stdout, stderr);
                if (process.ExitCode != 0)
                {
                    Debug.LogError($"[DSMS] {label} failed (exit {process.ExitCode}).\n{combined}");
                    throw new InvalidOperationException($"{label} failed. See Unity Console for details.");
                }

                Debug.Log($"[DSMS] {label} completed successfully.");
                return combined;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static string CombineOutput(string stdout, string stderr)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                builder.AppendLine(stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine(stderr.TrimEnd());
            }

            return builder.ToString().TrimEnd();
        }

        private static string ShellQuote(string value)
        {
            return $"'{value.Replace("'", "'\"'\"'")}'";
        }
    }

    [FilePath("UserSettings/DsmsOperationsSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class DsmsOperationsSettings : ScriptableSingleton<DsmsOperationsSettings>
    {
        public string ProjectId = string.Empty;
        public string EnvironmentName = "production";
        public string MatchmakerEnvironmentPath = string.Empty;
        public string CompetitiveQueuePath = string.Empty;
        public string CasualQueuePath = string.Empty;
        public string LinuxServerBuildDirectory = "Builds/LinuxServer";
        public string AutoMatchAppPath = "Builds/MacAutoMatchClient/DSMSAutoMatchClient.app";
        public int AutoMatchInstanceCount = 2;
        public string AutoMatchQueueName = "competitive-queue";
        public string VmCreateSlot = "A";
        public string VmCreateInstanceName = string.Empty;
        public string VmCreateAvailabilityZone = "ap-northeast-1a";
        public string VmCreateBlueprintId = "ubuntu_24_04";
        public string VmCreateBundleId = "nano_3_0";

        public void EnsureDefaults()
        {
            MatchmakerEnvironmentPath = DefaultIfBlank(
                MatchmakerEnvironmentPath,
                "Assets/Samples/Dedicated Server Multiplayer Sample/0.1.0/Basic Scene Setup/Configurations/MatchmakerEnvironment.mme");
            CompetitiveQueuePath = DefaultIfBlank(
                CompetitiveQueuePath,
                "Assets/Samples/Dedicated Server Multiplayer Sample/0.1.0/Basic Scene Setup/Configurations/CompetitiveQueue.mmq");
            CasualQueuePath = DefaultIfBlank(
                CasualQueuePath,
                "Assets/Samples/Dedicated Server Multiplayer Sample/0.1.0/Basic Scene Setup/Configurations/CasualQueue.mmq");
            AutoMatchInstanceCount = Mathf.Max(1, AutoMatchInstanceCount);
            VmCreateSlot = NormalizeSlot(VmCreateSlot);
            Save(true);
        }

        private static string DefaultIfBlank(string currentValue, string candidate)
        {
            if (!string.IsNullOrWhiteSpace(currentValue))
            {
                return currentValue;
            }

            var resolved = Path.GetFullPath(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), candidate));
            return File.Exists(resolved) ? candidate : currentValue;
        }

        private static string NormalizeSlot(string slot)
        {
            return string.Equals(slot, "B", StringComparison.OrdinalIgnoreCase) ? "B" : "A";
        }
    }

    internal sealed class DsmsOperationsWindow : EditorWindow
    {
        private Vector2 _scrollPosition;

        private void OnEnable()
        {
            DsmsOperationsSettings.instance.EnsureDefaults();
        }

        private void OnGUI()
        {
            var settings = DsmsOperationsSettings.instance;

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("DSMS Operations Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These settings are stored in UserSettings/DsmsOperationsSettings.asset and are used by the DSMS menu commands.",
                MessageType.Info);

            DrawCloudSection(settings);
            DrawMatchmakerSection(settings);
            DrawVmSection(settings);
            DrawTestSection(settings);

            EditorGUILayout.Space();
            if (GUILayout.Button("Save Settings"))
            {
                settings.Save(true);
                Debug.Log("[DSMS] Saved DSMS operations settings.");
            }

            EditorGUILayout.EndScrollView();

            if (GUI.changed)
            {
                settings.Save(true);
            }
        }

        private static void DrawCloudSection(DsmsOperationsSettings settings)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Cloud", EditorStyles.boldLabel);
            settings.ProjectId = EditorGUILayout.TextField("Project ID", settings.ProjectId);
            settings.EnvironmentName = EditorGUILayout.TextField("Environment", settings.EnvironmentName);
        }

        private static void DrawMatchmakerSection(DsmsOperationsSettings settings)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Matchmaker Config", EditorStyles.boldLabel);
            DrawPathField("Environment .mme", ref settings.MatchmakerEnvironmentPath, false, "mme");
            DrawPathField("Competitive Queue .mmq", ref settings.CompetitiveQueuePath, false, "mmq");
            DrawPathField("Casual Queue .mmq", ref settings.CasualQueuePath, false, "mmq");
        }

        private static void DrawVmSection(DsmsOperationsSettings settings)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("VM", EditorStyles.boldLabel);
            DrawPathField("Linux Server Build Dir", ref settings.LinuxServerBuildDirectory, true);
            settings.VmCreateSlot = EditorGUILayout.Popup("Create VM Slot", settings.VmCreateSlot == "B" ? 1 : 0, new[] { "A", "B" }) == 1 ? "B" : "A";
            settings.VmCreateInstanceName = EditorGUILayout.TextField("VM Instance Name", settings.VmCreateInstanceName);
            settings.VmCreateAvailabilityZone = EditorGUILayout.TextField("Availability Zone", settings.VmCreateAvailabilityZone);
            settings.VmCreateBlueprintId = EditorGUILayout.TextField("Blueprint ID", settings.VmCreateBlueprintId);
            settings.VmCreateBundleId = EditorGUILayout.TextField("Bundle ID", settings.VmCreateBundleId);
        }

        private static void DrawTestSection(DsmsOperationsSettings settings)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Auto Match Test", EditorStyles.boldLabel);
            DrawPathField("macOS Auto Match App", ref settings.AutoMatchAppPath, true);
            settings.AutoMatchInstanceCount = EditorGUILayout.IntField("Instance Count", settings.AutoMatchInstanceCount);
            settings.AutoMatchQueueName = EditorGUILayout.TextField("Queue Name", settings.AutoMatchQueueName);
        }

        private static void DrawPathField(string label, ref string value, bool folder, string extension = "")
        {
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.TextField(label, value);
            if (GUILayout.Button("Browse", GUILayout.Width(80f)))
            {
                value = folder
                    ? EditorUtility.OpenFolderPanel(label, Path.GetFullPath(Path.Combine(Application.dataPath, "..")), string.Empty)
                    : EditorUtility.OpenFilePanel(label, Path.GetFullPath(Path.Combine(Application.dataPath, "..")), extension);
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
