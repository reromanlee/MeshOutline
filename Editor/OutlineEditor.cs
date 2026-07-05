using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>
    /// Inspector for <see cref="Outline"/>: shows bake status and provides
    /// Create/Recalculate/Remove buttons with Undo support.
    /// </summary>
    [CustomEditor(typeof(Outline))]
    [CanEditMultipleObjects]
    public class OutlineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            DrawStatus();
            DrawButtons();
        }

        private void DrawStatus()
        {
            if (targets.Length != 1) return;
            var outline = (Outline)target;

            if (!outline.IsCreated)
            {
                EditorGUILayout.HelpBox(
                    "No outline baked yet. Assign the mask and fill materials and press Create.",
                    MessageType.Info);
            }
            else if (outline.IsBakeStale)
            {
                EditorGUILayout.HelpBox(
                    "The source mesh changed since the outline was baked. Press Recalculate " +
                    "(this normally happens automatically).",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Outline is baked and up to date.", MessageType.None);
            }
        }

        private void DrawButtons()
        {
            bool anyCreated = false;
            foreach (Object t in targets)
            {
                if (((Outline)t).IsCreated) { anyCreated = true; break; }
            }

            if (targets.Length == 1 && anyCreated)
            {
                var outline = (Outline)target;
                if (GUILayout.Button(outline.IsVisible ? "Hide" : "Show"))
                {
                    Undo.RecordObject(outline.GeneratedGameObject, "Toggle Outline Visibility");
                    outline.IsVisible = !outline.IsVisible;
                    MarkDirty(outline);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(anyCreated ? "Recalculate" : "Create"))
                {
                    foreach (Object t in targets) CreateOrRecalculate((Outline)t);
                }

                using (new EditorGUI.DisabledScope(!anyCreated))
                {
                    if (GUILayout.Button("Remove"))
                    {
                        foreach (Object t in targets) RemoveOutline((Outline)t);
                    }
                }
            }
        }

        internal static void CreateOrRecalculate(Outline outline)
        {
            Undo.RecordObject(outline, "Bake Outline");
            bool wasCreated = outline.IsCreated;

            outline.Recalculate();

            if (!wasCreated && outline.GeneratedGameObject != null)
            {
                Undo.RegisterCreatedObjectUndo(outline.GeneratedGameObject, "Bake Outline");
            }
            MarkDirty(outline);
        }

        internal static void RemoveOutline(Outline outline)
        {
            Undo.RecordObject(outline, "Remove Outline");
            if (outline.GeneratedGameObject != null)
            {
                // Undo-aware destroy of the child; Remove() then clears the remaining
                // references and destroys the baked mesh / material instances.
                Undo.DestroyObjectImmediate(outline.GeneratedGameObject);
            }
            outline.Remove();
            MarkDirty(outline);
        }

        internal static void MarkDirty(Outline outline)
        {
            EditorUtility.SetDirty(outline);
            if (!Application.isPlaying && outline.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(outline.gameObject.scene);
            }
        }
    }
}
