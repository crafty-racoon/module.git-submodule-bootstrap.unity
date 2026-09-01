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

        private static Task<GitCommandResult> commandTask;
        private static OperationPhase operationPhase;
        private static string activeProjectRoot;
        private static bool activeRequestIsAutomatic;
        private static bool assetRefreshSuspended;
        private static GitSubmoduleUpdateWindow progressWindow;

        static GitSubmoduleUpdater()
        {
            EditorApplication.delayCall += RunStartupUpdate;
            EditorApplication.quitting += HandleEditorQuitting;
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
            return commandTask == null && File.Exists(GitModulesPath) && IsGitWorkingTree;
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
            if (commandTask != null)
            {
                if (!automatic)
                {
                    Debug.Log("[Git Submodules] A check or update is already running.");
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

            activeProjectRoot = ProjectRoot;
            activeRequestIsAutomatic = automatic;
            operationPhase = OperationPhase.Detecting;
            commandTask = Task.Run(
                () => ExecuteGit(activeProjectRoot, "submodule status --recursive"));
            EditorApplication.update += PollOperation;
        }

        private static void PollOperation()
        {
            if (commandTask == null || !commandTask.IsCompleted)
            {
                return;
            }

            var completedPhase = operationPhase;
            var result = commandTask.Result;
            commandTask = null;

            if (completedPhase == OperationPhase.Detecting)
            {
                CompleteDetection(result);
                return;
            }

            CompleteUpdate(result);
        }

        private static void CompleteDetection(GitCommandResult result)
        {
            var output = FormatOutput(result.StandardOutput, result.StandardError);
            if (result.ExitCode != 0)
            {
                Debug.LogError(
                    $"[Git Submodules] Status check failed with exit code {result.ExitCode}." + output);
                FinishOperation();
                return;
            }

            var status = GitSubmoduleStatus.Parse(result.StandardOutput);
            if (status.ConflictedCount > 0)
            {
                Debug.LogError(
                    $"[Git Submodules] {status.ConflictedCount} submodule path(s) contain merge conflicts. " +
                    "Resolve them before updating.");
                FinishOperation();
                return;
            }

            if (!status.RequiresUpdate)
            {
                if (!activeRequestIsAutomatic)
                {
                    Debug.Log("[Git Submodules] All submodules already match the parent repository gitlinks.");
                }

                FinishOperation();
                return;
            }

            Debug.Log(
                $"[Git Submodules] Detected {status.MissingCount} uninitialized and " +
                $"{status.OutdatedCount} outdated submodule path(s); starting update.");
            progressWindow = GitSubmoduleUpdateWindow.Open(
                status.MissingCount,
                status.OutdatedCount);
            AssetDatabase.DisallowAutoRefresh();
            assetRefreshSuspended = true;

            operationPhase = OperationPhase.Updating;
            commandTask = Task.Run(
                () => ExecuteGit(activeProjectRoot, "submodule update --init --recursive"));
        }

        private static void CompleteUpdate(GitCommandResult result)
        {
            CloseProgressWindow();
            ResumeAssetRefresh();

            var output = FormatOutput(result.StandardOutput, result.StandardError);
            if (result.ExitCode == 0)
            {
                Debug.Log("[Git Submodules] Update completed successfully." + output);
                FinishOperation();
                AssetDatabase.Refresh();
                return;
            }

            Debug.LogError(
                $"[Git Submodules] Update failed with exit code {result.ExitCode}. " +
                "Authenticate Git or run the command manually, then use Tools > Git Submodules > Update Now." +
                output);
            FinishOperation();
        }

        private static GitCommandResult ExecuteGit(string projectRoot, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
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
                        return new GitCommandResult(-1, string.Empty, "Git failed to start.");
                    }

                    var standardOutput = process.StandardOutput.ReadToEndAsync();
                    var standardError = process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    Task.WaitAll(standardOutput, standardError);

                    return new GitCommandResult(
                        process.ExitCode,
                        standardOutput.Result,
                        standardError.Result);
                }
            }
            catch (Exception exception)
            {
                return new GitCommandResult(-1, string.Empty, exception.ToString());
            }
        }

        private static void FinishOperation()
        {
            EditorApplication.update -= PollOperation;
            operationPhase = OperationPhase.Idle;
            commandTask = null;
            activeProjectRoot = null;
            activeRequestIsAutomatic = false;
            CloseProgressWindow();
            ResumeAssetRefresh();
        }

        private static void CloseProgressWindow()
        {
            if (progressWindow == null)
            {
                return;
            }

            progressWindow.Close();
            progressWindow = null;
        }

        private static void ResumeAssetRefresh()
        {
            if (!assetRefreshSuspended)
            {
                return;
            }

            AssetDatabase.AllowAutoRefresh();
            assetRefreshSuspended = false;
        }

        private static void HandleEditorQuitting()
        {
            CloseProgressWindow();
            ResumeAssetRefresh();
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

        private enum OperationPhase
        {
            Idle,
            Detecting,
            Updating
        }

        private sealed class GitCommandResult
        {
            public GitCommandResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            public int ExitCode { get; }

            public string StandardOutput { get; }

            public string StandardError { get; }
        }

        internal readonly struct GitSubmoduleStatus
        {
            private GitSubmoduleStatus(int missingCount, int outdatedCount, int conflictedCount)
            {
                MissingCount = missingCount;
                OutdatedCount = outdatedCount;
                ConflictedCount = conflictedCount;
            }

            public int MissingCount { get; }

            public int OutdatedCount { get; }

            public int ConflictedCount { get; }

            public bool RequiresUpdate => MissingCount > 0 || OutdatedCount > 0;

            public static GitSubmoduleStatus Parse(string output)
            {
                var missingCount = 0;
                var outdatedCount = 0;
                var conflictedCount = 0;
                var lines = output.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    switch (line[0])
                    {
                        case '-':
                            missingCount++;
                            break;
                        case '+':
                            outdatedCount++;
                            break;
                        case 'U':
                            conflictedCount++;
                            break;
                    }
                }

                return new GitSubmoduleStatus(
                    missingCount,
                    outdatedCount,
                    conflictedCount);
            }
        }
    }
}
