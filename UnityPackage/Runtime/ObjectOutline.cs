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

        private const float DefaultWidth = 4f;
        private const float DefaultReferenceDistance = 10f;
        private const float MinReferenceDistance = 0.01f;

        [SerializeField, ColorUsage(true, true), FormerlySerializedAs("outlineColor")]
        [Tooltip("Outline color. HDR colors glow when bloom is enabled.")]
        private Color color = Color.white;

        [SerializeField, Min(0f), FormerlySerializedAs("outlineWidth")]
        [Tooltip("Outline width, in pixels at 1080p (Exact Pixels: in screen pixels).")]
        private float width = DefaultWidth;

        [SerializeField]
        [Tooltip("Pixels at 1080p: the same share of the screen at any resolution, the same thickness at any distance.\n" +
                 "Exact Pixels: the same number of screen pixels at any resolution and distance.\n" +
                 "Scales with Distance: like part of the object; Width pixels (at 1080p, 60° field of view) at Reference Distance, thinner farther away, thicker up close.")]
        private OutlineWidthMode widthMode = OutlineWidthMode.PixelsAt1080p;

        [SerializeField, Min(MinReferenceDistance)]
        [Tooltip("Scales with Distance: the distance, in meters, at which the outline is Width pixels thick (at 1080p with a 60° field of view).")]
        private float referenceDistance = DefaultReferenceDistance;

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

        [SerializeField]
        [Tooltip("Copy each source renderer's enabled state, layer and blend-shape weights to the outline every frame, e.g. to follow facial animation. When off (no per-frame cost), they're copied when the outline is enabled and on Refresh().")]
        private bool trackSourceEveryFrame;

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

        // LODGroups this outline added its parts to (play mode only).
        private readonly List<LODGroup> lodGroups = new List<LODGroup>();

#if UNITY_EDITOR
        // Every loaded outline, for the editor's bake tools and domain reload teardown. Players
        // never read it, so builds don't pay for keeping it.
        private static readonly List<ObjectOutline> live = new List<ObjectOutline>();
