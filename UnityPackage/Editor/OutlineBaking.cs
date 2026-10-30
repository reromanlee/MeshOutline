namespace reromanlee.MeshOutline.Editor
{
    /// <summary>Batch baking for build scripts and continuous integration.</summary>
    public static class OutlineBaking
    {
        /// <summary>
        /// Bakes the outlines of every prefab under Assets whose outline meshes are missing or
        /// outdated, and saves those prefabs. From the command line:
        /// <c>-batchmode -executeMethod reromanlee.MeshOutline.Editor.OutlineBaking.BakeAllPrefabs -quit</c>.
        /// </summary>
        /// <returns>The number of prefabs that were updated.</returns>
        public static int BakeAllPrefabs() => OutlineBakeValidator.BakePrefabs(new[] { "Assets" });
    }
}
