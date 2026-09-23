using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>Edit &gt; Project Settings &gt; Mesh Outline.</summary>
    internal static class MeshOutlineSettingsProvider
    {
        internal const string Path = "Project/Mesh Outline";

        private static readonly GUIContent FolderLabel = new GUIContent("Baked Mesh Folder",
            "Where outline meshes are baked, one per source mesh, shared by every scene and prefab. Changing it moves the existing bakes; references stay valid.");

        [SettingsProvider]
        private static SettingsProvider Create() => new SettingsProvider(Path, SettingsScope.Project)
        {
            label = "Mesh Outline",
            guiHandler = _ => OnGUI(),
            keywords = new HashSet<string> { "outline", "bake", "baked", "mesh", "cache", "folder" }
        };

        private static void OnGUI()
        {
            using (new PageLayout())
            {
                string current = OutlineBakeCache.Folder;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string typed = EditorGUILayout.DelayedTextField(FolderLabel, current);
                    if (EditorGUI.EndChangeCheck()) TryMoveCache(typed);
                    if (GUILayout.Button("Choose...", GUILayout.Width(80f)))
                    {
                        string picked = EditorUtility.OpenFolderPanel("Baked Mesh Folder", Application.dataPath, string.Empty);
                        if (!string.IsNullOrEmpty(picked)) TryMoveCache(ToProjectPath(picked));
                    }
                }

                (int count, long bytes) = OutlineCacheTools.GetStats();
                EditorGUILayout.LabelField(" ", $"{count} baked meshes, {EditorUtility.FormatBytes(bytes)}", EditorStyles.miniLabel);

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUIUtility.labelWidth);
                    if (GUILayout.Button(new GUIContent("Rebake All", "Rebake every cached outline mesh from its source mesh.")))
                    {
                        OutlineCacheTools.RebakeAll(out _);
                    }
                    if (GUILayout.Button(new GUIContent("Clean Up Unused...", "Delete baked meshes no scene, prefab or loaded outline uses.")))
                    {
                        OutlineCacheTools.CleanUpWithConfirmation();
                    }
                }
            }
        }

        private static void TryMoveCache(string folder)
        {
            folder = folder?.Replace('\\', '/').Trim().TrimEnd('/');
            if (!MeshOutlineSettings.IsValidFolder(folder))
            {
                EditorUtility.DisplayDialog("Mesh Outline", "The baked mesh folder must be inside the project's Assets folder.", "OK");
                return;
            }
            OutlineCacheTools.MoveCache(folder);
        }

        private static string ToProjectPath(string absolute)
        {
            string assets = Application.dataPath.Replace('\\', '/');
            absolute = absolute.Replace('\\', '/');
            return absolute.StartsWith(assets) ? "Assets" + absolute.Substring(assets.Length) : absolute;
        }

        /// <summary>The margins and label width Unity's own Project Settings pages use.</summary>
        private sealed class PageLayout : IDisposable
        {
            private readonly float labelWidth;

            public PageLayout()
            {
                labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 200f;
                GUILayout.BeginHorizontal();
                GUILayout.Space(10f);
                GUILayout.BeginVertical();
                GUILayout.Space(10f);
            }

            public void Dispose()
            {
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                EditorGUIUtility.labelWidth = labelWidth;
            }
        }
    }
}
