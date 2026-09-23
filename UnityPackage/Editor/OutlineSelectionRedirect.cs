using System.Linq;
using UnityEditor;
using UnityEngine;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>
    /// A hidden part covers exactly the same pixels as its source, so a click in the Scene view can
    /// land on it. Redirect such selections to the source object the user actually clicked.
    /// </summary>
    [InitializeOnLoad]
    internal static class OutlineSelectionRedirect
    {
        static OutlineSelectionRedirect()
        {
            Selection.selectionChanged += Redirect;
        }

        internal static void Redirect()
        {
            Object[] selection = Selection.objects;
            bool redirected = false;
            for (int i = 0; i < selection.Length; i++)
            {
                if (!(selection[i] is GameObject go) || !go.TryGetComponent(out OutlinePartMarker marker)) continue;
                Renderer source = marker.Source;
                selection[i] = source != null ? source.gameObject : null;
                redirected = true;
            }
            if (redirected) Selection.objects = selection.Where(o => o != null).Distinct().ToArray();
        }
    }
}
