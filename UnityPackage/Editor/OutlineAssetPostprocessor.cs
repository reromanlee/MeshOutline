using UnityEditor;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>Rebakes cached outline meshes whose source model was reimported.</summary>
    internal sealed class OutlineAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            OutlineBakeCache.OnAssetsChanged(imported, deleted, moved, movedFrom);
        }
    }
}
