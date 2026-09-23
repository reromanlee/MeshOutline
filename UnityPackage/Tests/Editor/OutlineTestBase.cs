using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using reromanlee.MeshOutline.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace reromanlee.MeshOutline.Tests
{
    /// <summary>
    /// Runs each test in a fresh scene, with the bake cache redirected to a scratch folder that is
    /// deleted afterwards (never the project's real cache).
    /// </summary>
    public abstract class OutlineTestBase
    {
        protected const string TestFolder = "Assets/MeshOutlineTests";
        protected const string CacheFolder = TestFolder + "/Baked";

        // The test framework re-runs SetUp when a test resumes after a domain reload.
        private const string ResumingKey = "MeshOutline.Tests.Resuming";

        [SetUp]
        public void SetUpScene()
        {
            OutlineBakeCache.FolderOverride = CacheFolder;
            if (Application.isPlaying || SessionState.GetBool(ResumingKey, false)) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(TestFolder)) AssetDatabase.CreateFolder("Assets", "MeshOutlineTests");
            OutlineBakeCache.Invalidate();
        }

        [TearDown]
        public void TearDownScene()
        {
            if (Application.isPlaying) return;
            SessionState.EraseBool(ResumingKey);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(TestFolder);
            OutlineBakeCache.FolderOverride = null;
            OutlineBakeCache.Invalidate();
        }

        /// <summary>Call before an action that reloads the domain mid-test (not play mode).</summary>
        protected static void MarkResuming() => SessionState.SetBool(ResumingKey, true);

        /// <summary>
        /// Starts a play mode test with a settled, empty scene: when the previous test ended right
        /// after exiting play mode, Unity may still be restoring that test's scene.
        /// </summary>
        protected static IEnumerator StartFresh()
        {
            yield return null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        protected static GameObject CreateOutlined(string name, PrimitiveType type = PrimitiveType.Cube)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.AddComponent<ObjectOutline>();
            return go;
        }

        protected static GameObject CreateMeshObject(string name, Mesh mesh, Transform parent = null)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        protected static Mesh BuiltinMesh(PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return mesh;
        }

        /// <summary>A small readable mesh that isn't an asset.</summary>
        protected static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh { name = "Test Quad" };
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return mesh;
        }

        protected static void Flush() => OutlineBakeValidator.Flush();

        /// <summary>Lets editor ticks run (change events, delayed calls) until the condition holds.</summary>
        protected static IEnumerator WaitFor(Func<bool> condition, int maxFrames = 60)
        {
            for (int i = 0; i < maxFrames && !condition(); i++) yield return null;
        }

        protected static int PartObjectsUnder(GameObject root) =>
            root.GetComponentsInChildren<OutlinePartMarker>(true).Length;

        /// <summary>Hidden parts alive anywhere (to catch leaks).</summary>
        protected static int LivePartObjects() =>
            Resources.FindObjectsOfTypeAll<OutlinePartMarker>().Count(m => m != null && !EditorUtility.IsPersistent(m));

        /// <summary>Outline material instances alive anywhere (to catch leaks).</summary>
        protected static int LiveMaskMaterials() =>
            Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.name == "Outline Mask (Instance)");
    }
}
