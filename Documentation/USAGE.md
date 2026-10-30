### Usage

Everything beyond the README quick start. The importable **Hover & Select** sample (Package Manager ▸ MeshOutline ▸ Samples) shows these patterns running, in URP and Built-in, with either input system.

### Adding an outline

1. Select any GameObject with a mesh, or the root of a model.
2. **Add Component ▸ Rendering ▸ Object Outline.**

The outline is baked right away and shows in the Scene view. Add it in the editor whenever you can, in a scene or on the prefab, so it's baked ahead of time and costs nothing to build at runtime.

### Showing and hiding

```csharp
using reromanlee.MeshOutline;

[SerializeField] private ObjectOutline outline;

void OnHoverEnter() => outline.enabled = true;
void OnHoverExit() => outline.enabled = false;
```

Toggling `enabled` allocates nothing and rebuilds nothing. Leave the component disabled in the editor for outlines that should start hidden, such as hover highlights.

### Appearance

```csharp
outline.Color = Color.cyan;                    // HDR colors glow with bloom
outline.Width = 6f;                            // pixels at 1080p by default
outline.Occlusion = OutlineOcclusion.XRay;     // visible through walls

outline.WidthMode = OutlineWidthMode.ExactPixels;          // the same pixel count at any resolution
outline.WidthMode = OutlineWidthMode.ScalesWithDistance;   // a world-space thickness
outline.ReferenceDistance = 10f;               // 6 px at 10 m, 3 px at 20 m, 12 px at 5 m
```

Every setter applies immediately. `Width` turns negative values into 0, and `ReferenceDistance` is at least 0.01. NaN and infinite values are ignored with an error, so a bad calculation can't corrupt the frame.

### Hierarchies and exclusions

```csharp
outline.IncludeChildren = false;               // only this object's own renderer
outline.SetExcluded(muzzleFlash, true);        // leave one renderer out
bool hidden = outline.IsExcluded(muzzleFlash);
IReadOnlyList<Renderer> parts = outline.Parts; // the renderers currently outlined
```

The nearest Object Outline above a renderer owns it, so a child with its own outline is outlined separately. Excluded renderers are still baked, so including one again at runtime is instant. Children left out by `IncludeChildren = false` aren't baked, so turning it on at runtime needs Read/Write meshes.

### Keeping in sync

To cost nothing per frame, an outline copies each renderer's **enabled state, layer, rendering layers and blend-shape weights** only when it's enabled and when you call `Refresh()`. Moving, rotating, animating bones and activating or deactivating GameObjects never need either: the outline follows automatically.

```csharp
// After meshes were added, removed, swapped or re-parented at runtime, or to copy renderer
// state and blend-shape weights on demand:
outline.Refresh();
```

If something changes those every frame (facial animation, or toggling `renderer.enabled` instead of `SetActive`), either call `Refresh()` after the change or turn on **Track Source Every Frame**, which copies them right before each frame renders:

```csharp
outline.TrackSourceEveryFrame = true;
```

Swapping a skinned mesh or re-binding it to another skeleton (equipment, character customization) re-binds the outline on the next `Refresh()`.

### Outlines added at runtime

```csharp
var outline = enemy.AddComponent<ObjectOutline>();
```

`AddComponent` at runtime works, but its outline meshes are baked on the spot: about 2 ms for a 10,000-vertex mesh on a desktop CPU, more on mobile. It needs the meshes' **Read/Write** import setting, and it can't outline a renderer that's already static batched. For anything known ahead of time, add the component on the prefab instead.

### Custom fill materials

**Advanced ▸ Custom Fill Material** replaces the fill pass, for example with an animated or textured outline. Base its shader on the built-in `Hidden/MeshOutline/Fill`:

1. It must use `_StencilRef` (stencil reference, `Comp NotEqual`) and `_ZTest`, or its fill covers the object it outlines. A warning says when a material lacks them.
2. It's given `_OutlineColor`, `_OutlineWidth` and `_OutlineWidthMode`.
3. The outline uses a copy, so the material itself is never modified, and its render queue is forced to the fill's queue.

### Baked meshes and version control

Outline meshes are baked into **Assets/MeshOutline Data/Baked Meshes/**, one asset per source mesh. **Commit this folder**: scenes and prefabs reference the meshes in it.

1. **Edit ▸ Project Settings ▸ Mesh Outline** moves the folder (references stay valid) and shows its size.
2. **Rebake All** rebakes every cached mesh from its source, e.g. after changing a mesh outside Unity's import pipeline.
3. **Clean Up Unused** deletes bakes that no scene, prefab or loaded outline uses.
4. The same commands are under **Tools ▸ Mesh Outline**.

### Prefabs and builds

1. **Prefab assets bake themselves** when the inspector shows them, including outlines added from the Project window without opening Prefab Mode.
2. **Tools ▸ Mesh Outline ▸ Bake All Prefabs** bakes every prefab in the project, for prefabs generated or edited by scripts.
3. **Builds bake what's missing.** A scene outline without an up-to-date bake gets one in the build only, listed in the build log; open and save the scene to fix it at the source.

For build scripts and continuous integration, bake prefabs before building:

```bash
Unity -batchmode -projectPath <project> -executeMethod reromanlee.MeshOutline.Editor.OutlineBaking.BakeAllPrefabs -quit
```

### Troubleshooting

1. **"Can't be outlined at runtime: mesh ... isn't readable"**: the outline was added at runtime to a mesh without Read/Write. Enable Read/Write in the mesh's import settings, or add the outline in the editor.
2. **"Can't outline ... it's static batched"**: the outline was added at runtime to a static-batched renderer. Add it in the editor, or turn off Batching Static on that object.
3. **An outline doesn't follow `renderer.enabled` or blend shapes**: see [Keeping in sync](#keeping-in-sync).
4. **Changing the Game view's resolution doesn't change the outline**: that's Pixels at 1080p working. It keeps the same share of the screen at every resolution; use Exact Pixels for a fixed pixel count.
5. **A part shows "baked · LOD 1+" but isn't visible in edit mode**: edit mode only outlines LOD0; the other levels show at runtime.
6. **A part shows "not in prefab"**: its mesh isn't an asset, so the prefab can't store it. Scene instances still bake it; spawned instances need Read/Write.
7. **An outline looks wrong after changing a mesh outside Unity's import pipeline**: press **Rebake** on the outline, or **Rebake All** in Project Settings.
8. **"Some now share a stencil reference"**: more than 240 outlines are enabled at once. Outlines sharing one only interfere where they overlap on screen.
