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
                // Same bones, root and quality as the source, so both deform identically.
                skinned.bones = skinnedSource.bones;
                skinned.rootBone = skinnedSource.rootBone;
                skinned.quality = skinnedSource.quality;
                skinned.updateWhenOffscreen = skinnedSource.updateWhenOffscreen;
                skinned.localBounds = skinnedSource.localBounds;
                skinned.skinnedMotionVectors = false;
                renderer = skinned;
            }
            else
            {
                filter = gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                renderer = gameObject.AddComponent<MeshRenderer>();
            }

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
            if (Renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh = mesh;
            else filter.sharedMesh = mesh;
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
                int count = Mesh.blendShapeCount;
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
