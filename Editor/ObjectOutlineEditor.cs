using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>
    /// Inspector for <see cref="ObjectOutline"/>: appearance fields, a table of the renderers the
    /// outline covers (with bake status and an include toggle each), and a one-line summary.
    /// </summary>
    [CustomEditor(typeof(ObjectOutline))]
    [CanEditMultipleObjects]
    internal sealed class ObjectOutlineEditor : UnityEditor.Editor
    {
        internal enum PartStatus
        {
            Excluded,
            Baking,
            Baked,
            BakedInScene
        }

        private static class Styles
        {
            public static readonly GUIContent Width = new GUIContent("Width",
                "Outline width, in pixels at 1080p (Exact Pixels: in screen pixels). See Width Mode.");
            public static readonly GUIContent PixelsAt1080p = new GUIContent("px @1080p");
            public static readonly GUIContent Pixels = new GUIContent("px");
            public static readonly GUIContent ReferenceDistance = new GUIContent("Reference Distance",
                "The distance at which the outline is Width pixels thick (at 1080p with a 60° field of view). Farther away it gets thinner, closer it gets thicker, like the object itself.");
            public static readonly GUIContent Meters = new GUIContent("m");
            public static readonly GUIContent Rebake = new GUIContent("Rebake",
                "Rebake this outline's meshes from their sources, even if they look up to date.");
            public static readonly GUIContent Advanced = new GUIContent("Advanced");
            public static readonly GUIContent LodHidden = new GUIContent("baked · LOD 1+",
                "Outlined at runtime, whenever its LOD level is showing. Edit mode only outlines LOD0.");

            public static readonly GUIStyle Status = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            public static readonly GUIStyle Dimmed = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };

            static Styles()
            {
                Color text = Status.normal.textColor;
                Dimmed.normal.textColor = new Color(text.r, text.g, text.b, text.a * 0.5f);
            }
        }

        private const string PartsExpandedKey = "MeshOutline.Inspector.PartsExpanded";
        private const string AdvancedExpandedKey = "MeshOutline.Inspector.AdvancedExpanded";

        private SerializedProperty color;
        private SerializedProperty width;
        private SerializedProperty widthMode;
        private SerializedProperty referenceDistance;
        private SerializedProperty occlusion;
        private SerializedProperty includeChildren;
        private SerializedProperty customFillMaterial;
        private SerializedProperty trackSourceEveryFrame;
        private readonly List<Renderer> candidates = new List<Renderer>();

        private void OnEnable()
        {
            color = serializedObject.FindProperty("color");
            width = serializedObject.FindProperty("width");
            widthMode = serializedObject.FindProperty("widthMode");
            referenceDistance = serializedObject.FindProperty("referenceDistance");
            occlusion = serializedObject.FindProperty("occlusion");
            includeChildren = serializedObject.FindProperty("includeChildren");
            customFillMaterial = serializedObject.FindProperty("customFillMaterial");
            trackSourceEveryFrame = serializedObject.FindProperty("trackSourceEveryFrame");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(color);
            EditorGUILayout.PropertyField(widthMode);
            bool mixedModes = widthMode.hasMultipleDifferentValues;
            var mode = (OutlineWidthMode)widthMode.enumValueIndex;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.Slider(width, 0f, 20f, Styles.Width);
                GUIContent unit = mixedModes || mode == OutlineWidthMode.Pixels ? Styles.Pixels : Styles.PixelsAt1080p;
                GUILayout.Label(unit, EditorStyles.miniLabel, GUILayout.Width(62f));
            }
            if (mixedModes || mode == OutlineWidthMode.ScalesWithDistance)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(referenceDistance, Styles.ReferenceDistance);
                    GUILayout.Label(Styles.Meters, EditorStyles.miniLabel, GUILayout.Width(62f));
                }
            }
            EditorGUILayout.PropertyField(occlusion);
            EditorGUILayout.PropertyField(includeChildren);
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (targets.Length == 1) DrawParts((ObjectOutline)target);

            serializedObject.Update();
            bool advanced = EditorGUILayout.Foldout(SessionState.GetBool(AdvancedExpandedKey, false), Styles.Advanced, true);
            SessionState.SetBool(AdvancedExpandedKey, advanced);
            if (advanced)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(customFillMaterial);
                    EditorGUILayout.PropertyField(trackSourceEveryFrame);
                }
            }
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawSummary();
        }

        private void DrawParts(ObjectOutline outline)
        {
            outline.GatherCandidates(candidates, includeExcluded: true);
            int included = candidates.Count(r => !outline.IsExcluded(r));
            bool expanded = EditorGUILayout.Foldout(SessionState.GetBool(PartsExpandedKey, true),
                $"Parts ({included} of {candidates.Count})", true);
            SessionState.SetBool(PartsExpandedKey, expanded);
            if (!expanded) return;

            if (candidates.Count == 0)
            {
                EditorGUILayout.HelpBox("Nothing to outline yet. Add a MeshRenderer (with a MeshFilter) or a " +
                                        "SkinnedMeshRenderer to this object or its children.", MessageType.Info);
                return;
            }

            foreach (Renderer renderer in candidates)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(14f);
                    bool isIncluded = !outline.IsExcluded(renderer);
                    bool include = EditorGUILayout.Toggle(isIncluded, GUILayout.Width(16f));
                    if (include != isIncluded)
                    {
                        Undo.RecordObject(outline, include ? "Include Renderer in Outline" : "Exclude Renderer from Outline");
                        outline.SetExcluded(renderer, !include);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(outline);
                    }

                    if (GUILayout.Button(new GUIContent(renderer.name, "Click to highlight the renderer."), EditorStyles.label, GUILayout.MinWidth(40f)))
                    {
                        EditorGUIUtility.PingObject(renderer);
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(renderer.GetType().Name, Styles.Dimmed, GUILayout.Width(120f));
                    GUILayout.Label(StatusLabel(outline, renderer), isIncluded ? Styles.Status : Styles.Dimmed, GUILayout.Width(90f));
                }
            }
        }

        private static GUIContent StatusLabel(ObjectOutline outline, Renderer renderer)
        {
            PartStatus status = GetStatus(outline, renderer);
            if (status == PartStatus.Baked || status == PartStatus.BakedInScene)
            {
                OutlinePart part = outline.BuiltParts.FirstOrDefault(p => p.Source == renderer);
                if (part != null && part.HiddenByLod) return Styles.LodHidden;
            }
            switch (status)
            {
                case PartStatus.Excluded:
                    return new GUIContent("excluded");
                case PartStatus.Baking:
                    return new GUIContent("baking...", "Baked on the next editor update.");
                case PartStatus.BakedInScene:
                    return new GUIContent("baked (scene)",
                        "Its mesh isn't an asset (e.g. ProBuilder), so the outline mesh is stored in the scene; it's rebaked when the mesh changes.");
                default:
                    return new GUIContent("baked", "Uses the shared outline mesh cache (Project Settings > Mesh Outline).");
            }
        }

        internal static PartStatus GetStatus(ObjectOutline outline, Renderer renderer)
        {
            if (outline.IsExcluded(renderer)) return PartStatus.Excluded;
            Mesh sourceMesh = ObjectOutline.GetSourceMesh(renderer);
            foreach (ObjectOutline.BakedPart part in outline.BakedParts)
            {
                if (part.renderer != renderer) continue;
                if (part.mesh == null || part.sourceMesh != sourceMesh) return PartStatus.Baking;
                return EditorUtility.IsPersistent(part.mesh) ? PartStatus.Baked : PartStatus.BakedInScene;
            }
            return PartStatus.Baking;
        }

        private void DrawSummary()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(SummaryText(), EditorStyles.wordWrappedMiniLabel);
                bool canRebake = targets.Cast<ObjectOutline>().Any(o => !EditorUtility.IsPersistent(o));
                using (new EditorGUI.DisabledScope(!canRebake))
                {
                    if (GUILayout.Button(Styles.Rebake, EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        foreach (Object t in targets) OutlineBakeValidator.Rebake((ObjectOutline)t);
                    }
                }
            }
        }

        private string SummaryText()
        {
            if (targets.Length > 1) return $"{targets.Length} outlines selected. Select one to see its parts.";
            var outline = (ObjectOutline)target;
            if (EditorUtility.IsPersistent(outline))
            {
                return $"{outline.BakedParts.Count} baked parts. Open the prefab to edit them.";
            }

            bool baking = candidates.Any(r => GetStatus(outline, r) == PartStatus.Baking);
            if (baking) return "Baking...";
            int parts = outline.Parts.Count;
            int cached = outline.BakedParts.Where(p => p.mesh != null && EditorUtility.IsPersistent(p.mesh)).Select(p => p.mesh).Distinct().Count();
            return $"{parts} part{(parts == 1 ? "" : "s")} · {cached} cached mesh{(cached == 1 ? "" : "es")} · up to date";
        }
    }
}
