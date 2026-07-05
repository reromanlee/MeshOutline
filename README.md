# Mesh Outline (Prebaked)

Highly efficient, crack-free outlines for Unity meshes with **zero per-frame CPU cost**. Everything expensive happens once, in the editor: the outline is baked into a hidden child object and serialized with your scene, so at runtime the only cost is the GPU drawing two extra lightweight passes.

No post-processing. No render textures. No command buffers. No `Update()` loops. No `OnWillRenderObject` callbacks.

## How it works

Most outline solutions pay a recurring cost: fullscreen post-processing effects re-render silhouettes into a texture and blur/composite every frame, and popular component-based solutions (e.g. Quick Outline) compute smooth normals at runtime and re-assign materials whenever the object becomes visible. This package moves all of that work to edit time:

1. When you add the `Outline` component (or press **Create**), a hidden child object `Outline (generated)` is created with a copy of the source mesh.
2. **Smooth normals** — the normals of all vertices sharing the same position, averaged — are baked into **UV channel 3 (TEXCOORD3)** of that mesh. This is what makes the outline crack-free on hard-edged (vertex-split) geometry.
3. The child's `MeshRenderer` gets two materials for its single combined submesh, so the mesh is drawn twice:
   - **Mask pass** (`OutlineMask` material): writes the object's silhouette to the stencil buffer, drawing no color.
   - **Fill pass** (`OutlineFill` material): extrudes vertices along the baked smooth normals by `_OutlineWidth` and draws `_OutlineColor` wherever the stencil test passes (i.e. only outside the original silhouette).
4. The generated object, its mesh (including the baked UV4 data) and the material setup are all **serialized into the scene**. On load — in editor and in builds — the outline is simply *there*. Nothing is computed.

## Performance

- **Runtime CPU:** effectively zero. The component defines no per-frame callbacks. The only runtime code paths are the ones you call yourself (`IsVisible`, `OutlineColor`, etc.), and toggling visibility is a single `GameObject.SetActive`.
- **Runtime GPU:** two additional draw calls per outlined object (mask + fill), both with trivial shaders. The generated renderer has shadows, light probes, and reflection probes disabled, so it never participates in shadow passes or probe updates.
- **Batching:** with **Use Material Instances** disabled (the default), all outlines share the same two material assets, so they batch/SRP-batch together. Enable instances only for objects that genuinely need a unique color or width.
- **Memory:** one extra mesh per outlined object (positions, indices, normals, UV4). No textures, no render targets.
- **Baking cost:** smooth-normal calculation is O(n) over vertices using a position-keyed dictionary — and it runs in the editor, never in your game.

## Quick start

1. Put this package in your project (e.g. `Packages/com.reromanlee.meshoutline` or anywhere under `Assets`).
2. The `OutlineMask` / `OutlineFill` materials in `Runtime/Materials` are found automatically (known package/Assets paths are checked first, then a project-wide search by name).
3. Select any GameObject with a `MeshFilter` and add the **Outline** component. That's it — the materials are found automatically and the outline is baked immediately.
4. Save the scene. The baked outline is saved with it.

If the default materials aren't found (or you use differently named ones), assign them in the inspector and press **Create**.

## Inspector reference

| Field | What it does |
|---|---|
| **Outline Mask Material** | Material for the stencil mask pass. |
| **Outline Fill Material** | Material for the extruded fill pass. |
| **Use Material Instances** | Creates per-object copies of both materials so *this* outline can have its own color/width. Off by default — shared materials batch better. |
| **Outline Color / Width** | Synced to the fill material's `_OutlineColor` / the materials' `_OutlineWidth`. With instances **off**, editing these writes to the shared material assets and therefore changes **every** outline using them (by design — the fields exist so you never have to hunt down the material). With instances **on**, only this object changes. |
| **Sync Child Outlines** | When on, setting `IsVisible` also shows/hides the outlines of all child objects — handy for outlining a whole prop hierarchy as one unit. |
| **Hide Generated Object In Hierarchy** | Hides `Outline (generated)` in the Hierarchy window to keep it tidy. The object is still rendered, still saved into the scene, and still pickable in the Scene view. |

The inspector also shows the bake status and **Create / Recalculate / Remove** buttons (with Undo support, multi-object editing works too).

## Automatic rebaking

You should never need to press **Recalculate** manually:

- Adding the component auto-creates the outline.
- Swapping the mesh on the `MeshFilter` is detected via the editor's `ObjectChangeEvents` and the outline is rebaked automatically — even if the inspector isn't open.
- If a bake ever goes stale anyway (e.g. after an external merge), the inspector shows a warning, and `Recalculate` fixes it. Rebaking **reuses** the existing generated mesh and child object, so repeated recalculation never leaks memory.

## Scripting API

