using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.MeshOutline
{
    /// <summary>
    /// One hidden, never-saved renderer that draws the outline for one source renderer. It is a
    /// child of the source with an identity transform, so it follows the source for free; a
    /// skinned part shares the source's bones, so it deforms with it.
    /// </summary>
    internal sealed class OutlinePart
    {
        internal const string ObjectName = "Outline Part";

        internal readonly Renderer Source;
        internal readonly GameObject GameObject;
        internal readonly Renderer Renderer;
        private readonly MeshFilter filter;

        /// <summary>The source mesh the outline mesh was baked from.</summary>
        internal Mesh SourceMesh { get; private set; }

        /// <summary>The outline mesh being drawn.</summary>
        internal Mesh Mesh { get; private set; }

        /// <summary>Hidden in edit mode because its source only appears in LOD levels past LOD0.</summary>
        internal bool HiddenByLod;

        private OutlinePart(Renderer source, GameObject gameObject, Renderer renderer, MeshFilter filter, Mesh sourceMesh, Mesh mesh)
        {
            Source = source;
            GameObject = gameObject;
            Renderer = renderer;
            this.filter = filter;
            SourceMesh = sourceMesh;
            Mesh = mesh;
        }

        internal static OutlinePart Create(ObjectOutline owner, Renderer source, Mesh sourceMesh, Mesh mesh, Material[] materials)
        {
            var gameObject = new GameObject(ObjectName) { hideFlags = HideFlags.HideAndDontSave };
            gameObject.transform.SetParent(source.transform, worldPositionStays: false);
            gameObject.AddComponent<OutlinePartMarker>().Initialize(owner, source);

            Renderer renderer;
            MeshFilter filter = null;
            if (source is SkinnedMeshRenderer skinnedSource)
            {
                var skinned = gameObject.AddComponent<SkinnedMeshRenderer>();
                skinned.sharedMesh = mesh;
                CopySkinning(skinnedSource, skinned);
                skinned.skinnedMotionVectors = false;
                renderer = skinned;
            }
            else
            {
                filter = gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                renderer = gameObject.AddComponent<MeshRenderer>();
            }

            // Hidden until the outline syncs it: the outline may be disabled.
            renderer.enabled = false;
            // A purely visual overlay: opt out of everything that costs performance.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            renderer.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            renderer.sharedMaterials = materials;

            return new OutlinePart(source, gameObject, renderer, filter, sourceMesh, mesh);
        }

        internal void SetMesh(Mesh sourceMesh, Mesh mesh)
        {
            SourceMesh = sourceMesh;
            Mesh = mesh;
            if (Renderer is SkinnedMeshRenderer skinned)
            {
                skinned.sharedMesh = mesh;
                // A swapped skinned mesh (e.g. new equipment) usually comes with its own bones.
                SyncSkinning();
            }
            else filter.sharedMesh = mesh;
        }

        /// <summary>
        /// Re-copies a skinned source's bones, root bone, quality and bounds, e.g. after equipment
        /// was re-bound to another skeleton. Bones are only reassigned when they changed.
        /// </summary>
        internal void SyncSkinning()
        {
            if (Source is SkinnedMeshRenderer source && Renderer is SkinnedMeshRenderer skinned) CopySkinning(source, skinned);
        }

        /// <summary>Same bones, root and quality as the source, so both deform identically.</summary>
        private static void CopySkinning(SkinnedMeshRenderer source, SkinnedMeshRenderer target)
        {
            Transform[] bones = source.bones;
            if (!SameBones(bones, target.bones)) target.bones = bones;
            target.rootBone = source.rootBone;
            target.quality = source.quality;
            target.updateWhenOffscreen = source.updateWhenOffscreen;
            target.localBounds = source.localBounds;
        }

        private static bool SameBones(Transform[] a, Transform[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!ReferenceEquals(a[i], b[i])) return false;
            }
            return true;
        }

        /// <summary>Copies the source's layer, rendering layers, enabled state and blend-shape weights.</summary>
        internal void Sync(bool outlineVisible)
        {
            if (Source == null || Renderer == null) return;
            GameObject.layer = Source.gameObject.layer;
            Renderer.renderingLayerMask = Source.renderingLayerMask;
            Renderer.enabled = outlineVisible && Source.enabled && !HiddenByLod;

            if (Source is SkinnedMeshRenderer skinnedSource && Renderer is SkinnedMeshRenderer skinned && Mesh != null)
            {
                // The source's mesh may have been swapped since the last Refresh: stay in range.
                Mesh sourceMesh = skinnedSource.sharedMesh;
                int count = sourceMesh != null ? Mathf.Min(Mesh.blendShapeCount, sourceMesh.blendShapeCount) : 0;
                for (int i = 0; i < count; i++) skinned.SetBlendShapeWeight(i, skinnedSource.GetBlendShapeWeight(i));
            }
        }

        internal void Destroy() => Destroy(immediate: !Application.isPlaying);

        internal void Destroy(bool immediate)
        {
            if (GameObject == null) return;
            if (immediate) Object.DestroyImmediate(GameObject);
            else Object.Destroy(GameObject);
        }
    }
}
