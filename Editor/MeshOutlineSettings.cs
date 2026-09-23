using UnityEditor;
using UnityEngine;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>
    /// Project-wide settings, stored in ProjectSettings/ so they're shared through version control.
    /// </summary>
    [FilePath("ProjectSettings/MeshOutlineSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class MeshOutlineSettings : ScriptableSingleton<MeshOutlineSettings>
    {
        internal const string DefaultBakedMeshFolder = "Assets/MeshOutline Data/Baked Meshes";

        [SerializeField] private string bakedMeshFolder = DefaultBakedMeshFolder;

        /// <summary>Where baked outline meshes are stored. Always a folder under Assets.</summary>
        internal string BakedMeshFolder => IsValidFolder(bakedMeshFolder) ? bakedMeshFolder.TrimEnd('/') : DefaultBakedMeshFolder;

        internal void SetBakedMeshFolder(string folder)
        {
            bakedMeshFolder = folder;
            Save(true);
        }

        internal static bool IsValidFolder(string folder) =>
            !string.IsNullOrWhiteSpace(folder) && (folder == "Assets" || folder.StartsWith("Assets/"));
    }
}
