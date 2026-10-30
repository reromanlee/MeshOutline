using System.Collections.Generic;
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
        private static readonly HashSet<Material> reportedCustomFills = new HashSet<Material>();

        internal static Shader Mask => mask != null ? mask : mask = Load(MaskPath);
        internal static Shader Fill => fill != null ? fill : fill = Load(FillPath);

        /// <summary>
        /// Warns once per material when a custom fill can't follow the stencil and depth
        /// conventions, which would make its fill cover the object it outlines.
        /// </summary>
        internal static void CheckCustomFill(Material material, Object context)
        {
            if (material == null || material.shader == null) return;
            if (material.HasProperty(StencilRefId) && material.HasProperty(ZTestId)) return;
            if (!reportedCustomFills.Add(material)) return;
            Debug.LogWarning($"[MeshOutline] Custom fill material '{material.name}' on '{context.name}' has no _StencilRef or _ZTest " +
                             "property, so its fill draws over the object it outlines. Base its shader on Hidden/MeshOutline/Fill.", context);
        }

#if UNITY_EDITOR
        // With domain reload off, statics outlive play sessions: report again in each one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetReports() => reportedCustomFills.Clear();
#endif

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