```csharp
using reromanlee.MeshOutline;

var outline = GetComponent<Outline>();

outline.IsVisible = true;              // show/hide (cascades to children if SyncChildOutlines)
outline.OutlineColor = Color.red;      // synced to the fill material
outline.OutlineWidth = 4f;
outline.UseMaterialInstances = true;   // give this object its own material copies
outline.SyncChildOutlines = true;

outline.Create();                      // bake using the inspector-assigned materials
outline.Create(maskMat, fillMat);      // bake with explicit materials
outline.Recalculate();                 // rebake after the source mesh changed
outline.Remove();                      // destroy the generated object/mesh/instances

bool baked = outline.IsCreated;
bool stale = outline.IsBakeStale;      // true if the source mesh changed since baking
```

Typical selection-highlight usage — note this is the *only* work done at runtime:

```csharp
void OnHoverEnter() => outline.IsVisible = true;
void OnHoverExit()  => outline.IsVisible = false;
```

Tip: for the cheapest possible runtime, bake outlines with `IsVisible = false` in the editor and only flip visibility in game code. Prefer shared materials; if you need a handful of distinct colors (e.g. red = enemy, green = ally), consider making a few material assets rather than per-object instances, so each color still batches.

## Shaders

The package ships with `reromanlee/OutlineMask` and `reromanlee/OutlineFill` (in `Runtime/Shaders/`). Each contains two SubShaders — Unity automatically picks the right one:

- **URP SubShader** (HLSL): all program properties live in a `UnityPerMaterial` CBUFFER, making the shaders **SRP Batcher compatible**, so many outlines render in a single batched sequence.
- **Built-in RP SubShader** (CG): the classic path, now with `#pragma multi_compile_instancing` so **GPU instancing** and **single-pass instanced VR** actually work (the instancing/stereo macros previously had no effect without it).

Shader details worth knowing:

- **Smooth normals** are read from `TEXCOORD3` (UV channel 3); the fill falls back to the regular normal for vertices where no smooth normal was baked.
- **`_OutlineColor` / `_OutlineWidth`** are the properties the component syncs. All writes are guarded with `HasProperty`, so custom shaders missing a property are simply skipped. Width is screen-space constant: scaled by view depth in perspective, and by the camera's orthographic size in ortho views (so outlines no longer thin out with distance under orthographic cameras).
- **`_StencilRef`** (default `1`, on both materials — keep them matching) lets you move the outline to a different stencil bit if something else in your project (URP decals, other assets) also uses stencil `1`.
- **`_ZTest`** defaults to `Disabled`, i.e. the outline shows through geometry. Set it to `LessEqual` on both materials if you want outlines occluded by other objects.
- The **mask** writes no color (`ColorMask 0`) and no depth — its GPU cost is essentially just stencil writes. The mask is now a programmable pass in both pipelines (fixed-function passes aren't supported by SRPs and break instancing/VR).
- The **fill** keeps `"DisableBatching"="True"` deliberately: dynamic/static batching pre-transform vertices and would break the per-object extrusion math. This does not affect the SRP Batcher or GPU instancing.

If your own shaders use different property names, change the `Shader.PropertyToID` constants at the top of `Outline.cs`.

## Requirements & compatibility

- **Unity 2021.3 LTS or newer** (the automatic rebake watcher uses `ObjectChangeEvents`, added in 2021.2).
- **Built-in RP and URP** are both supported out of the box via per-pipeline SubShaders (SRP Batcher compatible under URP). HDRP is not currently supported (it would need its own SubShader).
- Meshes with more than 65,535 vertices are supported (the baked mesh inherits the source index format).
- Multi-submesh meshes are supported; submeshes are combined into one for the outline (an outline doesn't care about per-submesh materials).

## Limitations

- **`MeshFilter` only.** `SkinnedMeshRenderer` isn't supported — the prebaked approach relies on a static mesh copy. Skinned support would require the outline mesh to be skinned too (planned).
- **Prefab assets:** bake outlines on scene instances or in prefab mode; the generated child is an ordinary GameObject and follows normal prefab rules.
- **Meshes edited in place:** swapping the mesh reference triggers an automatic rebake; mutating the same `Mesh` asset's vertices without changing the reference does not (press Recalculate or call `Recalculate()`).
- **Exact-position smoothing:** smooth normals group vertices by *identical* positions. Vertices that are merely very close (e.g. from lossy import welding) aren't merged.

## Project layout

```
com.reromanlee.meshoutline/
├── package.json
├── README.md
├── Runtime/
│   ├── Outline.cs                          # the component (runtime + light editor hooks)
│   ├── Shaders/
│   │   ├── OutlineFill.shader              # URP + Built-in SubShaders
│   │   └── OutlineMask.shader              # URP + Built-in SubShaders
│   ├── Materials/                          # OutlineMask.mat / OutlineFill.mat live here
│   └── reromanlee.MeshOutline.asmdef
└── Editor/
    ├── OutlineEditor.cs                    # custom inspector: status, Show/Hide, Create/Recalculate/Remove + Undo
    ├── OutlineMeshChangeWatcher.cs         # global auto-rebake on mesh changes
    └── reromanlee.MeshOutline.Editor.asmdef
```
