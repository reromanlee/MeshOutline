using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace reromanlee.MeshOutline
{
    /// <summary>
    /// Draws a constant-width outline around this GameObject's meshes and, with
    /// <see cref="IncludeChildren"/>, its children's meshes, merged into one silhouette. Show or
    /// hide it with <c>enabled</c>.
    /// </summary>
    /// <remarks>
    /// Nothing runs per frame. The outline is drawn by hidden, never-saved renderers built once in
    /// <c>Awake</c>, from outline meshes the editor bakes ahead of time into a shared cache
    /// (Project Settings &gt; Mesh Outline). Outlines added at runtime bake on the spot instead,
    /// which requires Read/Write-enabled meshes.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Object Outline")]
    [HelpURL("https://github.com/reromanlee/MeshOutline#readme")]
    public sealed class ObjectOutline : MonoBehaviour
    {
        /// <summary>The outline mesh baked for one source renderer.</summary>
        [Serializable]
        internal struct BakedPart
        {
            public Renderer renderer;
            public Mesh sourceMesh;
            public Mesh mesh;

            /// <summary>
            /// Fingerprint of <see cref="sourceMesh"/> at bake time. Only used when the bake is
            /// stored in the scene because the source mesh isn't an asset (e.g. ProBuilder's).
            /// </summary>
            public string sourceHash;
        }

        [SerializeField, ColorUsage(true, true), FormerlySerializedAs("outlineColor")]
        [Tooltip("Outline color. HDR colors glow when bloom is enabled.")]
        private Color color = Color.white;

        [SerializeField, Min(0f), FormerlySerializedAs("outlineWidth")]
        [Tooltip("Width in pixels at 1080p. The outline covers the same share of the screen at any resolution and field of view.")]
        private float width = 4f;

        [SerializeField]
        [Tooltip("Normal: hidden behind other geometry, like any object. X-Ray: always visible, even through walls.")]
        private OutlineOcclusion occlusion = OutlineOcclusion.Normal;

        [SerializeField]
        [Tooltip("Also outline child renderers, merged into one silhouette. A child with its own Object Outline is outlined separately.")]
        private bool includeChildren = true;

        [SerializeField]
        [Tooltip("Renderers under this outline that shouldn't be outlined.")]
        private List<Renderer> excludedRenderers = new List<Renderer>();

        [SerializeField]
        [Tooltip("Optional material for the fill pass, e.g. an animated outline. Its shader must follow the built-in fill shader's _StencilRef and _ZTest conventions.")]
        private Material customFillMaterial;

        // Written by the editor. Serialized so builds and prefab instances render without any
        // runtime baking.
        [SerializeField, HideInInspector] private List<BakedPart> bakedParts = new List<BakedPart>();

        // 1.0.0 saved a hidden child object here. It's deleted on load: parts are never saved now.
        [SerializeField, HideInInspector, FormerlySerializedAs("outlineGameObject")]
        private GameObject legacyGeneratedObject;

        // Runtime state, derived from the fields above and never saved.
        private readonly List<OutlinePart> parts = new List<OutlinePart>();
        private readonly List<Renderer> partSources = new List<Renderer>();
        private Material maskMaterial;
        private Material fillMaterial;
        private Material[] materials;
        private Material materialsBuiltFrom;
        private int stencilRef;
        private bool built;

        private static readonly List<ObjectOutline> live = new List<ObjectOutline>();
        private static readonly List<Renderer> rendererBuffer = new List<Renderer>();
        private static readonly List<Renderer> candidateBuffer = new List<Renderer>();

        /// <summary>Outline color. HDR colors glow when bloom is enabled.</summary>
        public Color Color
        {
            get => color;
            set
            {
                color = value;
                ApplyMaterialProperties();
            }
        }

        /// <summary>
        /// Width in pixels at 1080p: the outline covers the same share of the screen at any
        /// resolution and field of view.
        /// </summary>
        public float Width
        {
            get => width;
            set
            {
                width = Mathf.Max(0f, value);
                ApplyMaterialProperties();
            }
        }

        /// <summary>Whether the outline hides behind other geometry or is always visible.</summary>
        public OutlineOcclusion Occlusion
        {
            get => occlusion;
            set
            {
                occlusion = value;
                ApplyMaterialProperties();
            }
        }

        /// <summary>Whether child renderers are outlined too, merged into one silhouette.</summary>
        public bool IncludeChildren
        {
            get => includeChildren;
            set
            {
                if (includeChildren == value) return;
                includeChildren = value;
                Refresh();
            }
        }

        /// <summary>The renderers currently outlined.</summary>
        public IReadOnlyList<Renderer> Parts => partSources;

        /// <summary>
        /// Optional material for the fill pass, e.g. an animated outline. Its shader must follow
        /// the built-in fill shader's <c>_StencilRef</c> and <c>_ZTest</c> conventions. The
        /// outline uses a copy; the material itself is never modified.
        /// </summary>
        public Material CustomFillMaterial
        {
            get => customFillMaterial;
            set
            {
                if (customFillMaterial == value) return;
                customFillMaterial = value;
                if (built) RebuildMaterials();
            }
        }

        /// <summary>Excludes a renderer under this outline from it, or includes it again.</summary>
        public void SetExcluded(Renderer renderer, bool excluded)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (excludedRenderers.Contains(renderer) == excluded) return;
            if (excluded) excludedRenderers.Add(renderer);
            else excludedRenderers.RemoveAll(r => r == renderer);
            Refresh();
        }

        /// <summary>
        /// Re-gathers the outlined renderers after the hierarchy changed at runtime (meshes added,
        /// removed, re-parented or swapped), and copies each source renderer's enabled state and
        /// layer again. Cheap when nothing changed. The editor calls this automatically.
        /// </summary>
        public void Refresh()
        {
            // Not awake yet (e.g. added to an inactive object): Awake will build everything.
            if (!built) return;
            SyncParts();
            SyncState();
        }

        private void Awake()
        {
            if (!live.Contains(this)) live.Add(this);
            RemoveLegacyObject();
            Build();
        }

        private void OnEnable()
        {
            if (!live.Contains(this)) live.Add(this);
            // Awake doesn't run again after a domain reload, which tears everything down.
            if (!built) Build();
            stencilRef = StencilAllocator.Acquire(this);
            ApplyMaterialProperties();
            SyncState();
#if UNITY_EDITOR
            if (!Application.isPlaying) OutlineEditorBridge.RequestValidation?.Invoke(this);
#endif
        }

        private void OnDisable()
        {
            StencilAllocator.Release(stencilRef, this);
            stencilRef = 0;
            SyncState();
        }

        private void OnDestroy()
        {
            live.Remove(this);
            StencilAllocator.Release(stencilRef, this);
            TearDown(immediate: !Application.isPlaying);
        }

        private void Build()
        {
            built = true;
            DestroyInheritedParts();
            RebuildMaterials();
            SyncParts();
        }

        /// <summary>
        /// Instantiate, duplicate and copy/paste copy hidden children too. Delete the parts this
        /// outline inherited that way (their materials belong to the original, so only the objects
        /// go) before building its own.
        /// </summary>
        private void DestroyInheritedParts()
        {
            foreach (OutlinePartMarker marker in GetComponentsInChildren<OutlinePartMarker>(true))
            {
                // Parts of nested outlines are theirs to manage.
                if (marker.Owner != null && marker.Owner != this) continue;
                if (FindPart(marker.gameObject) != null) continue;
                DestroyImmediate(marker.gameObject);
            }
        }

        private void RebuildMaterials()
        {
            DestroyMaterial(ref maskMaterial, immediate: !Application.isPlaying);
            DestroyMaterial(ref fillMaterial, immediate: !Application.isPlaying);
            maskMaterial = CreateMaterial(OutlineShaders.Mask, null, "Outline Mask", OutlineShaders.MaskQueue);
            fillMaterial = CreateMaterial(OutlineShaders.Fill, customFillMaterial, "Outline Fill", OutlineShaders.FillQueue);
            materialsBuiltFrom = customFillMaterial;
            materials = new[] { maskMaterial, fillMaterial };
            ApplyMaterialProperties();
            foreach (OutlinePart part in parts)
            {
                if (part.Renderer != null) part.Renderer.sharedMaterials = materials;
            }
        }

        private static Material CreateMaterial(Shader shader, Material template, string materialName, int queue)
        {
            Material material = template != null ? new Material(template) : shader != null ? new Material(shader) : null;
            if (material == null) return null;
            material.name = materialName + " (Instance)";
            material.hideFlags = HideFlags.HideAndDontSave;
            // Forced even for custom materials: every mask must render before any fill.
            material.renderQueue = queue;
            return material;
        }

        private void ApplyMaterialProperties()
        {
            float zTest = occlusion == OutlineOcclusion.XRay ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual;
            if (maskMaterial != null)
            {
                maskMaterial.SetFloat(OutlineShaders.ZTestId, zTest);
                maskMaterial.SetFloat(OutlineShaders.StencilRefId, stencilRef);
            }
            if (fillMaterial != null)
            {
                fillMaterial.SetFloat(OutlineShaders.ZTestId, zTest);
                fillMaterial.SetFloat(OutlineShaders.StencilRefId, stencilRef);
                fillMaterial.SetColor(OutlineShaders.ColorId, color);
                fillMaterial.SetFloat(OutlineShaders.WidthId, width);
            }
        }

        /// <summary>Creates, updates and removes parts so there is one per outlined renderer.</summary>
        private void SyncParts()
        {
            GatherCandidates(candidateBuffer, includeExcluded: false);

            for (int i = parts.Count - 1; i >= 0; i--)
            {
                OutlinePart part = parts[i];
                if (part.GameObject != null && part.Source != null && candidateBuffer.Contains(part.Source)) continue;
                DestroyPart(part);
                parts.RemoveAt(i);
            }

            partSources.Clear();
            bool missingBakes = false;
            foreach (Renderer source in candidateBuffer)
            {
                Mesh sourceMesh = GetSourceMesh(source);
                Mesh baked = FindBakedMesh(source, sourceMesh);
                OutlinePart part = FindPart(source);

                bool upToDate = part != null && part.SourceMesh == sourceMesh && part.Mesh != null &&
                                (baked != null ? part.Mesh == baked : RuntimeBakeCache.Owns(part.Mesh));
                if (!upToDate)
                {
                    Mesh mesh = baked;
                    if (mesh == null && Application.isPlaying) mesh = RuntimeBakeCache.Acquire(sourceMesh, this);
                    if (mesh == null)
                    {
                        missingBakes = true;
                        if (part != null)
                        {
                            DestroyPart(part);
                            parts.Remove(part);
                        }
                        continue;
                    }

                    if (part == null)
                    {
                        parts.Add(OutlinePart.Create(this, source, sourceMesh, mesh, materials));
                    }
                    else
                    {
                        RuntimeBakeCache.Release(part.Mesh);
                        part.SetMesh(sourceMesh, mesh);
                    }
                }
                partSources.Add(source);
            }

#if UNITY_EDITOR
            if (missingBakes && !Application.isPlaying) OutlineEditorBridge.RequestValidation?.Invoke(this);
#endif
        }

        /// <summary>Copies each source's layer and enabled state to its part.</summary>
        private void SyncState()
        {
            bool visible = isActiveAndEnabled;
            foreach (OutlinePart part in parts) part.Sync(visible);
        }

        private Mesh FindBakedMesh(Renderer source, Mesh sourceMesh)
        {
            foreach (BakedPart baked in bakedParts)
            {
                if (baked.renderer == source && baked.sourceMesh == sourceMesh && baked.mesh != null) return baked.mesh;
            }
            return null;
        }

        private OutlinePart FindPart(Renderer source)
        {
            foreach (OutlinePart part in parts)
            {
                if (part.Source == source) return part;
            }
            return null;
        }

        private OutlinePart FindPart(GameObject partObject)
        {
            foreach (OutlinePart part in parts)
            {
                if (part.GameObject == partObject) return part;
            }
            return null;
        }

        private static void DestroyPart(OutlinePart part)
        {
            RuntimeBakeCache.Release(part.Mesh);
            part.Destroy();
        }

        /// <summary>Destroys all parts and materials; the next OnEnable rebuilds them.</summary>
        internal void TearDown(bool immediate)
        {
            foreach (OutlinePart part in parts)
            {
                RuntimeBakeCache.Release(part.Mesh);
                part.Destroy(immediate);
            }
            parts.Clear();
            partSources.Clear();
            DestroyMaterial(ref maskMaterial, immediate);
            DestroyMaterial(ref fillMaterial, immediate);
            materials = null;
            built = false;
        }

        private static void DestroyMaterial(ref Material material, bool immediate)
        {
            if (material == null) return;
            if (immediate) DestroyImmediate(material);
            else Destroy(material);
            material = null;
        }

        private void RemoveLegacyObject()
        {
            if (legacyGeneratedObject == null) return;
            GameObject legacy = legacyGeneratedObject;
            legacyGeneratedObject = null;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Objects that come from a prefab can't be deleted from an instance of it.
                if (PrefabUtility.IsPartOfPrefabInstance(legacy)) legacy.SetActive(false);
                else DestroyImmediate(legacy);
                EditorUtility.SetDirty(this);
                return;
            }
#endif
            Destroy(legacy);
        }

        /// <summary>
        /// The renderers this outline covers: its own and, with <see cref="IncludeChildren"/>, its
        /// children's, minus renderers that belong to a nested outline.
        /// </summary>
        internal void GatherCandidates(List<Renderer> results, bool includeExcluded)
        {
            results.Clear();
            if (includeChildren) GetComponentsInChildren(true, rendererBuffer);
            else GetComponents(rendererBuffer);

            foreach (Renderer renderer in rendererBuffer)
            {
                if (!IsOutlinable(renderer)) continue;
                // The nearest outline above a renderer owns it.
                if (renderer.GetComponentInParent<ObjectOutline>(true) != this) continue;
                if (!includeExcluded && excludedRenderers.Contains(renderer)) continue;
                results.Add(renderer);
            }
        }

        private static bool IsOutlinable(Renderer renderer)
        {
            // Skips our own parts and other tools' transient helper objects.
            if ((renderer.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) return false;
            return GetSourceMesh(renderer) != null;
        }

        internal static Mesh GetSourceMesh(Renderer renderer)
        {
            if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter)) return filter.sharedMesh;
            return null;
        }

        // Internal accessors for the editor and tests.
        internal static IReadOnlyList<ObjectOutline> Live => live;
        internal IReadOnlyList<BakedPart> BakedParts => bakedParts;
        internal IReadOnlyList<OutlinePart> BuiltParts => parts;
        internal Material MaskMaterial => maskMaterial;
        internal Material FillMaterial => fillMaterial;
        internal int StencilRef => stencilRef;
        internal bool IsExcluded(Renderer renderer) => excludedRenderers.Contains(renderer);

        internal void SetBakedParts(List<BakedPart> value)
        {
            bakedParts = value;
            Refresh();
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void TearDownBeforeDomainReload()
        {
            // A domain reload forgets the references to parts and materials, which are never saved:
            // destroy them first so they aren't leaked. OnEnable rebuilds them afterwards.
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    if (live[i] != null) live[i].TearDown(immediate: true);
                }
            };
        }

        private void OnValidate()
        {
            width = Mathf.Max(0f, width);
            ApplyMaterialProperties();
            // Not awake yet, or a prefab asset.
            if (!built) return;
            // Structural changes (children, exclusions, custom material) create and destroy objects,
            // which OnValidate doesn't allow: defer them one editor tick. (update rather than
            // delayCall, which doesn't run in batch mode.)
            EditorApplication.update -= DeferredRefresh;
            EditorApplication.update += DeferredRefresh;
        }

        private void DeferredRefresh()
        {
            EditorApplication.update -= DeferredRefresh;
            if (this == null || !built) return;
            // About to enter play mode: the scene reloads anyway.
            if (EditorApplication.isPlayingOrWillChangePlaymode && !Application.isPlaying) return;
            if (materialsBuiltFrom != customFillMaterial) RebuildMaterials();
            Refresh();
            if (!Application.isPlaying) OutlineEditorBridge.RequestValidation?.Invoke(this);
        }
#endif
    }
}
