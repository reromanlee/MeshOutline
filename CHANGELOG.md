# MeshOutline 1.0.0

### What's new

1. Constant-width outlines for any mesh, skinned mesh or whole hierarchy from one component, shown and hidden through `enabled` with no per-frame cost and no allocation.
2. Outline meshes baked ahead of time in the editor into a shared cache, one per source mesh, used by every scene and prefab and rebaked in place when a model is reimported.
3. One silhouette per object: every renderer under an outline merges into one shape, nested outlines stay independent, and overlapping outlines resolve per pixel by depth, with up to 240 unique stencil references.
4. Three width modes: Pixels at 1080p (the default), Exact Pixels and Scales with Distance.
5. Skinned meshes with blend shapes, LOD groups, static batching, X-ray occlusion and HDR colors.
6. Editor integration that never leaves stale or shared copies behind: duplicate, undo, prefabs, prefab assets edited from the Project window, Prefab Mode, play mode and domain reloads. Meshes that aren't assets (ProBuilder, procedural) are baked into the scene.
7. Build safety net: a scene outline still missing its bake gets one in the build itself, without writing any asset, and the build log lists it.
8. Runtime `AddComponent`, `Refresh()`, `SetExcluded()`, `IsExcluded()`, `Parts` and opt-in Track Source Every Frame. Excluded renderers are baked too, so including one again at runtime is instant.
9. Project Settings ▸ Mesh Outline and Tools ▸ Mesh Outline: move the cache folder, Rebake All, Bake All Prefabs (also `OutlineBaking.BakeAllPrefabs()` for build scripts and CI) and Clean Up Unused.
10. Inspector listing every outlined renderer with its bake status and an include toggle.
11. Hardened against bad input and broken data: NaN and infinite values are rejected, a mesh that fails to bake can't block the others, and each problem is reported once with the object as context.
12. Hover & Select sample, an Edit Mode test suite that also covers play mode, and a GameCI workflow.
13. URP and Built-in, forward and deferred, SRP Batcher compatible and single-pass instanced VR ready; Unity 2022.3 LTS and Unity 6.

### Important

1. The package lives in the `UnityPackage/` folder of the repository, so git URL installs must end with `?path=/UnityPackage`.
2. Commit the baked mesh folder (`Assets/MeshOutline Data/Baked Meshes/` by default) to version control: scenes and prefabs reference the meshes in it.

### Upgrading from internal builds

1. Scenes and prefabs saved with an internal 1.x build must be opened and saved with an internal 2.0 build first: this release no longer removes their hidden "Outline (generated)" children or reads their old color and width fields.
2. Scripts from internal 1.x builds: the component is `ObjectOutline` (was `Outline`), shown and hidden with `enabled` (was `IsVisible`), with `Color`, `Width` and `Occlusion` properties. `SyncChildOutlines`, `Create`, `Recalculate`, `Remove` and the material slots are gone; baking is automatic.
3. `OutlineWidthMode.Pixels` is now `OutlineWidthMode.ExactPixels`. Saved values are unaffected.
4. An outline that is disabled when its object loads now stays hidden until it's enabled; internal builds drew it.
5. Excluded renderers are now baked too, which can add a few entries to the baked mesh folder the first time each scene is opened.
