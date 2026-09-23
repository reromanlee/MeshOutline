using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>
    /// The shared outline-mesh cache: one baked mesh asset per source mesh asset, in the folder
    /// set under Project Settings &gt; Mesh Outline. Every outline of the same source mesh, in any
    /// scene or prefab, references the same baked asset.
    /// </summary>
    /// <remarks>
    /// Each baked asset records its source mesh, the bake format version and a fingerprint of
    /// the source in its importer's userData, so entries are found by source rather than by file
    /// name, and are rebaked in place (keeping their GUID, so every reference stays valid) when
    /// the source or the bake format changes.
    /// </remarks>
    internal static class OutlineBakeCache
    {
        [Serializable]
        private struct EntryInfo
        {
            public int version;
            public string source;
            public string fingerprint;
        }

        private const string FolderOverrideKey = "MeshOutline.BakeCache.FolderOverride";

        /// <summary>
        /// Lets tests redirect the cache to a scratch folder. Kept in SessionState so it survives
        /// the domain reloads of play mode tests.
        /// </summary>
        internal static string FolderOverride
        {
            get
            {
                string folder = SessionState.GetString(FolderOverrideKey, string.Empty);
                return folder.Length > 0 ? folder : null;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) SessionState.EraseString(FolderOverrideKey);
                else SessionState.SetString(FolderOverrideKey, value);
            }
        }

        internal static string Folder => FolderOverride ?? MeshOutlineSettings.instance.BakedMeshFolder;

        // Source key ("guid:localId") -> baked asset path. Built lazily from the folder's contents.
        private static Dictionary<string, string> pathByKey;

        // Entries checked against their source (format and fingerprint) this session.
        private static readonly Dictionary<string, Mesh> verified = new Dictionary<string, Mesh>();

        /// <summary>Whether <paramref name="source"/> is an asset, i.e. can be cached.</summary>
        internal static bool TryGetKey(Mesh source, out string key)
        {
            key = null;
            if (source == null || !EditorUtility.IsPersistent(source)) return false;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId)) return false;
            if (string.IsNullOrEmpty(guid)) return false;
            key = guid + ":" + localId;
            return true;
        }

        /// <summary>Returns the cached outline mesh for a source mesh asset, baking it if needed.</summary>
        internal static Mesh GetOrBake(Mesh source, string key, bool force = false)
        {
            if (!force && verified.TryGetValue(key, out Mesh known) && known != null) return known;

            string fingerprint = OutlineMeshBaker.Fingerprint(source).ToString();
            string path = FindPath(key);
            Mesh baked = path != null ? AssetDatabase.LoadAssetAtPath<Mesh>(path) : null;
            if (baked == null)
            {
                baked = Create(source, key, fingerprint);
            }
            else
            {
                EntryInfo info = ReadInfo(path);
                if (force || info.version != OutlineMeshBaker.FormatVersion || info.fingerprint != fingerprint)
                {
                    Rebake(source, baked, path, key, fingerprint);
                }
            }

            verified[key] = baked;
            return baked;
        }

        private static Mesh Create(Mesh source, string key, string fingerprint)
        {
            string folder = Folder;
            EnsureFolder(folder);
            // Name + hash of the source key: readable, and deterministic so two people baking
            // different meshes that share a name don't create conflicting files.
            string path = $"{folder}/{SanitizeFileName(source.name)} {Hash128.Compute(key).ToString().Substring(0, 8)}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) path = AssetDatabase.GenerateUniqueAssetPath(path);

            var mesh = new Mesh();
            OutlineMeshBaker.Bake(source, mesh);
            AssetDatabase.CreateAsset(mesh, path);
            WriteInfo(path, key, fingerprint);
            PathIndex[key] = path;
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private static void Rebake(Mesh source, Mesh baked, string path, string key, string fingerprint)
        {
            OutlineMeshBaker.Bake(source, baked);
            EditorUtility.SetDirty(baked);
            AssetDatabase.SaveAssetIfDirty(baked);
            WriteInfo(path, key, fingerprint);
        }

        /// <summary>
        /// Called for every import. Rebakes entries whose source asset was reimported, so every
        /// scene and prefab using them updates at once.
        /// </summary>
        internal static void OnAssetsChanged(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            string folderPrefix = Folder + "/";
            var sourceGuids = new HashSet<string>();
            foreach (string path in imported)
            {
                if (!path.StartsWith(folderPrefix, StringComparison.Ordinal))
                {
                    sourceGuids.Add(AssetDatabase.AssetPathToGUID(path));
                }
                else if (pathByKey != null)
                {
                    EntryInfo info = ReadInfo(path);
                    if (!string.IsNullOrEmpty(info.source)) pathByKey[info.source] = path;
                }
            }
            foreach (string[] paths in new[] { deleted, moved, movedFrom })
            {
                foreach (string path in paths)
                {
                    // Rare: rebuild the index lazily.
                    if (path.StartsWith(folderPrefix, StringComparison.Ordinal)) pathByKey = null;
                }
            }
            if (sourceGuids.Count == 0) return;

            var stale = new List<string>();
            foreach (string key in PathIndex.Keys)
            {
                if (sourceGuids.Contains(GuidOf(key))) stale.Add(key);
            }
            if (stale.Count == 0) return;

            foreach (string key in stale)
            {
                verified.Remove(key);
                staleKeys.Add(key);
            }
            // Rebaking writes assets, which isn't allowed while imports are being processed.
            EditorApplication.update -= RebakeStale;
            EditorApplication.update += RebakeStale;
        }

        private static readonly HashSet<string> staleKeys = new HashSet<string>();

        private static void RebakeStale()
        {
            EditorApplication.update -= RebakeStale;
            var keys = new List<string>(staleKeys);
            staleKeys.Clear();
            foreach (string key in keys)
            {
                Mesh source = LoadSource(key);
                if (source != null) GetOrBake(source, key);
            }
            OutlineBakeValidator.RequestAll();
        }

        /// <summary>Forgets session state, e.g. after the cache folder changed.</summary>
        internal static void Invalidate()
        {
            pathByKey = null;
            verified.Clear();
        }

        /// <summary>Paths of every baked mesh in the cache folder.</summary>
        internal static List<string> GetAllEntryPaths() => new List<string>(PathIndex.Values);

        /// <summary>The source mesh a cache entry was baked from, or null if it's gone.</summary>
        internal static Mesh LoadSourceOf(string entryPath)
        {
            EntryInfo info = ReadInfo(entryPath);
            return string.IsNullOrEmpty(info.source) ? null : LoadSource(info.source);
        }

        internal static bool IsCacheEntry(Mesh mesh)
        {
            if (mesh == null || !EditorUtility.IsPersistent(mesh)) return false;
            return !string.IsNullOrEmpty(ReadInfo(AssetDatabase.GetAssetPath(mesh)).source);
        }

        private static Dictionary<string, string> PathIndex
        {
            get
            {
                if (pathByKey != null) return pathByKey;
                pathByKey = new Dictionary<string, string>();
                if (AssetDatabase.IsValidFolder(Folder))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        EntryInfo info = ReadInfo(path);
                        if (!string.IsNullOrEmpty(info.source)) pathByKey[info.source] = path;
                    }
                }
                return pathByKey;
            }
        }

        private static string FindPath(string key) =>
            PathIndex.TryGetValue(key, out string path) && AssetDatabase.LoadAssetAtPath<Mesh>(path) != null ? path : null;

        private static Mesh LoadSource(string key)
        {
            int separator = key.IndexOf(':');
            if (separator < 0 || !long.TryParse(key.Substring(separator + 1), out long localId)) return null;
            string path = AssetDatabase.GUIDToAssetPath(GuidOf(key));
            if (string.IsNullOrEmpty(path)) return null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Mesh mesh &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string _, out long id) && id == localId)
                {
                    return mesh;
                }
            }
            return null;
        }

        private static string GuidOf(string key)
        {
            int separator = key.IndexOf(':');
            return separator < 0 ? key : key.Substring(0, separator);
        }

        private static EntryInfo ReadInfo(string path)
        {
            AssetImporter importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path);
            if (importer == null || string.IsNullOrEmpty(importer.userData)) return default;
            try
            {
                return JsonUtility.FromJson<EntryInfo>(importer.userData);
            }
            catch (ArgumentException)
            {
                return default;
            }
        }

        private static void WriteInfo(string path, string key, string fingerprint)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            string userData = JsonUtility.ToJson(new EntryInfo
            {
                version = OutlineMeshBaker.FormatVersion,
                source = key,
                fingerprint = fingerprint
            });
            if (importer == null || importer.userData == userData) return;
            importer.userData = userData;
            importer.SaveAndReimport();
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Mesh";
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return name.Trim();
        }
    }
}
