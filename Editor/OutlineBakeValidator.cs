using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BakedPart = reromanlee.MeshOutline.ObjectOutline.BakedPart;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>
    /// Keeps each outline's baked parts in sync with its renderers: bakes missing or outdated
    /// outline meshes (into the shared cache, or into the scene for source meshes that aren't
    /// assets) and records them on the outline. Requests are batched and run on the next editor
    /// tick.
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
            foreach (ObjectOutline outline in batch) Validate(outline, force: false);
        }

        /// <summary>Rebakes an outline's meshes even if they look up to date.</summary>
        internal static void Rebake(ObjectOutline outline) => Validate(outline, force: true);

        private static void Validate(ObjectOutline outline, bool force)
        {
            if (!ShouldValidate(outline)) return;

            outline.GatherCandidates(candidates, includeExcluded: false);
            IReadOnlyList<BakedPart> current = outline.BakedParts;
            var next = new List<BakedPart>(candidates.Count);
            bool changed = candidates.Count != current.Count;
            for (int i = 0; i < candidates.Count; i++)
            {
                Renderer renderer = candidates[i];
                BakedPart existing = Find(current, renderer);
                BakedPart part = Resolve(outline, renderer, ObjectOutline.GetSourceMesh(renderer), existing, force);
                next.Add(part);
                if (!changed && !Same(part, current[i])) changed = true;
            }

            if (!changed)
            {
                outline.Refresh();
                return;
            }

            outline.SetBakedParts(next);
            EditorUtility.SetDirty(outline);
            if (PrefabUtility.IsPartOfPrefabInstance(outline)) PrefabUtility.RecordPrefabInstancePropertyModifications(outline);
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
            string hash = OutlineMeshBaker.Fingerprint(sourceMesh).ToString();
            if (!force && existing.mesh != null && existing.sourceMesh == sourceMesh && existing.sourceHash == hash &&
                !EditorUtility.IsPersistent(existing.mesh))
            {
                return existing;
            }

            var mesh = new Mesh { name = sourceMesh.name + " (Outline)" };
            OutlineMeshBaker.Bake(sourceMesh, mesh);
            DestroyIfUnused(existing.mesh, outline);
            return new BakedPart { renderer = renderer, sourceMesh = sourceMesh, mesh = mesh, sourceHash = hash };
        }

        private static bool ShouldValidate(ObjectOutline outline)
        {
            if (outline == null || EditorUtility.IsPersistent(outline)) return false;
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
