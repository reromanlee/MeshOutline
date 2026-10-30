using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>
    /// Build safety net: while each scene is built, outlines whose bake is missing or outdated
    /// (added by an editor script, a bad merge, a batch-mode tool) get one, so a player never
    /// falls back to runtime baking for scene outlines. Valid cache entries are reused; anything
    /// else is baked into the built scene only. No asset is written during the build.
    /// </summary>
    internal sealed class OutlineBuildProcessor : IProcessSceneWithReport
    {
        private const int MaxListed = 10;

        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Entering play mode calls this too, without a report: scenes keep their editor bakes there.
            if (report == null) return;

            List<string> baked = BakeMissing(scene);
            if (baked.Count == 0) return;
            string listed = string.Join("\n", baked.GetRange(0, Mathf.Min(baked.Count, MaxListed)));
            if (baked.Count > MaxListed) listed += $"\n... and {baked.Count - MaxListed} more";
            Debug.LogWarning($"[MeshOutline] {baked.Count} outline part(s) in scene '{scene.path}' had no up-to-date bake, so " +
                             $"they were baked into this build only:\n{listed}\nOpen the scene (and any prefabs involved) in the " +
                             "editor and save it to store the bakes at the source.");
        }

        /// <summary>Bakes every outline part in the scene that lacks an up-to-date bake; lists what it baked.</summary>
        internal static List<string> BakeMissing(Scene scene)
        {
            var baked = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ObjectOutline outline in root.GetComponentsInChildren<ObjectOutline>(true))
                {
                    OutlineBakeValidator.BakeMissingForBuild(outline, baked);
                }
            }
            return baked;
        }
    }
}
