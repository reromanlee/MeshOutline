using UnityEngine;

namespace reromanlee.MeshOutline
{
    /// <summary>Loads the package's outline shaders and holds their property IDs.</summary>
    internal static class OutlineShaders
    {
        // Loaded from Resources so outlines added at runtime always find them, and so they are
        // always included in builds.
        private const string MaskPath = "MeshOutline/OutlineMask";
        private const string FillPath = "MeshOutline/OutlineFill";

        /// <summary>Transparent+100: every mask is stamped before any fill.</summary>
        internal const int MaskQueue = 3100;

        /// <summary>Transparent+110.</summary>
        internal const int FillQueue = 3110;

        internal static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        internal static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
        internal static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        internal static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
        internal static readonly int WidthModeId = Shader.PropertyToID("_OutlineWidthMode");

        private static Shader mask;
        private static Shader fill;

        internal static Shader Mask => mask != null ? mask : mask = Load(MaskPath);
        internal static Shader Fill => fill != null ? fill : fill = Load(FillPath);

        private static Shader Load(string path)
        {
            var shader = Resources.Load<Shader>(path);
            if (shader == null)
            {
                Debug.LogError($"[MeshOutline] Shader 'Resources/{path}' is missing from the package; reinstall it.");
            }
            return shader;
        }
    }
}
