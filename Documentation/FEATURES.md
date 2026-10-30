### Features

A detailed look at what MeshOutline does and the guarantees behind it. For setup and code, see [USAGE.md](USAGE.md).

### How it works

For each renderer it covers, an Object Outline builds a hidden child renderer that draws an **outline mesh**: a copy of the source mesh whose normals are *smoothed*, so that every vertex sharing a position gets the same averaged normal and hard edges don't tear open. It's drawn twice:

1. **Mask** stamps the object's visible silhouette into the stencil buffer, drawing no color.
2. **Fill** pushes the outline mesh outward along its smoothed normals in view space and draws the outline color only *outside* the silhouette.

Every mask renders before any fill, and each enabled outline gets its own stencil reference, so a fill skips only its own silhouette. Nearer outlines draw over farther objects and farther ones hide behind nearer ones, per pixel, with no sorting layers or priorities to manage.

The hidden renderers and their materials are never saved. They're rebuilt when the object loads, which is why duplicating, undoing, prefabs and play mode can't leave stale or shared copies behind. What *is* saved is a reference to each baked outline mesh.

### The Object Outline component

| Setting | What it does |
| --- | --- |
| **Color** | Outline color. HDR colors glow when bloom is enabled. |
| **Width Mode** | How Width is measured, see [Width modes](#width-modes). |
| **Width** | Outline width in pixels: at 1080p by default, or exact pixels with Exact Pixels. With Scales with Distance, it's the width at the Reference Distance. |
| **Reference Distance** | *Scales with Distance only.* The distance in meters at which the outline is Width pixels thick, at 1080p with a 60° field of view. |
| **Occlusion** | **Normal**: hidden behind other geometry, like any object. **X-Ray**: always visible, even through walls. |
| **Include Children** | Also outline child renderers, merged into one silhouette. A child with its own Object Outline is outlined separately. |
| **Parts** | Every renderer the outline covers, with its bake status. Untick one to leave it out (a muzzle flash, a shadow blob). Click a name to highlight it. |
| **Advanced ▸ Custom Fill Material** | A material for the fill pass, e.g. an animated outline. See [Custom fill materials](USAGE.md#custom-fill-materials). |
| **Advanced ▸ Track Source Every Frame** | Copy each renderer's enabled state, layer and blend-shape weights to the outline every frame. See [Keeping in sync](USAGE.md#keeping-in-sync). |
| **Rebake** | Rebake this outline's meshes, even if they look up to date. You shouldn't normally need it. |

Part statuses in the inspector:

| Status | Meaning |
| --- | --- |
| **baked** | Uses the shared outline mesh cache. |
| **baked (scene)** | The mesh isn't an asset (ProBuilder, procedural), so its outline mesh is stored in the scene and rebaked when the mesh changes. |
| **baked · LOD 1+** | Outlined at runtime whenever its LOD level shows. Edit mode only outlines LOD0. |
| **not in prefab** | On a prefab asset, a mesh that isn't an asset can't be stored. Scene instances bake it into their scene; spawned instances bake it at runtime. |
| **excluded** | Left out of the outline, but still baked so it can be included again at runtime for free. |
| **baking...** | Baked on the next editor update. |

### Width modes

1. **Pixels at 1080p** (default): the same thickness at any distance, and the same share of the screen at any resolution or field of view. 8 at 1080p is 16 pixels at 4K.
2. **Exact Pixels**: the same number of screen pixels at any distance and resolution.
3. **Scales with Distance**: a world-space thickness, like part of the object. Width pixels at the Reference Distance, thinner farther away and thicker up close.

The screen modes work in perspective and orthographic cameras. The fill's depth stays exactly that of the source surface, so occlusion between outlined objects stays correct at any distance.

### Hierarchies

1. **One silhouette per object.** Put the component on a character or prop root and every mesh under it is outlined as one shape, with no lines between parts. An outline can sit on an empty model root.
2. **Nested outlines are independent.** The nearest Object Outline above a renderer owns it, so a child with its own outline gets its own color, width and stencil reference.
3. **Inactive children are baked too**, so activating them later costs nothing.
4. **Excluded renderers are baked too**, so `SetExcluded(renderer, false)` at runtime is instant and needs no Read/Write.

### The baked mesh cache

Outline meshes are baked in the editor into **Assets/MeshOutline Data/Baked Meshes/**, one asset per source mesh, shared by every scene and prefab that uses it.

1. **Bakes are found by their source, not by file name.** Each entry records its source mesh, the bake format and a fingerprint of the source, so it can be moved, renamed or relocated with the whole folder and still be found.
2. **Reimported models rebake in place.** The entry keeps its GUID, so every scene and prefab picks up the change at once.
3. **Bakes are deterministic.** The same source always produces identical data, so entries don't churn between machines.
4. **Meshes that aren't assets** (ProBuilder, procedural) are baked into the scene instead, with a fingerprint that catches the mesh being edited in place.
5. **Maintenance** lives under **Project Settings ▸ Mesh Outline** and **Tools ▸ Mesh Outline**: move the folder (references stay valid), Rebake All, Bake All Prefabs and Clean Up Unused, which deletes bakes that no scene, prefab or loaded outline uses.

### Prefabs

1. **Outlines on prefabs work in every instance, with no overrides.** The prefab stores the references to its baked meshes.
2. **Prefab assets bake themselves.** An outline added to a prefab asset from the Project window is baked and saved as soon as the inspector shows it, like any inspector edit. Prefab Mode bakes as you edit.
3. **Read-only prefabs** (model prefabs and prefabs in immutable packages) are baked in each scene that uses them instead.
4. **Bake All Prefabs** bakes every prefab in the project at once, for projects whose prefabs are generated or edited by scripts.

### Builds

**No scene outline falls back to runtime baking in a build.** While each scene is built, any outline part without an up-to-date bake (set up by an editor script, a batch-mode tool or a bad merge) gets one: a valid cache entry is reused, and anything else is baked into the built scene only. No asset is written during the build, and the build log lists each part so it can be fixed at the source.

### Skinned meshes, blend shapes and LOD groups

1. **SkinnedMeshRenderers** get a skinned outline that shares the source's bones, so it deforms identically. Swapping the mesh or the skeleton (equipment, customization) re-binds the outline on `Refresh()`.
2. **Blend shapes** (morph targets) are baked in. Their weights are copied when the outline is enabled and on `Refresh()`, or every frame with Track Source Every Frame.
3. **LOD groups**: at runtime each outline part joins its source's LOD level, so it switches and culls with the model at no per-frame cost. In edit mode, where changing a saved LOD group would leave overrides behind, only LOD0 is outlined.

### Static batching

**Outlines survive static batching.** Static batching replaces a renderer's mesh with the whole batch's combined mesh, so outlines of static-batched renderers keep using the mesh they were baked from in the editor. An outline added at runtime to an already static-batched renderer can't be baked, and says so once.

### Stencil references

1. **Up to 240 outlines can be enabled at once**, each with its own stencil reference. Beyond that some share one, which only matters where they overlap on screen; a warning says when it happens.
2. **References never use the stencil bits URP's deferred path writes**, so outlines aren't clipped against lit geometry.
3. Outlines render in the transparent queue (masks at Transparent+100, fills at Transparent+110).

### Editor integration

1. **Duplicate, undo, prefabs, Prefab Mode, play mode and domain reloads just work.** A copy deletes the hidden parts it inherited and builds its own, with its own materials and stencil reference.
2. **Changes are picked up without an open inspector**: swapped meshes, added or removed renderers, re-parenting, undo and prefab updates.
3. **Clicking an outline in the Scene view** selects the outlined object, not the hidden part.
4. **Enter Play Mode Options** without a domain reload are supported, and one-time warnings are reported again in each play session.

### Robustness

1. **Invalid values never reach the GPU.** NaN or infinite widths, distances and colors are ignored with an error, and values loaded from hand-edited or merged files are sanitized.
2. **One broken mesh can't block the rest.** A mesh that fails to bake is reported with the outline as context, and every other outline still bakes.
3. **Problems are reported once**, with a fix in the message: unreadable meshes, static-batched renderers, custom fill materials that can't follow the stencil conventions, and stencil overflow.
4. **Nothing leaks.** Hidden parts and materials are destroyed with their outline and before domain reloads, and runtime bakes are freed with their last user.
