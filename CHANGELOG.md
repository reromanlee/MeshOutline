# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **The package moved into the repository's `UnityPackage/` folder,** so the git URL needs a path:
  `https://github.com/reromanlee/MeshOutline.git?path=/UnityPackage`. Installs from the old URL keep
  working until they update, then fail to resolve; replace the URL in **Packages/manifest.json**.
  Tags 2.0.0 and older still install from the old URL.
- The package now includes its LICENSE.md.

## [2.0.0] - 2026-09-23

A redesign around one idea: an outline saves only its settings and references to shared, baked
outline meshes, and rebuilds everything else when it loads. That fixes the open duplication issues
by construction, and makes prefabs, undo and runtime spawning work.

### Breaking changes

- **The component is now `ObjectOutline`** (was `Outline`, which clashed with `UnityEngine.UI.Outline`).
  Existing components keep working, since Unity binds them by script GUID, but scripts referencing
  `Outline` must be updated. It's under **Add Component > Rendering > Object Outline**.
- **Show and hide with `enabled`.** `IsVisible` is removed, and the component's checkbox now works.
- **Appearance is set on the component:** `Color`, `Width` and `Occlusion` replace the mask and fill
  material slots, `UseMaterialInstances`, `OutlineColor` and `OutlineWidth`. Setting them always
  takes effect immediately (1.0.0 silently ignored them unless material instances were enabled).
  `CustomFillMaterial` takes over for custom fill shaders.
- **Width is in pixels at 1080p** by default and no longer depends on the field of view. At a 60°
  FOV, 1.0.0 widths look almost the same (within 7%); narrower FOVs used to make outlines much thicker.
- **One component covers the whole hierarchy** as one silhouette, so `SyncChildOutlines` is removed.
  Nested outlines are independent. `RequireComponent(MeshFilter)` is gone, so an outline can sit on an
  empty model root.
- **Removed** `Create`, `Recalculate`, `Remove`, `IsCreated`, `IsBakeStale`, `GeneratedGameObject` and
  `HideGeneratedObjectInHierarchy`: baking is automatic, and the parts are always hidden and never saved.
- **Removed** the `OutlineMask` and `OutlineFill` materials. The shaders are now
  `Hidden/MeshOutline/Mask` and `Hidden/MeshOutline/Fill`, loaded from `Resources`.
- **Minimum Unity version is 2022.3.** 1.0.0 declared 2021.3 but only compiled on 6000.4 and newer.

To upgrade a project: update scripts that reference `Outline`, then open each scene or prefab that
has outlines and save it. Its hidden "Outline (generated)" children are removed and the outlines
rebaked automatically when it loads.

### Added

- Shared baked-mesh cache (`Assets/MeshOutline Data/Baked Meshes/`): one outline mesh per source mesh,
  used by every scene and prefab. Reimported models rebake their outline meshes in place. Meshes that
  aren't assets (ProBuilder, procedural) are baked into the scene and rebaked when edited.
- Prefab support: outlines on prefabs work in every instance, with no overrides.
- Skinned mesh support, including blend shapes, and LOD group support.
- `Refresh()` and the opt-in **Track Source Every Frame** to copy renderers' enabled state, layer and
  blend-shape weights.
- `IncludeChildren`, `SetExcluded()` and `Parts`.
- `WidthMode`: **Pixels at 1080p** (the default: the same thickness at any distance, the same share of
  the screen at any resolution), **Exact Pixels** (the same pixel count at any distance and
  resolution), or **Scales with Distance** (a world-space thickness, `Width` pixels at
  `ReferenceDistance`, thinner farther away).
- HDR outline colors, for glowing outlines with bloom.
- Runtime `AddComponent`: outline meshes are baked on the spot for Read/Write meshes, with one clear
  error for meshes that aren't readable.
- Inspector with a table of the outlined renderers and their bake status.
- **Edit > Project Settings > Mesh Outline**: move the cache folder, Rebake All, Clean Up Unused
  (also under **Tools > Mesh Outline**).
- **Hover & Select** sample.
- Tests (EditMode and play mode), run on Unity 2022.3 and 6000.5.

### Fixed

- Duplicating or instantiating an outlined object no longer shares the original's material instances
  and mesh. A copy also overwrote the original's stencil reference, so the two clipped each other's
  outlines. ([#6](https://github.com/reromanlee/MeshOutline/issues/6))
- Deleting a duplicate no longer destroys the original's outline.
  ([#5](https://github.com/reromanlee/MeshOutline/issues/5))
- Compile errors on Unity versions older than 6000.4.
- Undoing the deletion of an outlined object (or of the component) restores its outline.
- Reimporting a model updates its outline instead of keeping the old shape.
- The outline uses its object's layer and rendering layers (it was always on `Default`).
- Stencil references no longer collide with the values URP's deferred path writes, which could hide an
  outline against lit geometry.
- Selecting an outline's hidden part in the Scene view selects the outlined object instead.

## [1.0.0] - 2026-07-10

First release.

[Unreleased]: https://github.com/reromanlee/MeshOutline/compare/2.0.0...HEAD
[2.0.0]: https://github.com/reromanlee/MeshOutline/compare/1.0.0...2.0.0
[1.0.0]: https://github.com/reromanlee/MeshOutline/releases/tag/1.0.0
