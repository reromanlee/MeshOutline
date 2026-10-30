### Testing

The package ships its test suite in `UnityPackage/Tests`. It compiles only in projects that opt in, so it costs nothing otherwise.

### What is covered

Every test lives in the Edit Mode assembly (`Tests/Editor`). The play mode tests bake in the editor first, then enter and exit play mode themselves, so they cover both sides of the editor-to-runtime handoff.

1. **Lifecycle:** shared cache bakes, duplicates and instantiated copies getting their own materials and stencil references, deleting copies, undoing deletions and removed components, prefabs without overrides, saved scenes without hidden parts, toggling `enabled` without rebuilding or allocating, entering and exiting play mode, and domain reloads without leaks.
2. **Robustness:** outlines disabled when their object loads, static-batched renderers with and without editor bakes, NaN and infinite input, custom fill materials missing the stencil conventions, and unreadable meshes reporting one clear error.
3. **Baking:** smoothed normals, merged submeshes and quads, determinism, fingerprints, reimported and edited sources, outdated bake formats, procedural meshes baked into the scene, swapped meshes, excluded renderers baked ahead, prefab assets baked from the inspector, Bake All Prefabs, and the build safety net reusing cache entries without writing assets.
4. **Hierarchies:** one silhouette per object, nested outlines, exclusions, `IncludeChildren`, inactive and re-parented children.
5. **Skinned meshes and LOD groups:** shared bones, blend shapes, per-frame tracking, swapped meshes and skeletons, LOD levels in edit mode and at runtime, and parts joining and leaving LOD groups exactly once.
6. **Editor tooling:** inspector bake statuses, Scene view selection, Clean Up Unused, moving the cache folder and Rebake All.
7. **Stencil references:** unique per enabled outline, reused when freed, and never colliding with URP's deferred bits.

For 1.0.0, all 58 tests passed on Unity 2022.3.62f3 with the Built-in Render Pipeline and on Unity 6000.6.7f1 with URP 17.6.0. Performance was measured separately, see [PERFORMANCE.md](PERFORMANCE.md).

### Running the tests locally

Add the package to `testables` in your project's `Packages/manifest.json`, then open **Window ▸ General ▸ Test Runner** and run the Edit Mode tests:

```json
{
  "dependencies": {
    "com.reromanlee.meshoutline": "https://github.com/reromanlee/MeshOutline.git?path=/UnityPackage"
  },
  "testables": [
    "com.reromanlee.meshoutline"
  ]
}
```

The tests run in fresh scenes and redirect the bake cache to a scratch folder that they delete afterwards, so the project's own baked meshes, scenes and prefabs are never touched.

### Continuous integration

The repository's GitHub Actions workflow runs the suite with GameCI on Unity 2022.3 and Unity 6000.0 for every push to `main` and every pull request. It needs the `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` repository secrets; without them the job passes with a notice instead of failing.
