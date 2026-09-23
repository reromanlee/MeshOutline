using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.MeshOutline
{
    /// <summary>
    /// One hidden, never-saved renderer that draws the outline for one source renderer. It is a
    /// child of the source with an identity transform, so it follows the source for free.
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

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();

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
            filter.sharedMesh = mesh;
        }

        /// <summary>Copies the source's layer, rendering layers and enabled state.</summary>
        internal void Sync(bool outlineVisible)
        {
            if (Source == null || Renderer == null) return;
            GameObject.layer = Source.gameObject.layer;
            Renderer.renderingLayerMask = Source.renderingLayerMask;
            Renderer.enabled = outlineVisible && Source.enabled;
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
