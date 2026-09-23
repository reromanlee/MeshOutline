#if UNITY_EDITOR
using System;

namespace reromanlee.MeshOutline
{
    /// <summary>
    /// Hooks the editor assembly installs, so the runtime can ask for editor-only work (baking
    /// into the asset cache) without referencing the editor assembly.
    /// </summary>
    internal static class OutlineEditorBridge
    {
        /// <summary>Asks the editor to check (and if needed bake) this outline's parts.</summary>
        internal static Action<ObjectOutline> RequestValidation;
    }
}
#endif