#endif

        private static readonly List<Renderer> rendererBuffer = new List<Renderer>();
        private static readonly List<Renderer> candidateBuffer = new List<Renderer>();
        private static readonly List<Renderer> lodRendererBuffer = new List<Renderer>();
        private static readonly List<LODGroup> lodGroupBuffer = new List<LODGroup>();

        /// <summary>Outline color. HDR colors glow when bloom is enabled.</summary>
        /// <remarks>A color with a NaN or infinite component is ignored, with an error.</remarks>
        public Color Color
        {
            get => color;
            set
            {
                if (!IsFinite(value, nameof(Color))) return;
                color = value;
                ApplyMaterialProperties();
            }
        }

        /// <summary>
        /// Outline width in pixels: at 1080p by default, see <see cref="WidthMode"/>. Negative
        /// values become 0; NaN and infinity are ignored, with an error.
        /// </summary>
        public float Width
        {
            get => width;
            set
            {
                if (!IsFinite(value, nameof(Width))) return;
                width = Mathf.Max(0f, value);
                ApplyMaterialProperties();
            }
        }

        /// <summary>
        /// How <see cref="Width"/> is measured: pixels at 1080p (the same share of the screen at
        /// any resolution), exact pixels, or a world-space thickness that scales with distance.
        /// </summary>
        public OutlineWidthMode WidthMode
        {
            get => widthMode;
            set
            {
                widthMode = value;
                ApplyMaterialProperties();
            }
        }

        /// <summary>
        /// For <see cref="OutlineWidthMode.ScalesWithDistance"/>: the distance, in meters, at which
        /// the outline is <see cref="Width"/> pixels thick (at 1080p with a 60° field of view).
        /// At least 0.01; NaN and infinity are ignored, with an error.
        /// </summary>
        public float ReferenceDistance
        {
            get => referenceDistance;
            set
            {
                if (!IsFinite(value, nameof(ReferenceDistance))) return;
                referenceDistance = Mathf.Max(MinReferenceDistance, value);
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
        /// Copy each source renderer's enabled state, layer and blend-shape weights to the outline
        /// every frame (right before rendering), e.g. to follow facial animation. When off, the
        /// default, nothing runs per frame: they're copied when the outline is enabled and on
        /// <see cref="Refresh"/>.
        /// </summary>
        public bool TrackSourceEveryFrame
        {
            get => trackSourceEveryFrame;
            set
            {
                trackSourceEveryFrame = value;
                UpdateTracking();
            }
        }

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
            else
            {
                for (int i = excludedRenderers.Count - 1; i >= 0; i--)
                {
                    if (excludedRenderers[i] == renderer) excludedRenderers.RemoveAt(i);
                }
            }
            Refresh();
        }

        /// <summary>Whether a renderer was excluded with <see cref="SetExcluded"/> or in the inspector.</summary>
        public bool IsExcluded(Renderer renderer) => renderer != null && excludedRenderers.Contains(renderer);

        /// <summary>
        /// Re-gathers the outlined renderers after the hierarchy changed at runtime (meshes added,
        /// removed, re-parented or swapped), and copies each source renderer's enabled state,
        /// layer and blend-shape weights again. Cheap when nothing changed. The editor calls this
        /// automatically.
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
            Register();
            RemoveLegacyObject();
            Build();
        }

        private void OnEnable()
        {
            Register();
            // Awake doesn't run again after a domain reload, which tears everything down.
            if (!built) Build();
            stencilRef = StencilAllocator.Acquire(this);
            ApplyMaterialProperties();
            SyncState();
            UpdateTracking();
#if UNITY_EDITOR
            if (!Application.isPlaying) OutlineEditorBridge.RequestValidation?.Invoke(this);
#endif
        }

        private void OnDisable()
        {
            StencilAllocator.Release(stencilRef, this);
            stencilRef = 0;
            SyncState();
            UpdateTracking();
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            live.Remove(this);
#endif
            OutlineTracking.Remove(this);
            StencilAllocator.Release(stencilRef, this);
            TearDown(immediate: !Application.isPlaying);
        }

        private void Register()
        {
#if UNITY_EDITOR
            if (!live.Contains(this)) live.Add(this);
#endif
        }

        private void UpdateTracking()
        {
            if (trackSourceEveryFrame && isActiveAndEnabled) OutlineTracking.Add(this);
            else OutlineTracking.Remove(this);
        }

        private void Build()
        {
            built = true;
            DestroyInheritedParts();
            RebuildMaterials();
            SyncParts();
            // Awake also runs for a disabled component, and then no OnEnable follows to hide the
            // new parts.
            SyncState();
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
            OutlineShaders.CheckCustomFill(customFillMaterial, this);
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
                fillMaterial.SetFloat(OutlineShaders.WidthId, ShaderWidth);
                fillMaterial.SetFloat(OutlineShaders.WidthModeId, (float)widthMode);
            }
        }

        // World units a pixel of a 1080p-high frame spans per meter of distance, with a 60°
        // vertical field of view: 2 * tan(30°) / 1080.
        private static readonly float worldUnitsPerPixelAt1080pPerMeter = 2f * Mathf.Tan(30f * Mathf.Deg2Rad) / 1080f;

        /// <summary>
        /// The fill shader's _OutlineWidth: pixels in the screen modes; for Scales with Distance,
        /// the world-space thickness that is <see cref="width"/> pixels at <see cref="referenceDistance"/>.
        /// </summary>
        internal float ShaderWidth => widthMode == OutlineWidthMode.ScalesWithDistance
            ? width * referenceDistance * worldUnitsPerPixelAt1080pPerMeter
            : width;

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
                OutlinePart part = FindPart(source);
                Mesh baked = FindBakedMesh(source, part, out Mesh sourceMesh);

                bool upToDate = part != null && part.SourceMesh == sourceMesh && part.Mesh != null &&
                                (baked != null ? part.Mesh == baked : RuntimeBakeCache.Owns(part.Mesh));
                if (upToDate) part.SyncSkinning();
                else
                {
                    Mesh mesh = baked;
                    if (mesh == null && Application.isPlaying) mesh = RuntimeBakeCache.Acquire(source, sourceMesh, this);
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

            SyncLods();

#if UNITY_EDITOR
            if (missingBakes && !Application.isPlaying) OutlineEditorBridge.RequestValidation?.Invoke(this);
#endif
        }

        /// <summary>Copies each source's layer, enabled state and blend-shape weights to its part.</summary>
        internal void SyncState()
        {
            bool visible = isActiveAndEnabled;
            foreach (OutlinePart part in parts) part.Sync(visible);
        }

        /// <summary>
        /// Keeps parts in step with LODGroups. At runtime each part joins its source's LOD level,
        /// so it switches and culls with it at no per-frame cost. In edit mode, where changing a
        /// saved LODGroup would leave overrides behind, parts of levels past LOD0 are hidden.
        /// </summary>
        private void SyncLods()
        {
            if (!Application.isPlaying)
            {
                foreach (OutlinePart part in parts) part.HiddenByLod = FindLodLevel(part.Source, out _) > 0;
                return;
            }

            // The groups parts belong in, plus the ones they joined before and may have to leave.
            lodGroupBuffer.Clear();
            foreach (LODGroup group in lodGroups)
            {
                if (group != null && !lodGroupBuffer.Contains(group)) lodGroupBuffer.Add(group);
            }
            foreach (OutlinePart part in parts)
            {
                if (part.Renderer == null || FindLodLevel(part.Source, out LODGroup group) < 0) continue;
                if (!lodGroupBuffer.Contains(group)) lodGroupBuffer.Add(group);
            }

            lodGroups.Clear();
            foreach (LODGroup group in lodGroupBuffer)
            {
                if (SyncLodGroup(group, attach: true)) lodGroups.Add(group);
            }
            lodGroupBuffer.Clear();
        }

        private void DetachFromLodGroups()
        {
            foreach (LODGroup group in lodGroups)
            {
                if (group != null) SyncLodGroup(group, attach: false);
            }
            lodGroups.Clear();
        }

        /// <summary>
        /// Puts each part in exactly the levels that list its source (or, to detach, in none) with
        /// one <c>SetLODs</c> call, skipped when the group already matches. Returns whether any
        /// part is in the group.
        /// </summary>
        private bool SyncLodGroup(LODGroup group, bool attach)
        {
            LOD[] lods = group.GetLODs();
            bool changed = false;
            bool joined = false;
            for (int level = 0; level < lods.Length; level++)
            {
                Renderer[] current = lods[level].renderers;
                lodRendererBuffer.Clear();
                foreach (Renderer renderer in current)
                {
                    if (renderer != null && !IsOwnPart(renderer)) lodRendererBuffer.Add(renderer);
                }
                if (attach)
                {
                    foreach (OutlinePart part in parts)
                    {
                        if (part.Renderer == null || Array.IndexOf(current, part.Source) < 0) continue;
                        lodRendererBuffer.Add(part.Renderer);
                        joined = true;
                    }
                }
                if (SameRenderers(current, lodRendererBuffer)) continue;
                lods[level].renderers = lodRendererBuffer.ToArray();
                changed = true;
            }
            if (changed) group.SetLODs(lods);
            lodRendererBuffer.Clear();
            return joined;
        }

        private static bool SameRenderers(Renderer[] current, List<Renderer> next)
        {
            if (current.Length != next.Count) return false;
            for (int i = 0; i < current.Length; i++)
            {
                if (!ReferenceEquals(current[i], next[i])) return false;
            }
            return true;
        }

        private bool IsOwnPart(Renderer renderer) =>
            renderer.TryGetComponent(out OutlinePartMarker marker) && marker.Owner == this;

        /// <summary>The lowest LOD level listing this renderer, or -1 if no LODGroup above it does.</summary>
        private static int FindLodLevel(Renderer renderer, out LODGroup group)
        {
            for (Transform t = renderer.transform; t != null; t = t.parent)
            {
                if (!t.TryGetComponent(out group)) continue;
                LOD[] lods = group.GetLODs();
                for (int level = 0; level < lods.Length; level++)
                {
                    if (Array.IndexOf(lods[level].renderers, renderer) >= 0) return level;
                }
            }
            group = null;
            return -1;
        }

        /// <summary>
        /// The baked outline mesh for a source renderer, and the source mesh it stands for. A
        /// static-batched renderer reports its batch's combined mesh instead of its own, so it's
        /// matched by renderer alone and keeps the mesh it was baked or built from.
        /// </summary>
        private Mesh FindBakedMesh(Renderer source, OutlinePart part, out Mesh sourceMesh)
        {
            sourceMesh = GetSourceMesh(source);
            bool staticBatched = source.isPartOfStaticBatch;
            foreach (BakedPart baked in bakedParts)
            {
                if (baked.renderer != source || baked.mesh == null) continue;
                if (staticBatched && baked.sourceMesh != null) sourceMesh = baked.sourceMesh;
                if (baked.sourceMesh == sourceMesh) return baked.mesh;
            }
            if (staticBatched && part != null && part.SourceMesh != null) sourceMesh = part.SourceMesh;
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
            DetachFromLodGroups();
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
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter)) return filter.sharedMesh;
            return null;
        }

        private bool IsFinite(float value, string property)
        {
            if (!float.IsNaN(value) && !float.IsInfinity(value)) return true;
            Debug.LogError($"[MeshOutline] {property} of '{name}' must be a finite number, not {value}; the value was ignored.", this);
            return false;
        }

        private bool IsFinite(Color value, string property)
        {
            if (IsFinite(value)) return true;
            Debug.LogError($"[MeshOutline] {property} of '{name}' must have finite components, not {value}; the value was ignored.", this);
            return false;
        }

        // float.IsFinite is missing from the .NET Framework API compatibility level.
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFinite(Color value) => IsFinite(value.r) && IsFinite(value.g) && IsFinite(value.b) && IsFinite(value.a);

        // Internal accessors for the editor and tests.
        internal IReadOnlyList<BakedPart> BakedParts => bakedParts;
        internal IReadOnlyList<OutlinePart> BuiltParts => parts;
        internal Material MaskMaterial => maskMaterial;
        internal Material FillMaterial => fillMaterial;
        internal int StencilRef => stencilRef;

        internal void SetBakedParts(List<BakedPart> value)
        {
            bakedParts = value;
            Refresh();
        }

#if UNITY_EDITOR
        internal static IReadOnlyList<ObjectOutline> Live => live;

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
            // Hand-edited or merged YAML can hold anything: never pass NaN or infinity to the shader.
            width = IsFinite(width) ? Mathf.Max(0f, width) : DefaultWidth;
            referenceDistance = IsFinite(referenceDistance) ? Mathf.Max(MinReferenceDistance, referenceDistance) : DefaultReferenceDistance;
            if (!IsFinite(color)) color = Color.white;
            ApplyMaterialProperties();
            UpdateTracking();
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
