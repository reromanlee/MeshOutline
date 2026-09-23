using UnityEditor;
using UnityEngine;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>Inspector for <see cref="ObjectOutline"/>.</summary>
    [CustomEditor(typeof(ObjectOutline))]
    [CanEditMultipleObjects]
    internal sealed class ObjectOutlineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Rebake"))
            {
                foreach (Object t in targets) OutlineBakeValidator.Rebake((ObjectOutline)t);
            }
        }
    }
}
