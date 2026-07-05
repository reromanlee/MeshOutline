using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace reromanlee.MeshOutline.Editor
{
    [CustomEditor(typeof(Outline))]
    public class OutlineEditor : UnityEditor.Editor
    {
        private const string packageOutlineMaskPath = "Packages/com.reromanlee.meshoutline/Runtime/Materials/OutlineMask.mat";
        private const string assetsOutlineMaskPath = "Assets/MeshOutline/Runtime/Materials/OutlineMask.mat";

        private const string packageOutlineFillPath = "Packages/com.reromanlee.meshoutline/Runtime/Materials/OutlineFill.mat";
        private const string assetsOutlineFillPath = "Assets/MeshOutline/Runtime/Materials/OutlineFill.mat";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            Outline outline = (Outline)target;
            if (outline.IsCreated)
            {
                if (GUILayout.Button(outline.IsVisible ? "Hide" : "Show"))
                {
                    outline.IsVisible = !outline.IsVisible;
                    MarkActveSceneDirty();
                }
            }
            if (GUILayout.Button(outline.IsCreated ? "Recalculate" : "Create"))
            {
                Material outlineMask = LoadMaterialAt(packageOutlineMaskPath, assetsOutlineMaskPath);
                Material outlineFill = LoadMaterialAt(packageOutlineFillPath, assetsOutlineFillPath);
                if (outline.IsCreated)
                {
                    outline.Recalculate(outlineMask, outlineFill);
                    MarkActveSceneDirty();
                    return;
                }
                outline.Create(outlineMask, outlineFill);
                MarkActveSceneDirty();
            }
            if (!outline.IsCreated)
            {
                EditorGUILayout.HelpBox("Outline object is not created.", MessageType.Warning);
            }
        }

        private Material LoadMaterialAt(string packagePath, string assetsPath)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(packagePath);
            if (material != null)
            {
                return material;
            }
            material = AssetDatabase.LoadAssetAtPath<Material>(assetsPath);
            if (material == null)
            {
                throw new NullReferenceException($"Material not found at '{packagePath}' or '{assetsPath}'");
            }
            return material;
        }

        private void MarkActveSceneDirty()
        {
            EditorSceneManager.MarkSceneDirty(
                EditorSceneManager.GetActiveScene()
            );
        }
    }
}