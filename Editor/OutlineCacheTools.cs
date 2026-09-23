using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>Maintenance operations on the baked-mesh cache (Project Settings, Tools menu).</summary>
    internal static class OutlineCacheTools
    {
        internal const string MenuRoot = "Tools/Mesh Outline/";

        [MenuItem(MenuRoot + "Rebake All Outlines", priority = 1)]
        private static void RebakeAllMenu()
        {
            int rebaked = RebakeAll(out int orphaned);
            string orphans = orphaned > 0 ? $" {orphaned} baked meshes have no source anymore; Clean Up Unused Baked Meshes removes them." : string.Empty;
            Debug.Log($"[MeshOutline] Rebaked {rebaked} outline meshes.{orphans}");
        }

        [MenuItem(MenuRoot + "Clean Up Unused Baked Meshes...", priority = 2)]
        private static void CleanUpMenu() => CleanUpWithConfirmation();

        [MenuItem(MenuRoot + "Settings...", priority = 20)]
        private static void OpenSettings() => SettingsService.OpenProjectSettings(MeshOutlineSettingsProvider.Path);

        /// <summary>Rebakes every cached mesh from its source, even if it looks up to date.</summary>
        internal static int RebakeAll(out int orphaned)
        {
            int rebaked = 0;
            orphaned = 0;
            foreach (string path in OutlineBakeCache.GetAllEntryPaths())
            {
                Mesh source = OutlineBakeCache.LoadSourceOf(path);
                if (source == null || !OutlineBakeCache.TryGetKey(source, out string key))
                {
                    orphaned++;
                    continue;
                }
                OutlineBakeCache.GetOrBake(source, key, force: true);
                rebaked++;
            }
            OutlineBakeValidator.RequestAll();
            return rebaked;
        }

        /// <summary>
        /// Cached meshes nothing references: no asset in the project (scenes, prefabs, ...) and no
        /// outline currently loaded, including in unsaved scenes.
        /// </summary>
        internal static List<string> FindUnusedEntries()
        {
            var entries = new HashSet<string>(OutlineBakeCache.GetAllEntryPaths());
            if (entries.Count == 0) return new List<string>();

            var used = new HashSet<string>();
            string[] referencing = AssetDatabase.GetAllAssetPaths().Where(p => !entries.Contains(p)).ToArray();
            foreach (string dependency in AssetDatabase.GetDependencies(referencing, recursive: false))
            {
                if (entries.Contains(dependency)) used.Add(dependency);
            }
            foreach (ObjectOutline outline in ObjectOutline.Live)
            {
                if (outline == null) continue;
                foreach (ObjectOutline.BakedPart part in outline.BakedParts)
                {
                    if (part.mesh != null) used.Add(AssetDatabase.GetAssetPath(part.mesh));
                }
            }
            return entries.Where(e => !used.Contains(e)).OrderBy(e => e).ToList();
        }

        internal static void CleanUpWithConfirmation()
        {
            List<string> unused = FindUnusedEntries();
            if (unused.Count == 0)
            {
                EditorUtility.DisplayDialog("Mesh Outline", "Every baked mesh is in use.", "OK");
                return;
            }
            string list = string.Join("\n", unused.Take(15).Select(Path.GetFileNameWithoutExtension));
            if (unused.Count > 15) list += $"\n... and {unused.Count - 15} more";
            if (!EditorUtility.DisplayDialog("Clean Up Unused Baked Meshes",
                    $"Delete {unused.Count} baked meshes that no scene, prefab or loaded outline uses?\n\n{list}",
                    "Delete", "Cancel"))
            {
                return;
            }
            DeleteEntries(unused);
        }

        internal static void DeleteEntries(IEnumerable<string> paths)
        {
            var failed = new List<string>();
            AssetDatabase.DeleteAssets(paths.ToArray(), failed);
            OutlineBakeCache.Invalidate();
            foreach (string path in failed) Debug.LogWarning($"[MeshOutline] Couldn't delete {path}.");
        }

        /// <summary>
        /// Moves the cache to a new folder. Assets keep their GUIDs, so every reference stays valid.
        /// </summary>
        internal static void MoveCache(string newFolder)
        {
            newFolder = newFolder.TrimEnd('/');
            if (newFolder == OutlineBakeCache.Folder) return;

            List<string> entries = OutlineBakeCache.GetAllEntryPaths();
            OutlineBakeCache.EnsureFolder(newFolder);
            foreach (string path in entries)
            {
                string target = AssetDatabase.GenerateUniqueAssetPath($"{newFolder}/{Path.GetFileName(path)}");
                string error = AssetDatabase.MoveAsset(path, target);
                if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"[MeshOutline] Couldn't move {path}: {error}");
            }
            if (OutlineBakeCache.FolderOverride != null) OutlineBakeCache.FolderOverride = newFolder;
            else MeshOutlineSettings.instance.SetBakedMeshFolder(newFolder);
            OutlineBakeCache.Invalidate();
        }

        /// <summary>Number of baked meshes and their total size on disk.</summary>
        internal static (int count, long bytes) GetStats()
        {
            List<string> entries = OutlineBakeCache.GetAllEntryPaths();
            long bytes = 0;
            foreach (string path in entries)
            {
                var file = new FileInfo(path);
                if (file.Exists) bytes += file.Length;
            }
            return (entries.Count, bytes);
        }
    }
}
