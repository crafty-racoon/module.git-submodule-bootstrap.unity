using UnityEditor;
using UnityEngine;

namespace CraftyRacoon.GitSubmoduleBootstrap.Editor
{
    internal sealed class GitSubmoduleUpdateWindow : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(420f, 150f);

        private int missingCount;
        private int outdatedCount;
        private double openedAt;

        internal static GitSubmoduleUpdateWindow Open(int missingCount, int outdatedCount)
        {
            var window = CreateInstance<GitSubmoduleUpdateWindow>();
            window.titleContent = new GUIContent("Git Submodules");
            window.missingCount = missingCount;
            window.outdatedCount = outdatedCount;
            window.minSize = WindowSize;
            window.maxSize = WindowSize;

            var mainWindow = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                mainWindow.x + (mainWindow.width - WindowSize.x) * 0.5f,
                mainWindow.y + (mainWindow.height - WindowSize.y) * 0.5f,
                WindowSize.x,
                WindowSize.y);
            window.ShowUtility();
            window.Focus();
            return window;
        }

        private void OnEnable()
        {
            openedAt = EditorApplication.timeSinceStartup;
        }

        private void Update()
        {
            Repaint();
        }

        private void OnGUI()
        {
            GUILayout.Space(14f);
            EditorGUILayout.LabelField(
                "正在初始化或更新 Git submodules…",
                EditorStyles.boldLabel);
            GUILayout.Space(6f);
            EditorGUILayout.LabelField(
                $"未初始化：{missingCount}　需要更新：{outdatedCount}");
            GUILayout.Space(10f);

            var progressRect = GUILayoutUtility.GetRect(1f, 20f, GUILayout.ExpandWidth(true));
            var progress = (float)((EditorApplication.timeSinceStartup - openedAt) % 1.0);
            EditorGUI.ProgressBar(progressRect, progress, "處理中，請稍候…");

            GUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "完成後此視窗會自動關閉。",
                MessageType.Info);
        }
    }
}

