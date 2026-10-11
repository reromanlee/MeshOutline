using System.Collections.Generic;
using UnityEngine;

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
        private static readonly HashSet<Mesh> reportedUnreadable = new HashSet<Mesh>();

        /// <summary>Returns a shared outline mesh for <paramref name="source"/>, or null if it can't be read.</summary>
        internal static Mesh Acquire(Mesh source, Object context)
        {
            if (bySource.TryGetValue(source, out Entry entry) && entry.Baked != null)
            {
                entry.Users++;
                return entry.Baked;
            }

            if (!source.isReadable)
            {
                if (reportedUnreadable.Add(source))
                {
                    Debug.LogError(
                        $"[MeshOutline] '{context.name}' can't be outlined at runtime: mesh '{source.name}' isn't readable. " +
                        "Either enable Read/Write in its import settings, or add the ObjectOutline in the editor " +
                        "(e.g. on the prefab) so the outline is baked ahead of time.", context);
                }
                return null;
            }

            var baked = new Mesh { name = source.name + " (Outline)", hideFlags = HideFlags.HideAndDontSave };
            OutlineMeshBaker.Bake(source, baked);
            baked.UploadMeshData(markNoLongerReadable: true);

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
            if (Application.isPlaying) Object.Destroy(baked);
            else Object.DestroyImmediate(baked);
        }
    }
}
