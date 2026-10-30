using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace reromanlee.MeshOutline
{
    /// <summary>
    /// Outline meshes baked at runtime, for outlines whose parts have no editor bake (e.g. added
    /// with AddComponent in a build). Shared per source mesh and freed when the last user goes.
    /// </summary>
    internal static class RuntimeBakeCache
    {
        private sealed class Entry
        {
            public Mesh Source;
            public Mesh Baked;
            public int Users;
        }

        private static readonly Dictionary<Mesh, Entry> bySource = new Dictionary<Mesh, Entry>();
        private static readonly Dictionary<Mesh, Entry> byBaked = new Dictionary<Mesh, Entry>();

        // Meshes that couldn't be baked, so each problem is reported once rather than per outline.
        private static readonly HashSet<Mesh> reported = new HashSet<Mesh>();

        /// <summary>
        /// Returns a shared outline mesh for <paramref name="source"/>, the mesh of
        /// <paramref name="renderer"/>, or null if it can't be baked here.
        /// </summary>
        internal static Mesh Acquire(Renderer renderer, Mesh source, Object context)
        {
            if (source == null) return null;
            if (bySource.TryGetValue(source, out Entry entry) && entry.Baked != null)
            {
                entry.Users++;
                return entry.Baked;
            }

            // Static batching swaps the renderer's mesh for the whole batch's, already merged in
            // world space: the renderer's own mesh is gone.
            if (renderer.isPartOfStaticBatch)
            {
                ReportOnce(source, context,
                    $"[MeshOutline] '{context.name}' can't outline '{renderer.name}' at runtime: it's static batched, so its own mesh " +
                    "is no longer available. Add the ObjectOutline in the editor (in the scene or on the prefab) so the outline " +
                    "is baked ahead of time, or turn off Batching Static on it.");
                return null;
            }

            if (!source.isReadable)
            {
                ReportOnce(source, context,
                    $"[MeshOutline] '{context.name}' can't be outlined at runtime: mesh '{source.name}' isn't readable. " +
                    "Either enable Read/Write in its import settings, or add the ObjectOutline in the editor " +
                    "(e.g. on the prefab) so the outline is baked ahead of time.");
                return null;
            }

            var baked = new Mesh { name = source.name + " (Outline)", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                OutlineMeshBaker.Bake(source, baked);
                baked.UploadMeshData(markNoLongerReadable: true);
            }
            catch (Exception exception)
            {
                DestroyMesh(baked);
                if (reported.Add(source)) Debug.LogException(exception, context);
                return null;
            }

            entry = new Entry { Source = source, Baked = baked, Users = 1 };
            bySource[source] = entry;
            byBaked[baked] = entry;
            return baked;
        }

        internal static bool Owns(Mesh baked) => baked != null && byBaked.ContainsKey(baked);

        internal static void Release(Mesh baked)
        {
            if (baked == null || !byBaked.TryGetValue(baked, out Entry entry)) return;
            if (--entry.Users > 0) return;

            byBaked.Remove(baked);
            bySource.Remove(entry.Source);
            DestroyMesh(baked);
        }

        private static void ReportOnce(Mesh source, Object context, string message)
        {
            if (reported.Add(source)) Debug.LogError(message, context);
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (Application.isPlaying) Object.Destroy(mesh);
            else Object.DestroyImmediate(mesh);
        }
    }
}
