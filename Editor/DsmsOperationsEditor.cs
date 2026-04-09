using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DedicatedServerMultiplayerSample.Editor
{
    internal static class DsmsOperationsEditor
    {
        private const string PackageName = "info.mygames888.dedicatedservermultiplayersample";
        private const string DefaultLinuxServerBuildDirectory = "Builds/LinuxServer";

        [MenuItem("DSMS/Cloud/Deploy Cloud Code Module A")]
        public static void DeployCloudCodeModuleA()
        {
            var config = LoadVmConfig();

            RunPackageScript(
                "Deploy Cloud Code Module A",
                Path.Combine("Tools~", "cloud", "deploy_cloudcode_module.sh"),
                RequireTopLevelValue(config.ProjectId, "projectId"),
                RequireTopLevelValue(config.Environment, "environment"),
                "A",
                ResolvePackageRoot());
        }

        [MenuItem("DSMS/Cloud/Deploy Cloud Code Module B")]
        public static void DeployCloudCodeModuleB()
        {
            var config = LoadVmConfig();

            RunPackageScript(
                "Deploy Cloud Code Module B",
                Path.Combine("Tools~", "cloud", "deploy_cloudcode_module.sh"),
                RequireTopLevelValue(config.ProjectId, "projectId"),
                RequireTopLevelValue(config.Environment, "environment"),
                "B",
                ResolvePackageRoot());
        }

        [MenuItem("DSMS/Cloud/Deploy Matchmaker Config")]
        public static void DeployMatchmakerConfig()
        {
            var config = LoadVmConfig();

            RunPackageScript(
                "Deploy Matchmaker Config",
                Path.Combine("Tools~", "matchmaker", "deploy_matchmaker_config.sh"),
                RequireTopLevelValue(config.ProjectId, "projectId"),
                RequireTopLevelValue(config.Environment, "environment"),
                ResolveRequiredProjectFile("MatchmakerEnvironment.mme"),
                ResolveRequiredProjectFile("CompetitiveQueue.mmq"),
                ResolveRequiredProjectFile("CasualQueue.mmq"));
        }

        [MenuItem("DSMS/VM/Create Lightsail VM")]
        public static void CreateLightsailVm()
        {
            var config = LoadVmConfig();
            var slot = config.CurrentWorkSlot;
            var slotData = config.GetSlot(slot);

            RequireTopLevelValue(config.DefaultAvailabilityZone, "defaultAvailabilityZone");
            RequireTopLevelValue(config.DefaultBlueprintId, "defaultBlueprintId");
            RequireTopLevelValue(config.DefaultBundleId, "defaultBundleId");
            RequireSlotValue(slotData.instanceName, $"slots.{slot}.instanceName");

            RunPackageScript(
                "Create Lightsail VM",
                Path.Combine("Tools~", "vm", "create_lightsail_vm.sh"),
                slot);
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
            LoadVmConfig();

            RunPackageScript(
                "Deploy VM Launcher",
                Path.Combine("Tools~", "vm", "deploy_vm_launcher.sh"),
                ResolvePackageRoot());
        }

        [MenuItem("DSMS/VM/Install VM Launcher Service")]
        public static void InstallVmLauncherService()
        {
            LoadVmConfig();

            RunPackageScript(
                "Install VM Launcher Service",
                Path.Combine("Tools~", "vm", "install_vm_launcher_service.sh"));
        }

        [MenuItem("DSMS/VM/Upload Server Build")]
        public static void UploadServerBuild()
        {
            LoadVmConfig();

            var buildDirectory = ResolveDefaultLinuxServerBuildDirectory();
            RunPackageScript(
                "Upload Server Build",
                Path.Combine("Tools~", "vm", "upload_server_build.sh"),
                buildDirectory);
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

        [MenuItem("DSMS/Utility/Open VM Config")]
        public static void OpenVmConfig()
        {
            var configPath = GetVmConfigPath();
            if (!File.Exists(configPath))
            {
                EditorUtility.DisplayDialog(
                    "DSMS",
                    $"VM config not found:\n{configPath}",
                    "OK");
                return;
            }

            EditorUtility.RevealInFinder(configPath);
        }

        [MenuItem("DSMS/Utility/Print Active Package Root")]
        public static void PrintActivePackageRoot()
        {
            Debug.Log($"[DSMS] Package root: {ResolvePackageRoot()}");
        }

        [MenuItem("DSMS/Utility/Print Active VM Config")]
        public static void PrintActiveVmConfig()
        {
            var configPath = GetVmConfigPath();
            if (!File.Exists(configPath))
            {
                Debug.LogWarning($"[DSMS] VM config not found: {configPath}");
                return;
            }

            Debug.Log($"[DSMS] VM config path: {configPath}\n{File.ReadAllText(configPath)}");
        }

        internal static string ResolvePackageRoot()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{PackageName}");
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                throw new InvalidOperationException($"Failed to resolve package root for {PackageName}.");
            }

            return packageInfo.resolvedPath;
        }

        internal static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        internal static string GetVmConfigPath()
        {
            return Path.Combine(ProjectRoot, "dsms-vm.json");
        }

        internal static DsmsVmConfigData LoadVmConfig(bool requireFile = true)
        {
            var configPath = GetVmConfigPath();
            if (!File.Exists(configPath))
            {
                if (!requireFile)
                {
                    return new DsmsVmConfigData();
                }

                throw new FileNotFoundException(
                    $"DSMS VM config not found: {configPath}\nCreate project-root/dsms-vm.json first, for example by copying the DSMS example file and filling the required keys.");
            }

            var json = File.ReadAllText(configPath);
            var data = JsonUtility.FromJson<DsmsVmConfigData>(json) ?? new DsmsVmConfigData();
            data.EnsureInitialized();
            return data;
        }

        private static string ResolveRequiredProjectFile(string fileName)
        {
            var assetsRoot = Path.Combine(ProjectRoot, "Assets");
            if (!Directory.Exists(assetsRoot))
            {
                throw new DirectoryNotFoundException($"Assets directory not found: {assetsRoot}");
            }

            var matches = Directory.GetFiles(assetsRoot, fileName, SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new FileNotFoundException(
                    $"Required file not found under Assets: {fileName}\nImport the DSMS sample or place the file in your project."),
                _ => throw new InvalidOperationException(
                    $"Multiple {fileName} files were found under Assets. Keep one canonical file or update DSMS tooling to disambiguate:\n- " +
                    string.Join("\n- ", matches))
            };
        }

        private static string ResolveDefaultLinuxServerBuildDirectory()
        {
            var fullPath = Path.GetFullPath(Path.Combine(ProjectRoot, DefaultLinuxServerBuildDirectory));
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException(
                    $"Linux server build directory not found: {fullPath}\nBuild it first with DSMS/Build/Build Linux Dedicated Server.");
            }

            var executablePath = Path.Combine(fullPath, "DedicatedServer.x86_64");
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    $"Linux server executable not found: {executablePath}\nBuild it first with DSMS/Build/Build Linux Dedicated Server.");
            }

            return fullPath;
        }

        private static string RequireTopLevelValue(string value, string keyName)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            throw new InvalidOperationException(
                $"Missing required key in dsms-vm.json: {keyName}\nFill project-root/dsms-vm.json before running this menu command.");
        }

        private static string RequireSlotValue(string value, string keyName)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            throw new InvalidOperationException(
                $"Missing required key in dsms-vm.json: {keyName}\nFill project-root/dsms-vm.json before running this menu command.");
        }

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
            EditorUtility.DisplayProgressBar("DSMS", $"{label}...", 0.5f);
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

    [Serializable]
    internal sealed class DsmsVmConfigData
    {
        public string defaultAvailabilityZone = string.Empty;
        public string defaultBlueprintId = string.Empty;
        public string defaultBundleId = string.Empty;
        public string environment = string.Empty;
        public string currentWorkSlot = "A";
        public string projectId = string.Empty;
        public string projectName = string.Empty;
        public DsmsVmSlotsData slots = new();

        public string DefaultAvailabilityZone => defaultAvailabilityZone ?? string.Empty;
        public string DefaultBlueprintId => defaultBlueprintId ?? string.Empty;
        public string DefaultBundleId => defaultBundleId ?? string.Empty;
        public string Environment => environment ?? string.Empty;
        public string CurrentWorkSlot => NormalizeSlot(currentWorkSlot);
        public string ProjectId => projectId ?? string.Empty;
        public string ProjectName => projectName ?? string.Empty;

        public void EnsureInitialized()
        {
            slots ??= new DsmsVmSlotsData();
            slots.A ??= new DsmsVmSlotData();
            slots.B ??= new DsmsVmSlotData();
        }

        public DsmsVmSlotData GetSlot(string slot)
        {
            EnsureInitialized();
            return NormalizeSlot(slot) == "B" ? slots.B : slots.A;
        }

        public static string NormalizeSlot(string slot)
        {
            return string.Equals(slot?.Trim(), "B", StringComparison.OrdinalIgnoreCase) ? "B" : "A";
        }
    }

    [Serializable]
    internal sealed class DsmsVmSlotsData
    {
        public DsmsVmSlotData A = new();
        public DsmsVmSlotData B = new();
    }

    [Serializable]
    internal sealed class DsmsVmSlotData
    {
        public bool enabled;
        public string host = string.Empty;
        public string instanceName = string.Empty;
        public string launcherBaseUrl = string.Empty;
        public string launcherToken = string.Empty;
        public int maxConcurrentMatches = 3;
        public string publicIp = string.Empty;
        public string sshKeyPath = string.Empty;
    }
}
