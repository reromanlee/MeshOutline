# MeshOutline

**Constant-width outlines for any mesh, skinned mesh or whole hierarchy in Unity, with zero per-frame cost.** Add one component, toggle it with `enabled`. No `Update()` loop, no post-processing, no render textures, no camera setup.

![License: MIT](https://img.shields.io/badge/License-MIT-green.svg) ![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black.svg) ![Pipelines](https://img.shields.io/badge/Render%20Pipeline-URP%20%7C%20Built--in-blue.svg)

![Mesh outline showcase](.github/readme-outline-showcase.gif)

## Why MeshOutline?

- **Zero CPU cost per frame.** The outline is drawn by ordinary renderers built once when the object loads. Showing and hiding it is just `outline.enabled`: no allocation, no rebuilding.
- **No runtime baking.** Outline meshes are baked in the editor into a shared cache: one per source mesh, however many objects, scenes and prefabs use it.
- **One silhouette per object.** Put the component on a character or prop root and every mesh under it is outlined as one shape, with no lines between parts.
- **Correct overlaps, automatically.** Nearer outlines draw over farther objects and farther ones hide behind nearer ones, with no sorting layers or priorities to manage.
- **Skinned meshes and LOD groups.** Skinned outlines deform with their bones and blend shapes; outlines switch and cull with their LOD level at runtime.
- **Width the way you want it.** By default it's in pixels at 1080p: the same thickness at any distance, and the same share of the screen at any resolution or field of view. It can also be exact pixels, or scale with distance like part of the object.
- **Works with the editor, not against it.** Duplicate, undo, prefabs, prefab mode, play mode and domain reloads all just work. Change or reimport a mesh and its outline rebakes on its own.
- **URP and Built-in**, forward and deferred, in the same shaders. SRP Batcher compatible, GPU instancing and single-pass instanced VR ready.

## Installation

Open **Window > Package Manager**, click **+ > Install package from git URL...** and paste:

```
https://github.com/reromanlee/MeshOutline.git?path=/UnityPackage
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.reromanlee.meshoutline": "https://github.com/reromanlee/MeshOutline.git?path=/UnityPackage"
  }
}
```

To pin a release, add its tag after the path: `...MeshOutline.git?path=/UnityPackage#<version>`. Releases 2.0.0 and older were published from the repository root, so pin those without the path: `...MeshOutline.git#2.0.0`.

Requires Unity 2022.3 or newer and a stencil buffer (the default 24/32-bit depth-stencil setup).

## Quick start

1. Select any GameObject with a mesh, or the root of a model.
2. **Add Component > Rendering > Object Outline.**

That's it. To show and hide it from a script:

```csharp
using reromanlee.MeshOutline;

[SerializeField] private ObjectOutline outline;

void OnHoverEnter() => outline.enabled = true;
void OnHoverExit() => outline.enabled = false;
```

Want a complete example? Import the **Hover & Select** sample from the package's page in the Package Manager.

## The inspector

| Setting | What it does |
| --- | --- |
| **Color** | Outline color. HDR colors glow when bloom is enabled. |
| **Width Mode** | How Width is measured. **Pixels at 1080p** (default): the same thickness at any distance, and the same share of the screen at any resolution (8 at 1080p is 16 pixels at 4K). **Exact Pixels**: the same number of pixels at any distance and resolution. **Scales with Distance**: like part of the object, thinner farther away and thicker up close. |
| **Width** | Outline width in pixels: at 1080p, or exact pixels with Exact Pixels. With Scales with Distance, it's the width when the object is at the Reference Distance. |
| **Reference Distance** | *Scales with Distance only.* The distance (in meters) at which the outline is Width pixels thick, at 1080p with a 60° field of view. |
| **Occlusion** | **Normal**: hidden behind other geometry, like any object. **X-Ray**: always visible, even through walls. |
| **Include Children** | Also outline child renderers, merged into one silhouette. A child with its own Object Outline is outlined separately. |
| **Parts** | Every renderer the outline covers, with its bake status. Untick one to leave it out (a muzzle flash, a shadow blob...). Click a name to highlight it. |
| **Advanced > Custom Fill Material** | A material for the fill pass, e.g. an animated outline. Base its shader on the built-in fill shader: it must use `_StencilRef` and `_ZTest`, and it's given `_OutlineColor`, `_OutlineWidth` and `_OutlineWidthMode`. |
| **Advanced > Track Source Every Frame** | Copy each renderer's enabled state, layer and blend-shape weights to the outline every frame. See [Keeping in sync](#keeping-in-sync). |
| **Rebake** | Rebake this outline's meshes, even if they look up to date. You shouldn't normally need it. |

## Scripting

```csharp
var outline = gameObject.AddComponent<ObjectOutline>();

outline.Color = Color.cyan;          // applies immediately
outline.Width = 6f;                  // pixels at 1080p by default
outline.Occlusion = OutlineOcclusion.XRay;

outline.WidthMode = OutlineWidthMode.ScalesWithDistance;
outline.ReferenceDistance = 10f;     // 6 px at 10 m, 3 px at 20 m, 12 px at 5 m
outline.enabled = false;             // hide; no allocation

outline.IncludeChildren = false;     // only this object's own renderer
outline.SetExcluded(muzzleFlash, true);
IReadOnlyList<Renderer> parts = outline.Parts;

// After changing the hierarchy at runtime (meshes added, removed or swapped),
// or to copy renderer state and blend-shape weights on demand:
outline.Refresh();
```

`AddComponent` at runtime works too: outline meshes are then baked on the spot, which needs the meshes' **Read/Write** import setting enabled. For anything you know about ahead of time, add the component in the editor (for example on the prefab): it's baked in advance and costs nothing at runtime.

## How it works

For each renderer it covers, an Object Outline builds a hidden child renderer that draws an **outline mesh**: a copy of the source mesh whose normals are *smoothed* (all normals sharing a position are averaged, so hard edges don't tear open). It's drawn twice:

1. **Mask**: stamps the object's visible silhouette into the stencil buffer, drawing no color.
2. **Fill**: pushes the outline mesh outward along its smoothed normals in view space and draws the outline color only *outside* the silhouette.

Every mask renders before any fill, and each enabled outline gets its own stencil reference. So a fill skips only its own silhouette, and overlapping outlines resolve per pixel by depth.

The hidden renderers and their materials are never saved. They're rebuilt when the object loads, which is why duplicating, undoing, prefabs and play mode can't leave stale or shared copies behind. What *is* saved is a reference to each outline mesh.

## Baked outline meshes

Outline meshes are baked into **Assets/MeshOutline Data/Baked Meshes/**, one asset per source mesh, shared by every scene and prefab. **Commit this folder** to version control: bakes are deterministic, so they don't churn between machines.

- A reimported model rebakes its outline meshes in place, so every scene and prefab picks up the change.
- Meshes that aren't assets (ProBuilder, procedural meshes) are baked into the scene instead, and rebaked when edited.
- **Edit > Project Settings > Mesh Outline** lets you move the folder (references stay valid), **Rebake All**, and **Clean Up Unused** bakes that no scene, prefab or loaded outline uses. The same commands are under **Tools > Mesh Outline**.

## Skinned meshes, blend shapes and LOD groups

- **SkinnedMeshRenderers** get a skinned outline that shares the source's bones, so it deforms identically. That's a second GPU skinning pass per outlined skinned mesh.
- **Blend shapes** (morph targets) are baked in. Their weights are copied when the outline is enabled and on `Refresh()`, or every frame with **Track Source Every Frame** (e.g. for facial animation).
- **LOD groups**: at runtime each outline part joins its source's LOD level, so it switches and culls with the model. In edit mode only LOD0 is outlined.

## Keeping in sync

To cost nothing per frame, an outline copies each source renderer's **enabled state, layer, rendering layers and blend-shape weights** only when it's enabled and when you call `Refresh()`. Moving, rotating, animating bones and activating or deactivating GameObjects never need either: the outline follows automatically.

If something changes those four every frame (facial animation, or toggling `renderer.enabled` instead of `SetActive`), either call `Refresh()` after the change or turn on **Track Source Every Frame**, which copies them right before each frame renders.

## Performance

- **CPU:** nothing per frame, unless Track Source Every Frame is on. Building an outline (when its object loads) creates one hidden GameObject per outlined renderer and two small materials.
- **GPU:** two unlit draw calls per outlined renderer (mask and fill). SRP Batcher compatible.
- **Memory:** one outline mesh per source mesh, shared everywhere.
- **Stencil:** up to 240 outlines can be enabled at once, each with its own stencil reference. Beyond that some share one, which only matters where they overlap on screen. The references never use the stencil bits URP's deferred path writes.

## Limitations

- HDRP, sprites and UI aren't supported.
- Outlines come from the silhouette's normals: a flat quad facing the camera has no outline, and very hard-edged meshes get a slightly thinner outline along straight edges.
- Cloth and meshes deformed on the CPU by scripts aren't followed.
- Outlines render in the transparent queue and don't write motion vectors, so temporal anti-aliasing can smear fast-moving outlines slightly.
- Rendering into your own RenderTexture needs a depth buffer with stencil (24 bits or more).

## Troubleshooting

- **"Can't be outlined at runtime: mesh ... isn't readable"**: the outline was added at runtime to a mesh without Read/Write. Enable Read/Write in the mesh's import settings, or add the outline in the editor.
- **An outline doesn't follow `renderer.enabled` or blend shapes**: see [Keeping in sync](#keeping-in-sync).
- **Changing the Game view's resolution doesn't change the outline**: that's Pixels at 1080p working. The Game view scales the image to fit the window, and the outline keeps the same share of the screen at every resolution, so it looks the same. Use Exact Pixels for a fixed pixel count. Neither screen mode changes with distance; Scales with Distance does.
- **A part shows "baked · LOD 1+" but isn't visible in edit mode**: edit mode only outlines LOD0; the other levels show at runtime.
- **An outline looks wrong after changing a mesh outside Unity's import pipeline**: press **Rebake** on the outline, or **Rebake All** in Project Settings.

## Upgrading from 1.x

2.0.0 is a breaking release; [CHANGELOG.md](CHANGELOG.md) lists every change. In short:

- The component is now `ObjectOutline` (existing components keep working; update your scripts). Show and hide it with `enabled`.
- Color and width are plain properties (`Color`, `Width`). Width is in pixels at 1080p by default; `WidthMode` also offers exact pixels and a width that scales with distance.
- One component covers the whole hierarchy, so `SyncChildOutlines` is gone.
- Opening a scene saved with 1.0.0 removes its hidden "Outline (generated)" children and rebakes; save the scene afterwards.

## License

[MIT](LICENSE.md): free for personal and commercial use.

## Links

- Repository: <https://github.com/reromanlee/MeshOutline>
- Issues & feature requests: <https://github.com/reromanlee/MeshOutline/issues>
