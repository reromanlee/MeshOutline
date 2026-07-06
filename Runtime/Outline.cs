using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace reromanlee.MeshOutline
{
    /// <summary>
    /// Bakes a prebaked outline for the <see cref="MeshFilter"/> on this GameObject.
    ///
    /// A hidden child GameObject ("Outline (generated)") is created in the editor with a copy of
    /// the source mesh. Smooth (position-averaged) normals are baked into UV channel 3, which the
    /// outline fill shader uses to extrude a crack-free silhouette. Because everything is baked at
    /// edit time and serialized into the scene, the component has zero per-frame runtime cost:
    /// no Update loop, no post-processing, no render textures, no command buffers.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [DisallowMultipleComponent]
    [ExecuteAlways] // Needed only so OnDestroy cleans up the generated object in edit mode. No per-frame callbacks are used.
    public class Outline : MonoBehaviour
    {
        private const string GeneratedName = "Outline (generated)";

        /// <summary>UV channel (TEXCOORD3) the smooth normals are baked into. Must match the fill shader.</summary>
        private const int SmoothNormalsUVChannel = 3;

        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

        [Header("Materials")]
        [SerializeField, Tooltip("Material for the mask pass (writes stencil, no color).")]
        private Material outlineMaskMaterial;

        [SerializeField, Tooltip("Material for the fill pass (extrudes along baked smooth normals and draws the outline color).")]
        private Material outlineFillMaterial;

        [SerializeField, Tooltip("Create per-object instances of the outline materials so this outline can have its own color and width. When disabled, the shared material assets define the look and Outline Color/Width below are read-only (the shared assets are never modified by this component).")]
        private bool useMaterialInstances;

        [Header("Appearance")]
        [SerializeField, Tooltip("Synced to the fill material instance's _OutlineColor. Editable only when Use Material Instances is enabled; otherwise it mirrors the shared material.")]
        private Color outlineColor = Color.white;

        [SerializeField, Min(0f), Tooltip("Synced to the outline material instances' _OutlineWidth. Editable only when Use Material Instances is enabled; otherwise it mirrors the shared material.")]
        private float outlineWidth = 2f;

        [Header("Behaviour")]
        [SerializeField, Tooltip("When toggling IsVisible on this outline, also toggle the outlines of all child objects.")]
        private bool syncChildOutlines;

        [SerializeField, Tooltip("Hide the generated outline object in the Hierarchy window. It is still rendered, saved with the scene and pickable in the Scene view.")]
        private bool hideGeneratedObjectInHierarchy = true;

        // Baked state. Serialized (but hidden) so the outline survives scene reloads and builds
        // without any runtime work.
        [SerializeField, HideInInspector] private MeshFilter meshFilter;
        [SerializeField, HideInInspector] private GameObject outlineGameObject;
        [SerializeField, HideInInspector] private MeshFilter outlineMeshFilter;
        [SerializeField, HideInInspector] private MeshRenderer outlineMeshRenderer;
        [SerializeField, HideInInspector] private Mesh generatedMesh;
        [SerializeField, HideInInspector] private Mesh bakedSourceMesh;
        [SerializeField, HideInInspector] private Material maskMaterialInstance;
        [SerializeField, HideInInspector] private Material fillMaterialInstance;

        /// <summary>Whether an outline has been created for this object.</summary>
        public bool IsCreated => outlineGameObject != null;

        /// <summary>The generated child GameObject that renders the outline, or null if not created.</summary>
        public GameObject GeneratedGameObject => outlineGameObject;

        /// <summary>
        /// True when the outline was baked from a different mesh than the one currently assigned
        /// to the MeshFilter, i.e. the bake is out of date and <see cref="Recalculate()"/> should run.
        /// </summary>
        public bool IsBakeStale
        {
            get
            {
                if (!IsCreated) return false;
                if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
                return meshFilter != null && meshFilter.sharedMesh != bakedSourceMesh;
            }
        }

        /// <summary>
        /// Shows or hides the outline. When <see cref="SyncChildOutlines"/> is enabled, setting this
        /// also shows/hides the outlines of all child objects.
        /// </summary>
        public bool IsVisible
        {
            get => outlineGameObject != null && outlineGameObject.activeSelf;
            set
            {
                if (outlineGameObject != null)
                {
                    outlineGameObject.SetActive(value);
                }
                if (syncChildOutlines)
                {
                    // Note: allocates; only runs when visibility is toggled, never per frame.
                    foreach (Outline child in GetComponentsInChildren<Outline>(includeInactive: true))
                    {
                        if (child == this || child.outlineGameObject == null) continue;
                        child.outlineGameObject.SetActive(value);
                    }
                }
            }
        }

        /// <summary>
        /// Outline color. With <see cref="UseMaterialInstances"/> enabled this is per-object and
        /// written to the fill material instance. With it disabled, the shared material asset is
        /// the source of truth: the getter reads from it and the setter has no visual effect
        /// (shared assets are never modified by this component).
        /// </summary>
        public Color OutlineColor
        {
            get
            {
                if (!useMaterialInstances && outlineFillMaterial != null && outlineFillMaterial.HasProperty(OutlineColorId))
                {
                    return outlineFillMaterial.GetColor(OutlineColorId);
                }
                return outlineColor;
            }
            set
            {
                outlineColor = value;
                if (!useMaterialInstances) { WarnSharedMaterialsAreReadOnly(); return; }
                ApplyOutlineProperties();
            }
        }

        /// <summary>
        /// Outline width. With <see cref="UseMaterialInstances"/> enabled this is per-object and
        /// written to the outline material instances. With it disabled, the shared material asset
        /// is the source of truth: the getter reads from it and the setter has no visual effect
        /// (shared assets are never modified by this component).
        /// </summary>
        public float OutlineWidth
        {
            get
            {
                if (!useMaterialInstances && outlineFillMaterial != null && outlineFillMaterial.HasProperty(OutlineWidthId))
                {
                    return outlineFillMaterial.GetFloat(OutlineWidthId);
                }
                return outlineWidth;
            }
            set
            {
                outlineWidth = Mathf.Max(0f, value);
                if (!useMaterialInstances) { WarnSharedMaterialsAreReadOnly(); return; }
                ApplyOutlineProperties();
            }
        }

        /// <summary>
        /// When true, this outline renders with its own material instances so color/width can be
        /// set per object. When false, the shared material assets are used as-is (best for
        /// batching) and are treated as read-only: <see cref="OutlineColor"/> and
        /// <see cref="OutlineWidth"/> mirror them but never modify them.
        /// </summary>
        public bool UseMaterialInstances
        {
            get => useMaterialInstances;
            set
            {
                if (useMaterialInstances == value) return;
                // When enabling, seed the per-object values from the shared materials so the
                // freshly created instances start out looking identical.
                if (value) SyncPropertiesFromMaterials();
                useMaterialInstances = value;
                ApplyMaterials();
            }
        }

        /// <summary>When true, <see cref="IsVisible"/> also toggles the outlines of all children.</summary>
        public bool SyncChildOutlines
        {
            get => syncChildOutlines;
            set => syncChildOutlines = value;
        }

        /// <summary>Hide the generated outline object in the Hierarchy window (still saved and pickable in Scene view).</summary>
        public bool HideGeneratedObjectInHierarchy
        {
            get => hideGeneratedObjectInHierarchy;
            set { hideGeneratedObjectInHierarchy = value; ApplyHideFlags(); }
        }

        /// <summary>Creates the outline using the materials assigned in the inspector.</summary>
        public void Create() => Create(outlineMaskMaterial, outlineFillMaterial);

        /// <summary>
        /// Creates the outline child object and bakes the outline mesh. If an outline already
        /// exists, it is recalculated in place instead (no duplicate objects, no leaked meshes).
        /// </summary>
        public void Create(Material outlineMask, Material outlineFill)
        {
            outlineMaskMaterial = outlineMask;
            outlineFillMaterial = outlineFill;

            if (!IsCreated)
            {
                outlineGameObject = new GameObject(GeneratedName);
                outlineGameObject.transform.SetParent(transform, worldPositionStays: false);
                outlineMeshFilter = outlineGameObject.AddComponent<MeshFilter>();
                outlineMeshRenderer = outlineGameObject.AddComponent<MeshRenderer>();

                // The outline is a purely visual overlay: exclude it from anything that costs performance.
                outlineMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outlineMeshRenderer.receiveShadows = false;
                outlineMeshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                outlineMeshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                outlineMeshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            }

            Bake();
            ApplyMaterials();
            ApplyHideFlags();
        }

        /// <summary>Rebakes the outline mesh using the materials assigned in the inspector.</summary>
        public void Recalculate() => Recalculate(outlineMaskMaterial, outlineFillMaterial);

        /// <summary>
        /// Rebakes the outline mesh from the current source mesh, reusing the existing generated
        /// mesh and child object. Creates the outline first if it does not exist yet.
        /// </summary>
        public void Recalculate(Material outlineMask, Material outlineFill)
        {
            // Create() already handles both paths without duplicating the bake logic.
            Create(outlineMask, outlineFill);
        }

        /// <summary>Destroys the generated outline object, mesh and material instances.</summary>
        public void Remove()
        {
            DestroyMaterialInstances();
            if (generatedMesh != null)
            {
                SafeDestroy(generatedMesh);
                generatedMesh = null;
            }
            if (outlineGameObject != null)
            {
                SafeDestroy(outlineGameObject);
            }
            outlineGameObject = null;
            outlineMeshFilter = null;
            outlineMeshRenderer = null;
            bakedSourceMesh = null;
        }

        /// <summary>
        /// Copies the source mesh into the (reused) generated mesh and bakes smooth normals into
        /// UV channel 3. Reusing <see cref="generatedMesh"/> avoids leaking a Mesh on every rebake.
        /// </summary>
        private void Bake()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }
            Mesh source = meshFilter != null ? meshFilter.sharedMesh : null;
            if (source == null)
            {
                Debug.LogWarning($"[{nameof(Outline)}] '{name}' has no mesh assigned to its MeshFilter; outline not baked.", this);
                return;
            }

            if (generatedMesh == null)
            {
                generatedMesh = new Mesh { name = GeneratedName };
            }
            else
            {
                generatedMesh.Clear();
            }

            // Support meshes with more than 65535 vertices.
            generatedMesh.indexFormat = source.indexFormat;

            Vector3[] vertices = source.vertices;
            generatedMesh.vertices = vertices;
            // Note: source.triangles concatenates all submeshes into one. The renderer is given
            // [mask, fill] materials for this single submesh, so Unity draws it twice — first the
            // stencil mask pass, then the extruded fill pass.
            generatedMesh.triangles = source.triangles;

            // Keep the authored normals instead of recalculating them (cheaper and more accurate).
            Vector3[] normals = source.normals;
            if (normals.Length == vertices.Length)
            {
                generatedMesh.normals = normals;
            }
            else
            {
                generatedMesh.RecalculateNormals();
                normals = generatedMesh.normals;
            }

            generatedMesh.SetUVs(SmoothNormalsUVChannel, CalculateSmoothNormals(vertices, normals));
            generatedMesh.RecalculateBounds();

            outlineMeshFilter.sharedMesh = generatedMesh;
            bakedSourceMesh = source;
        }

        /// <summary>
        /// Averages normals of vertices that share the same position, so hard edges (split
        /// vertices) don't produce cracks when the fill shader extrudes the silhouette.
        /// </summary>
        private static List<Vector3> CalculateSmoothNormals(Vector3[] vertices, Vector3[] normals)
        {
            var smoothNormals = new List<Vector3>(normals);

            // Group vertex indices by position. A Dictionary avoids the boxing/allocation overhead
            // of the previous LINQ GroupBy implementation.
            var groups = new Dictionary<Vector3, List<int>>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!groups.TryGetValue(vertices[i], out List<int> indices))
                {
                    indices = new List<int>(4);
                    groups.Add(vertices[i], indices);
                }
                indices.Add(i);
            }

            foreach (KeyValuePair<Vector3, List<int>> group in groups)
            {
                List<int> indices = group.Value;
                if (indices.Count == 1) continue;

                Vector3 smoothNormal = Vector3.zero;
                for (int i = 0; i < indices.Count; i++)
                {
                    smoothNormal += normals[indices[i]];
                }
                smoothNormal.Normalize();

                for (int i = 0; i < indices.Count; i++)
                {
                    smoothNormals[indices[i]] = smoothNormal;
                }
            }

            return smoothNormals;
        }

        /// <summary>
        /// Assigns [mask, fill] to the renderer, creating or destroying per-object material
        /// instances according to <see cref="useMaterialInstances"/>, then syncs color/width.
        /// </summary>
        private void ApplyMaterials()
        {
            if (outlineMeshRenderer == null) return;

            Material mask = outlineMaskMaterial;
            Material fill = outlineFillMaterial;

            if (useMaterialInstances)
            {
                if (maskMaterialInstance == null && outlineMaskMaterial != null)
                {
                    maskMaterialInstance = new Material(outlineMaskMaterial) { name = outlineMaskMaterial.name + " (instance)" };
                }
                if (fillMaterialInstance == null && outlineFillMaterial != null)
                {
                    fillMaterialInstance = new Material(outlineFillMaterial) { name = outlineFillMaterial.name + " (instance)" };
                }
                if (maskMaterialInstance != null) mask = maskMaterialInstance;
                if (fillMaterialInstance != null) fill = fillMaterialInstance;
            }
            else
            {
                DestroyMaterialInstances();
            }

            // sharedMaterials: never trigger Unity's implicit .materials instancing.
            outlineMeshRenderer.sharedMaterials = new[] { mask, fill };
            ApplyOutlineProperties();
        }

        /// <summary>
        /// Writes <see cref="outlineColor"/> and <see cref="outlineWidth"/> to the per-object
        /// material instances. With material instances disabled this is a no-op: the shared
        /// material assets define the look and are never modified by this component.
        /// </summary>
        private void ApplyOutlineProperties()
        {
            if (!useMaterialInstances) return;

            if (fillMaterialInstance != null)
            {
                if (fillMaterialInstance.HasProperty(OutlineColorId)) fillMaterialInstance.SetColor(OutlineColorId, outlineColor);
                if (fillMaterialInstance.HasProperty(OutlineWidthId)) fillMaterialInstance.SetFloat(OutlineWidthId, outlineWidth);
            }
            if (maskMaterialInstance != null && maskMaterialInstance.HasProperty(OutlineWidthId))
            {
                maskMaterialInstance.SetFloat(OutlineWidthId, outlineWidth);
            }
        }

        /// <summary>
        /// Reads the current color/width from the active fill material into the serialized fields,
        /// so the (read-only) inspector fields mirror the shared material when instances are off,
        /// and so freshly enabled instances start out identical to the shared look.
        /// </summary>
        public void SyncPropertiesFromMaterials()
        {
            Material fill = useMaterialInstances && fillMaterialInstance != null ? fillMaterialInstance : outlineFillMaterial;
            if (fill == null) return;
            if (fill.HasProperty(OutlineColorId)) outlineColor = fill.GetColor(OutlineColorId);
            if (fill.HasProperty(OutlineWidthId)) outlineWidth = fill.GetFloat(OutlineWidthId);
        }

        [System.NonSerialized] private bool warnedSharedMaterialsReadOnly;

        private void WarnSharedMaterialsAreReadOnly()
        {
            if (warnedSharedMaterialsReadOnly) return;
            warnedSharedMaterialsReadOnly = true;
            Debug.LogWarning(
                $"[{nameof(Outline)}] '{name}': OutlineColor/OutlineWidth were set while " +
                $"{nameof(UseMaterialInstances)} is disabled. Shared material assets are read-only, " +
                "so the change has no visual effect. Enable UseMaterialInstances for per-object " +
                "color/width, or edit the shared material asset directly.", this);
        }

        /// <summary>
        /// Hides the generated object in the Hierarchy window. HideFlags.HideInHierarchy does not
        /// include DontSave, so the object is still serialized into the scene, still rendered, and
        /// still pickable by clicking it in the Scene view.
        /// </summary>
        private void ApplyHideFlags()
        {
            if (outlineGameObject == null) return;
            HideFlags flags = hideGeneratedObjectInHierarchy ? HideFlags.HideInHierarchy : HideFlags.None;
            if (outlineGameObject.hideFlags == flags) return;
            outlineGameObject.hideFlags = flags;
#if UNITY_EDITOR
            EditorApplication.RepaintHierarchyWindow();
#endif
        }

        private void DestroyMaterialInstances()
        {
            if (maskMaterialInstance != null)
            {
                SafeDestroy(maskMaterialInstance);
                maskMaterialInstance = null;
            }
            if (fillMaterialInstance != null)
            {
                SafeDestroy(fillMaterialInstance);
                fillMaterialInstance = null;
            }
        }

        /// <summary>Destroy that is safe both at runtime and in edit mode (incl. from OnValidate).</summary>
        private static void SafeDestroy(Object obj)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Deferred: DestroyImmediate is not allowed from OnValidate/OnDestroy in edit mode.
                EditorApplication.delayCall += () =>
                {
                    if (obj != null) DestroyImmediate(obj);
                };
                return;
            }
