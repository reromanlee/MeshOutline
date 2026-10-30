### Performance

What MeshOutline costs, measured in Unity. Numbers are the best of five rounds.

### Test setup

1. **Machine:** Intel Core i7-12700K, NVIDIA GeForce RTX 4070 Ti, Windows 11.
2. **Runtime:** Unity 2022.3.62f3 in play mode in the editor (Mono), with release code optimization.
3. **Scene:** Built-in primitives. Bake timings use generated grid meshes of the stated vertex count.

Editor play mode is slower than a release player, so these are upper bounds on this machine. Expect several times higher absolute numbers on mobile and WebGL hardware; the relative shape stays the same.

### Per frame

**Nothing runs per frame by default.** The outline is drawn by ordinary hidden renderers that follow their source through the transform hierarchy, LOD groups and skinning, with no `Update()` loop.

| Operation | Time | Allocated |
| --- | --- | --- |
| Show and hide (`enabled = true`, then `false`) | 2.2 µs | 0 B |
| Set `Color`, `Width` or another appearance property | 0.5 µs | 0 B |
| Track Source Every Frame, 10 mesh renderers | 3.2 µs per frame | 0 B |
| Track Source Every Frame, skinned mesh with 50 blend shapes | 4.8 µs per frame | 0 B |
| `Refresh()` with nothing changed, 1 renderer | 2.6 µs | 0 B |
| `Refresh()` with nothing changed, 10 renderers | 23 µs | 0 B |

1. **Showing and hiding is free to call often.** It enables the hidden renderers and assigns a stencil reference; the test suite asserts it allocates nothing.
2. **Tracking costs a few microseconds per outline.** It's off by default and only needed when something changes renderers' enabled state, layers or blend shapes every frame.
3. **`Refresh()` is for changes, not for every frame.** It re-gathers the hierarchy, which is cheap but grows with the number of renderers. On skinned meshes and in LOD groups it also allocates the small arrays Unity's `bones` and `GetLODs()` return.

### When an object loads

Building an outline creates one hidden GameObject per outlined renderer and two small materials per outline. It happens once, when the object loads or is instantiated, whether the outline starts enabled or not.

| Instantiate | Without outline | With outline | Outline cost |
| --- | --- | --- | --- |
| 1 renderer | 6 µs | 101 µs | ~95 µs |
| 10 renderers, one outline | 27 µs | 440 µs | ~41 µs per renderer |

**Pool objects you spawn in bulk.** Spawning a hundred outlined objects in one frame costs about 10 ms on this machine; pooled objects pay it once.

### Baking

| Bake | Time |
| --- | --- |
| Runtime, 10,000 vertices | 1.7 ms |
| Runtime, 100,000 vertices | 25 ms |
| Editor, 100,000 vertices, into the cache (asset write included) | ~165 ms |

1. **Editor bakes cost nothing at runtime.** They run once per source mesh and are shared by every scene and prefab that uses it; scenes and prefabs only store references.
2. **Runtime bakes are synchronous.** They run on the main thread the first time an outline is added at runtime to a mesh that has no editor bake, then are shared by every outline of that mesh and freed with the last one. Their temporary buffers grow with the vertex count and are released right after.
3. **Welding scales linearly.** Smoothing a 270,000-vertex grid takes 37 ms, including position welding with a well-mixed hash; Unity's own `Vector3` hash took 50 ms for the welding step alone on grid-aligned meshes.

### GPU

1. **Two unlit draw calls per outlined renderer:** a mask that writes only stencil (no color), and a fill that only draws the outline band around the silhouette.
2. **No extra render targets and no full-screen passes:** the cost scales with the outlined geometry, not with the screen resolution.
3. **SRP Batcher compatible.** Each outline has its own pair of materials, because its stencil reference is render state.
4. **Skinned meshes are skinned twice:** once for the source and once for the outline, which shares the same bones.

### Memory

1. **One outline mesh per source mesh**, shared everywhere: the source's vertex positions, smoothed normals and one merged index buffer, plus bone weights and blend-shape position deltas for skinned meshes. Runtime bakes free their CPU copy after upload.
2. **Per outline:** two small material instances.
3. **Per outlined renderer:** one hidden GameObject with a renderer and a marker component.
4. **No managed allocations per frame**, and none when showing, hiding or changing the appearance of an outline.
