using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BakedPart = reromanlee.MeshOutline.ObjectOutline.BakedPart;
using Object = UnityEngine.Object;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>
    /// Keeps each outline's baked parts in sync with its renderers: bakes missing or outdated
    /// outline meshes (into the shared cache, or into the scene for source meshes that aren't
    /// assets) and records them on the outline. Requests are batched and run on the next editor
    /// tick. Excluded renderers are baked too, so including them again at runtime costs nothing.
    /// </summary>
    [InitializeOnLoad]
    internal static class OutlineBakeValidator
    {
        private static readonly HashSet<ObjectOutline> pending = new HashSet<ObjectOutline>();
        private static readonly List<Renderer> candidates = new List<Renderer>();
        private static bool scheduled;

        static OutlineBakeValidator()
        {
            OutlineEditorBridge.RequestValidation = Request;
            // Outlines loaded before this assembly (e.g. right after a domain reload).
            RequestAll();
        }

        internal static void Request(ObjectOutline outline)
        {
            if (outline == null) return;
            pending.Add(outline);
            if (scheduled) return;
            scheduled = true;
            // update rather than delayCall: delayCall doesn't run in batch mode.
            EditorApplication.update += Flush;
        }

        internal static void RequestAll()
        {
            foreach (ObjectOutline outline in ObjectOutline.Live) Request(outline);
        }

        /// <summary>Processes all pending requests now (the next editor tick does it otherwise).</summary>
        internal static void Flush()
        {
            EditorApplication.update -= Flush;
            scheduled = false;
            // Play mode bakes at runtime; nothing done to scene objects there would be kept.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                pending.Clear();
                return;
            }

            var batch = new List<ObjectOutline>(pending);
            pending.Clear();
            foreach (ObjectOutline outline in batch) TryValidate(outline, force: false);
        }

        /// <summary>Rebakes an outline's meshes even if they look up to date.</summary>
        internal static void Rebake(ObjectOutline outline) => TryValidate(outline, force: true);

        /// <summary>
        /// Prefab assets whose outlines can be baked in place: not model prefabs, and not prefabs
        /// in read-only packages.
        /// </summary>
        internal static bool IsEditablePrefabAsset(ObjectOutline outline) =>
            outline != null && PrefabUtility.IsPartOfPrefabAsset(outline) && !PrefabUtility.IsPartOfImmutablePrefab(outline);

        /// <summary>
        /// For builds: gives every part without an up-to-date bake one, reusing a valid cache entry
        /// or else baking into the scene being built, and never writes an asset. Describes each
        /// part it baked in <paramref name="baked"/>.
        /// </summary>
        internal static void BakeMissingForBuild(ObjectOutline outline, List<string> baked)
        {
            outline.GatherCandidates(candidates, includeExcluded: true);
            IReadOnlyList<BakedPart> current = outline.BakedParts;
            List<BakedPart> next = null;
            foreach (Renderer renderer in candidates)
            {
                // Static batching has already swapped its mesh: there's nothing left to bake from.
                if (renderer.isPartOfStaticBatch) continue;
                Mesh sourceMesh = ObjectOutline.GetSourceMesh(renderer);
                if (IsUpToDate(Find(current, renderer), sourceMesh)) continue;

                Mesh cached = OutlineBakeCache.TryGetKey(sourceMesh, out string key) ? OutlineBakeCache.FindUpToDate(sourceMesh, key) : null;
                BakedPart part = cached != null
                    ? new BakedPart { renderer = renderer, sourceMesh = sourceMesh, mesh = cached }
                    : BakeIntoScene(renderer, sourceMesh);

                if (next == null) next = new List<BakedPart>(current);
                next.RemoveAll(p => p.renderer == renderer);
                next.Add(part);
                baked.Add($"{PathOf(renderer.transform)} (mesh '{sourceMesh.name}')");
            }
            if (next != null) outline.SetBakedParts(next);
        }

        /// <summary>One broken mesh must not stop the rest of the batch from baking.</summary>
        private static void TryValidate(ObjectOutline outline, bool force)
        {
            try
            {
                bool changed = Validate(outline, force);
                // Prefab assets aren't saved with a scene: save them like an inspector edit would.
                if (IsEditablePrefabAsset(outline) && (changed || EditorUtility.IsDirty(outline)))
                {
                    PrefabUtility.SavePrefabAsset(outline.transform.root.gameObject);
                }
            }
            catch (Exception exception)
            {
                // The outline as context: clicking the log selects it.
                Debug.LogException(exception, outline);
            }
        }

        /// <summary>Returns whether the outline's baked parts changed.</summary>
        private static bool Validate(ObjectOutline outline, bool force)
        {
            if (!ShouldValidate(outline)) return false;
            bool asset = EditorUtility.IsPersistent(outline);

            outline.GatherCandidates(candidates, includeExcluded: true);
            IReadOnlyList<BakedPart> current = outline.BakedParts;
            var next = new List<BakedPart>(candidates.Count);
            foreach (Renderer renderer in candidates)
            {
                Mesh sourceMesh = ObjectOutline.GetSourceMesh(renderer);
                // A prefab asset can only reference assets: a mesh that isn't one is baked in each
                // scene instance instead, or at runtime.
                if (asset && !OutlineBakeCache.TryGetKey(sourceMesh, out _)) continue;
                next.Add(Resolve(outline, renderer, sourceMesh, Find(current, renderer), force));
            }

            bool changed = next.Count != current.Count;
            for (int i = 0; !changed && i < next.Count; i++) changed = !Same(next[i], current[i]);
            if (!changed)
            {
                outline.Refresh();
                return false;
            }

            outline.SetBakedParts(next);
            EditorUtility.SetDirty(outline);
            if (!asset && PrefabUtility.IsPartOfPrefabInstance(outline)) PrefabUtility.RecordPrefabInstancePropertyModifications(outline);
            return true;
        }

        private static BakedPart Resolve(ObjectOutline outline, Renderer renderer, Mesh sourceMesh, BakedPart existing, bool force)
        {
            if (OutlineBakeCache.TryGetKey(sourceMesh, out string key))
            {
                return new BakedPart
                {
                    renderer = renderer,
                    sourceMesh = sourceMesh,
                    mesh = OutlineBakeCache.GetOrBake(sourceMesh, key, force)
                };
            }

            // The source mesh isn't an asset (procedural, ProBuilder...): store the bake in the
            // scene. A fingerprint catches the source being edited in place.
            if (!force && IsUpToDate(existing, sourceMesh) && !EditorUtility.IsPersistent(existing.mesh)) return existing;
            BakedPart part = BakeIntoScene(renderer, sourceMesh);
            DestroyIfUnused(existing.mesh, outline);
            return part;
        }

        private static BakedPart BakeIntoScene(Renderer renderer, Mesh sourceMesh)
        {
            var mesh = new Mesh { name = sourceMesh.name + " (Outline)" };
            OutlineMeshBaker.Bake(sourceMesh, mesh);
            return new BakedPart
            {
                renderer = renderer,
                sourceMesh = sourceMesh,
                mesh = mesh,
                sourceHash = OutlineMeshBaker.Fingerprint(sourceMesh).ToString()
            };
        }

        /// <summary>
        /// Whether a part's bake matches its source. Cached bakes are kept fresh on import;
        /// scene-stored ones carry a fingerprint of the source they were baked from.
        /// </summary>
        private static bool IsUpToDate(BakedPart part, Mesh sourceMesh)
        {
            if (part.mesh == null || part.sourceMesh != sourceMesh) return false;
            return EditorUtility.IsPersistent(part.mesh) || part.sourceHash == OutlineMeshBaker.Fingerprint(sourceMesh).ToString();
        }

        private static bool ShouldValidate(ObjectOutline outline)
        {
            if (outline == null) return false;
            if (EditorUtility.IsPersistent(outline)) return IsEditablePrefabAsset(outline);
            var scene = outline.gameObject.scene;
            if (!scene.IsValid()) return false;
            // Leave previews alone (asset thumbnails, scripts using LoadPrefabContents); Prefab
            // Mode is a preview scene too, but a real editing context.
            return !EditorSceneManager.IsPreviewScene(scene) || PrefabStageUtility.GetPrefabStage(outline.gameObject) != null;
        }

        private static BakedPart Find(IReadOnlyList<BakedPart> parts, Renderer renderer)
        {
            foreach (BakedPart part in parts)
            {
                if (part.renderer == renderer) return part;
            }
            return default;
        }

        private static bool Same(BakedPart a, BakedPart b) =>
            a.renderer == b.renderer && a.sourceMesh == b.sourceMesh && a.mesh == b.mesh &&
            (a.sourceHash ?? string.Empty) == (b.sourceHash ?? string.Empty);

        private static string PathOf(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }

        /// <summary>Destroys a replaced scene-stored bake unless another outline still uses it.</summary>
        private static void DestroyIfUnused(Mesh mesh, ObjectOutline owner)
        {
            if (mesh == null || EditorUtility.IsPersistent(mesh)) return;
            foreach (ObjectOutline outline in ObjectOutline.Live)
            {
                if (outline == null || outline == owner) continue;
                // e.g. a duplicate, which starts out sharing the original's scene bake.
                foreach (BakedPart part in outline.BakedParts)
                {
                    if (part.mesh == mesh) return;
                }
            }
            Object.DestroyImmediate(mesh);
        }
    }
}
