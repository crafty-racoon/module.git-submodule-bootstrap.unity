using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CraftyRacoon.GitSubmoduleBootstrap.Editor
{
    [InitializeOnLoad]
    internal static class GitSubmoduleUpdater
    {
        private const string MenuRoot = "Tools/Git Submodules/";
        private const string UpdateMenuPath = MenuRoot + "Update Now";
        private const string AutoUpdateMenuPath = MenuRoot + "Update on Project Open";
        private const string SessionKey = "CraftyRacoon.GitSubmoduleBootstrap.StartupUpdateAttempted";
        private const string PreferencePrefix = "CraftyRacoon.GitSubmoduleBootstrap.AutoUpdate.";
        private const int MaximumLogLength = 12000;

        private static Task<GitUpdateResult> updateTask;

        static GitSubmoduleUpdater()
        {
            EditorApplication.delayCall += RunStartupUpdate;
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string GitModulesPath => Path.Combine(ProjectRoot, ".gitmodules");

        private static string GitMetadataPath => Path.Combine(ProjectRoot, ".git");

        private static string AutoUpdatePreferenceKey =>
            PreferencePrefix + Application.dataPath.Replace('\\', '/');

        private static bool AutoUpdateEnabled =>
            EditorPrefs.GetBool(AutoUpdatePreferenceKey, true);

        private static bool IsGitWorkingTree =>
            Directory.Exists(GitMetadataPath) || File.Exists(GitMetadataPath);

        private static void RunStartupUpdate()
        {
            if (Application.isBatchMode || !AutoUpdateEnabled ||
                SessionState.GetBool(SessionKey, false) || !File.Exists(GitModulesPath))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            StartUpdate(true);
        }

        [MenuItem(UpdateMenuPath, priority = 2000)]
        private static void UpdateNow()
        {
            StartUpdate(false);
        }

        [MenuItem(UpdateMenuPath, true)]
        private static bool ValidateUpdateNow()
        {
            return updateTask == null && File.Exists(GitModulesPath) && IsGitWorkingTree;
        }

        [MenuItem(AutoUpdateMenuPath, priority = 2001)]
        private static void ToggleAutoUpdate()
        {
            var enabled = !AutoUpdateEnabled;
            EditorPrefs.SetBool(AutoUpdatePreferenceKey, enabled);
            Menu.SetChecked(AutoUpdateMenuPath, enabled);
            Debug.Log($"[Git Submodules] Update on project open {(enabled ? "enabled" : "disabled")}.");
        }

        [MenuItem(AutoUpdateMenuPath, true)]
        private static bool ValidateAutoUpdate()
        {
            Menu.SetChecked(AutoUpdateMenuPath, AutoUpdateEnabled);
            return true;
        }

        private static void StartUpdate(bool automatic)
        {
            if (updateTask != null)
            {
                if (!automatic)
                {
                    Debug.Log("[Git Submodules] An update is already running.");
                }

                return;
            }

            if (!File.Exists(GitModulesPath))
            {
                if (!automatic)
                {
                    Debug.LogWarning($"[Git Submodules] No .gitmodules file was found at {GitModulesPath}.");
                }

                return;
            }

            if (!IsGitWorkingTree)
            {
                Debug.LogWarning($"[Git Submodules] {ProjectRoot} is not a Git working tree; update skipped.");
                return;
            }

            var projectRoot = ProjectRoot;
            Debug.Log($"[Git Submodules] Running git submodule update --init --recursive in {projectRoot}...");
            updateTask = Task.Run(() => ExecuteGitUpdate(projectRoot));
            EditorApplication.update += PollUpdate;
        }

        private static GitUpdateResult ExecuteGitUpdate(string projectRoot)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "submodule update --init --recursive",
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // An automatic project-open hook must never wait for an interactive credential prompt.
                startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                startInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";

                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start())
                    {
                        return new GitUpdateResult(-1, string.Empty, "Git failed to start.");
                    }

                    var standardOutput = process.StandardOutput.ReadToEndAsync();
                    var standardError = process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    Task.WaitAll(standardOutput, standardError);

                    return new GitUpdateResult(
                        process.ExitCode,
                        standardOutput.Result,
                        standardError.Result);
                }
            }
            catch (Exception exception)
            {
                return new GitUpdateResult(-1, string.Empty, exception.ToString());
            }
        }

        private static void PollUpdate()
        {
            if (updateTask == null || !updateTask.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PollUpdate;
            var result = updateTask.Result;
            updateTask = null;

            var output = FormatOutput(result.StandardOutput, result.StandardError);
            if (result.ExitCode == 0)
            {
                Debug.Log("[Git Submodules] Update completed successfully." + output);
                AssetDatabase.Refresh();
                return;
            }

            Debug.LogError(
                $"[Git Submodules] Update failed with exit code {result.ExitCode}. " +
                "Authenticate Git or run the command manually, then use Tools > Git Submodules > Update Now." +
                output);
        }

        private static string FormatOutput(string standardOutput, string standardError)
        {
            var output = string.Join(
                Environment.NewLine,
                new[] { standardOutput.Trim(), standardError.Trim() });
            output = output.Trim();

            if (string.IsNullOrEmpty(output))
            {
                return string.Empty;
            }

            if (output.Length > MaximumLogLength)
            {
                output = output.Substring(0, MaximumLogLength) +
                         Environment.NewLine + "[output truncated]";
            }

            return Environment.NewLine + output;
        }

        private sealed class GitUpdateResult
        {
            public GitUpdateResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            public int ExitCode { get; }

            public string StandardOutput { get; }

            public string StandardError { get; }
        }
    }
}

