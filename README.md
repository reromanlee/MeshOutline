# MeshOutline

**Zero-per-frame-cost mesh outlines for Unity 6.** Bake once in the editor, ship it, forget it — no `Update()` loops, no post-processing, no render textures, no command buffers, no camera setup.

MeshOutline draws crisp, constant-width silhouette outlines around any `MeshFilter`-based object using a classic two-pass stencil technique — with the expensive part (smooth-normal generation) done **at edit time** and serialized straight into your scene.

![License: MIT](https://img.shields.io/badge/License-MIT-green.svg) ![Unity](https://img.shields.io/badge/Unity-6.0%2B-black.svg) ![Pipelines](https://img.shields.io/badge/Render%20Pipeline-URP%20%7C%20Built--in-blue.svg)

---

## Why MeshOutline?

Most outline solutions pay for their looks every single frame: fullscreen post-processing passes, per-frame normal baking on `Awake`/`OnEnable`, render-texture blits, or `Update()`-driven material juggling. MeshOutline takes a different route:

- **Zero CPU cost per frame.** The component has no `Update`, `LateUpdate`, or per-frame callbacks of any kind. Once baked, the outline is just a regular `MeshRenderer` on a hidden child object.
- **Zero runtime baking.** Smooth (position-averaged) normals are computed in the editor and baked into UV channel 3 of a generated mesh, then serialized with the scene. Your players never pay for it — not even on scene load.
- **Minimal GPU cost.** Two lightweight unlit passes per outlined object (a color-less stencil mask + an extruded fill). No fullscreen passes, no extra render targets, no depth-normals prepass. Shadow casting, light probes, reflection probes, and motion vectors are all disabled on the generated renderer.
- **Multi-object correct, automatically.** Outlines never clip or swallow each other, and occlusion between outlined objects is resolved purely by depth — no sorting layers, no render-queue fiddling, no per-object stencil references, no priorities to manage. It just works with 2 or 200 outlined objects.
- **Crack-free silhouettes.** Hard edges split vertices, and naive normal extrusion tears at those seams. The baked smooth normals average all normals sharing a position, so cubes, low-poly art, and CAD-style meshes outline cleanly.
- **Editor-friendly.** Outlines auto-create when you add the component, auto-rebake when you swap the mesh on the `MeshFilter` (even with the inspector closed), and every button is fully Undo-aware.
- **URP and Built-in RP** support in the same shaders, SRP Batcher compatible, GPU instancing and single-pass instanced VR ready.

## How it works

Adding the `Outline` component creates a hidden child (`Outline (generated)`) with a copy of your mesh. That mesh gets smooth normals baked into `TEXCOORD3` and two materials:

1. **Mask** — writes the object's silhouette into the stencil buffer (no color, no depth writes).
2. **Fill** — extrudes vertices along the baked smooth normals in view space (constant screen-space width, works in perspective *and* orthographic) and draws the outline color only *outside* the silhouette. It then erases the object's own stencil footprint, leaving the buffer clean for the next outlined object.

Because the mask and fill share one render queue on one renderer, each object renders as a self-contained mask→fill pair, back to front. That per-object isolation is what makes multiple overlapping outlines behave correctly with zero configuration.

Everything above is baked and serialized at edit time. At runtime it's just meshes and materials.

## Requirements

- **Unity 6.0 or newer**
- URP or Built-in Render Pipeline (the shaders contain a SubShader for each; Unity picks automatically)
- A stencil buffer (any standard 24/32-bit depth-stencil setup — the default)

## Installation

### Via Unity Package Manager (recommended)

1. Open **Window → Package Manager**.
2. Click **＋** → **Install package from git URL…**
3. Paste:

```
https://github.com/reromanlee/MeshOutline.git
```

### Via manifest.json

Add this line to `Packages/manifest.json` in your project:

```json
{
  "dependencies": {
    "com.reromanlee.meshoutline": "https://github.com/reromanlee/MeshOutline.git"
  }
}
```

## Quick start

1. Select any GameObject with a `MeshFilter` + `MeshRenderer`.
2. **Add Component → Outline.**

That's it. The default mask/fill materials are auto-assigned and the outline is created immediately. Swap the mesh later and the outline rebakes itself automatically.

### Inspector overview

| Setting | What it does |
| --- | --- |
| **Outline Mask / Fill Material** | The two materials used by the outline. Defaults ship with the package. |
| **Use Material Instances** | Creates per-object copies of the materials so *this* outline can have its own color and width. When **off**, the shared material assets define the look and the color/width fields are shown read-only — the shared (package) assets are never modified. |
| **Outline Color / Width** | Per-object appearance (requires *Use Material Instances*). Width is in constant screen-space units. |
| **Sync Child Outlines** | Toggling visibility also toggles the outlines of all children. |
| **Hide Generated Object In Hierarchy** | Keeps the generated child out of your Hierarchy window (it's still saved, rendered, and pickable in the Scene view). |
| **Show / Hide, Create / Recalculate, Remove** | One-click, Undo-aware control over the baked outline. |

### Scripting

```csharp
using reromanlee.MeshOutline;

var outline = gameObject.AddComponent<Outline>();
outline.Create(maskMaterial, fillMaterial);   // bake (in builds, assign materials yourself)

outline.UseMaterialInstances = true;          // enable per-object appearance
outline.OutlineColor = Color.cyan;
outline.OutlineWidth = 4f;

outline.IsVisible = false;                    // toggle without any rebaking cost
outline.Remove();                             // destroy the generated object, mesh and instances
```

Common pattern — bake in the editor, toggle at runtime:

```csharp
// Selection highlight: zero allocation, zero baking, just a SetActive under the hood.
void OnHoverEnter() => outline.IsVisible = true;
void OnHoverExit()  => outline.IsVisible = false;
```

## Occlusion modes (ZTest)

Both materials expose a **ZTest** property:

- **LessEqual** *(default)* — outlines are occluded by scene geometry like any normal object. Overlapping outlined objects resolve front-to-back exactly as you'd expect.
- **Always** — X-ray mode: the outline shows through walls. Great for "objective behind cover" markers.

## Performance notes

- **CPU:** nothing per frame. The component only does work when you bake, toggle visibility, or change properties.
- **GPU:** 2 unlit draw calls per outlined object (mask + fill). The shaders are SRP Batcher compatible; with shared materials (the default), all outlines batch their state efficiently.
- **Memory:** one baked mesh per outlined object (a copy of the source with an extra UV channel), serialized with the scene.
- **Batching:** dynamic/static batching is intentionally disabled for the outline passes (the extrusion math needs per-object transforms, and per-object pass ordering must be preserved). SRP Batcher and GPU instancing are unaffected.
- Prefer **shared materials** (Use Material Instances off) when many objects share one look; enable instances only where you need a unique color/width.

## Troubleshooting

- **Outline looks torn/cracked at hard edges** — the bake is stale; press **Recalculate** (normally automatic when the mesh changes).
- **No outline after adding the component in a build** — baking is editor-only by design. Bake in the editor and ship the scene, or call `Create(mask, fill)` yourself at runtime with materials you assign.
- **Changed color/width but nothing happens** — enable **Use Material Instances**. Without it the shared material assets are read-only by design (so the package's default materials are never modified), and a console warning points this out.
- **Skinned meshes** — not supported; the component requires a `MeshFilter`.

## License

[MIT](LICENSE) — free for personal and commercial use.

## Links

- Repository: <https://github.com/reromanlee/MeshOutline>
- Issues & feature requests: <https://github.com/reromanlee/MeshOutline/issues>