#endif
            Destroy(obj);
        }

        private void OnDestroy()
        {
            // Clean up the generated object, mesh and material instances when the component is
            // removed (works in edit mode thanks to [ExecuteAlways]).
            Remove();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Called by the editor when the component is first added (or reset). Auto-assigns the
        /// default outline materials and auto-creates the outline — a MeshFilter is guaranteed by
        /// RequireComponent.
        /// </summary>
        private void Reset()
        {
            if (outlineMaskMaterial == null) outlineMaskMaterial = FindDefaultMaterial("OutlineMask");
            if (outlineFillMaterial == null) outlineFillMaterial = FindDefaultMaterial("OutlineFill");
            SyncPropertiesFromMaterials();

            if (outlineMaskMaterial == null || outlineFillMaterial == null) return;

            // Creating objects directly from Reset is not allowed; defer one editor tick.
            EditorApplication.delayCall += () =>
            {
                if (this == null || IsCreated) return;
                Create();
                EditorUtility.SetDirty(this);
            };
        }

        /// <summary>
        /// Keeps materials/hide flags in sync when inspector fields change, and schedules a rebake
        /// if the source mesh was swapped. (Mesh swaps on the MeshFilter itself are additionally
        /// caught globally by OutlineMeshChangeWatcher in the Editor assembly.)
        /// </summary>
        private void OnValidate()
        {
            if (!IsCreated) return;

            ApplyMaterials();
            // With instances off the shared materials are the source of truth; keep the
            // (read-only) inspector fields mirroring them.
            if (!useMaterialInstances) SyncPropertiesFromMaterials();
            ApplyHideFlags();

            if (IsBakeStale)
            {
                EditorApplication.delayCall += () =>
                {
                    if (this != null && IsBakeStale) Recalculate();
                };
            }
        }

        private static Material FindDefaultMaterial(string materialName)
        {
            // Known locations first (UPM package install, then Assets install)...
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                $"Packages/com.reromanlee.meshoutline/Runtime/Materials/{materialName}.mat");
            if (material != null) return material;

            material = AssetDatabase.LoadAssetAtPath<Material>(
                $"Assets/MeshOutline/Runtime/Materials/{materialName}.mat");
            if (material != null) return material;

            // ...then fall back to a project-wide search by name.
            foreach (string guid in AssetDatabase.FindAssets($"t:Material {materialName}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null && material.name == materialName) return material;
            }
            return null;
        }
#endif
    }
}
