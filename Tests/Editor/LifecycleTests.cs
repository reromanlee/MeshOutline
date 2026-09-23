using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace reromanlee.MeshOutline.Tests
{
    public class LifecycleTests : OutlineTestBase
    {
        [Test]
        public void AddingComponent_BakesOneSharedCacheAssetPerSourceMesh()
        {
            ObjectOutline a = CreateOutlined("A").GetComponent<ObjectOutline>();
            ObjectOutline b = CreateOutlined("B").GetComponent<ObjectOutline>();
            Flush();

            Assert.AreEqual(1, a.BuiltParts.Count);
            Assert.AreEqual(1, b.BuiltParts.Count);
            Assert.AreSame(a.BuiltParts[0].Mesh, b.BuiltParts[0].Mesh, "outlines of the same mesh should share one bake");
            StringAssert.StartsWith(CacheFolder + "/", AssetDatabase.GetAssetPath(a.BuiltParts[0].Mesh));
            Assert.IsTrue(a.BuiltParts[0].Renderer.enabled);
        }

        // Issue #6: a copy shared the original's material instances, and even overwrote the
        // original's stencil reference with its own.
        [Test]
        public void Duplicate_GetsItsOwnMaterialsAndStencilReference()
        {
            GameObject original = CreateOutlined("A");
            Flush();
            var outline = original.GetComponent<ObjectOutline>();
            int originalRef = outline.StencilRef;

            Selection.activeGameObject = original;
            Unsupported.DuplicateGameObjectsUsingPasteboard();
            GameObject copy = Selection.activeGameObject;
            Flush();
            var copyOutline = copy.GetComponent<ObjectOutline>();

            Assert.AreNotSame(original, copy);
            Assert.AreEqual(1, PartObjectsUnder(original));
            Assert.AreEqual(1, PartObjectsUnder(copy), "the copy should replace the hidden part it inherited");
            Assert.AreNotSame(outline.FillMaterial, copyOutline.FillMaterial);
            Assert.AreNotSame(outline.MaskMaterial, copyOutline.MaskMaterial);
            Assert.AreNotEqual(originalRef, copyOutline.StencilRef);
            Assert.AreEqual(originalRef, outline.StencilRef);
            Assert.AreEqual(originalRef, (int)outline.MaskMaterial.GetFloat(OutlineShaders.StencilRefId));

            copyOutline.Color = Color.red;
            Assert.AreEqual(Color.white, outline.FillMaterial.GetColor(OutlineShaders.ColorId));
        }

        // Issue #5: deleting a copy destroyed the original's mesh and materials.
        [Test]
        public void DeletingDuplicate_KeepsOriginalOutlineIntact()
        {
            GameObject original = CreateOutlined("A");
            Flush();
            GameObject copy = Object.Instantiate(original);
            Flush();

            Undo.DestroyObjectImmediate(copy);

            var outline = original.GetComponent<ObjectOutline>();
            OutlinePart part = outline.BuiltParts.Single();
            Assert.IsNotNull(part.GameObject);
            Assert.IsNotNull(part.Mesh);
            Assert.IsTrue(part.Renderer.sharedMaterials.All(m => m != null));
            Assert.AreEqual(1, LivePartObjects());
            Assert.AreEqual(1, LiveMaskMaterials());
        }

        [Test]
        public void UndoingDelete_RestoresOutline()
        {
            CreateOutlined("A");
            Flush();
            Undo.IncrementCurrentGroup();
            Undo.DestroyObjectImmediate(GameObject.Find("A"));
            Assert.AreEqual(0, LivePartObjects());
            Assert.AreEqual(0, LiveMaskMaterials());

            Undo.PerformUndo();
            Flush();
            var outline = GameObject.Find("A").GetComponent<ObjectOutline>();
            Assert.IsNotNull(outline.BuiltParts.Single().Mesh);
            Assert.AreEqual(1, LivePartObjects());
            Assert.AreEqual(1, LiveMaskMaterials());
        }

        [Test]
        public void UndoingRemoveComponent_RestoresOutline()
        {
            GameObject go = CreateOutlined("A");
            Flush();
            Undo.IncrementCurrentGroup();
            Undo.DestroyObjectImmediate(go.GetComponent<ObjectOutline>());
            Assert.AreEqual(0, PartObjectsUnder(go));

            Undo.PerformUndo();
            Flush();
            Assert.AreEqual(1, go.GetComponent<ObjectOutline>().BuiltParts.Count);
            Assert.AreEqual(1, LiveMaskMaterials());
        }

        [Test]
        public void Prefab_StoresNoPartsAndItsInstancesHaveNoOverrides()
        {
            GameObject go = CreateOutlined("A");
            Flush();
            string path = TestFolder + "/Outlined.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);

            StringAssert.DoesNotContain(OutlinePart.ObjectName, File.ReadAllText(path));
            Mesh baked = prefab.GetComponent<ObjectOutline>().BakedParts.Single().mesh;
            Assert.IsTrue(EditorUtility.IsPersistent(baked), "the prefab should reference the cached asset");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Flush();
            Assert.AreSame(baked, instance.GetComponent<ObjectOutline>().BuiltParts.Single().Mesh);
            Assert.IsFalse(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false));
        }

        [Test]
        public void SavedScene_ContainsNoPartsOrMaterialInstances()
        {
            GameObject go = CreateOutlined("A");
            Flush();
            string path = TestFolder + "/Outlined.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path));

            string yaml = File.ReadAllText(path);
            StringAssert.DoesNotContain(OutlinePart.ObjectName, yaml);
            StringAssert.DoesNotContain("(Instance)", yaml);
            string bakedGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(go.GetComponent<ObjectOutline>().BuiltParts[0].Mesh));
            StringAssert.Contains(bakedGuid, yaml);

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Assert.IsFalse(SceneManager.GetActiveScene().isDirty, "opening a scene with up-to-date bakes shouldn't dirty it");
            Assert.AreEqual(1, GameObject.Find("A").GetComponent<ObjectOutline>().BuiltParts.Count);
            Flush();
            Assert.IsFalse(SceneManager.GetActiveScene().isDirty, "validating up-to-date bakes shouldn't dirty the scene");
        }

        [Test]
        public void TogglingEnabled_ShowsAndHidesPartsWithoutRebuilding()
        {
            var outline = CreateOutlined("A").GetComponent<ObjectOutline>();
            Flush();
            OutlinePart part = outline.BuiltParts.Single();

            outline.enabled = false;
            Assert.IsFalse(part.Renderer.enabled);
            outline.enabled = true;
            Assert.IsTrue(part.Renderer.enabled);
            Assert.AreSame(part, outline.BuiltParts.Single());
        }

        [Test]
        public void Appearance_IsAppliedToTheMaterialsImmediately()
        {
            var outline = CreateOutlined("A").GetComponent<ObjectOutline>();
            outline.Color = Color.red;
            outline.Width = 7f;
            outline.Occlusion = OutlineOcclusion.XRay;

            Assert.AreEqual(Color.red, outline.FillMaterial.GetColor(OutlineShaders.ColorId));
            Assert.AreEqual(7f, outline.FillMaterial.GetFloat(OutlineShaders.WidthId));
            Assert.AreEqual((float)CompareFunction.Always, outline.FillMaterial.GetFloat(OutlineShaders.ZTestId));
            Assert.AreEqual((float)CompareFunction.Always, outline.MaskMaterial.GetFloat(OutlineShaders.ZTestId));
            Assert.AreEqual(OutlineShaders.MaskQueue, outline.MaskMaterial.renderQueue);
            Assert.AreEqual(OutlineShaders.FillQueue, outline.FillMaterial.renderQueue);

            outline.Width = -3f;
            Assert.AreEqual(0f, outline.Width);
        }

        [Test]
        public void CustomFillMaterial_IsCopiedAndNeverModified()
        {
            var outline = CreateOutlined("A").GetComponent<ObjectOutline>();
            var custom = new Material(Shader.Find("Hidden/MeshOutline/Fill"));
            custom.SetColor(OutlineShaders.ColorId, Color.green);

            outline.CustomFillMaterial = custom;
            outline.Color = Color.blue;

            Assert.AreNotSame(custom, outline.FillMaterial);
            Assert.AreEqual(Color.green, custom.GetColor(OutlineShaders.ColorId));
            Assert.AreEqual(OutlineShaders.FillQueue, outline.FillMaterial.renderQueue);
            Object.DestroyImmediate(custom);
        }

        [Test]
        public void Parts_CopyTheSourceLayerAndEnabledState()
        {
            GameObject go = CreateOutlined("A");
            go.layer = 5;
            Flush();
            var outline = go.GetComponent<ObjectOutline>();
            OutlinePart part = outline.BuiltParts.Single();
            Assert.AreEqual(5, part.GameObject.layer);

            go.GetComponent<Renderer>().enabled = false;
            outline.Refresh();
            Assert.IsFalse(part.Renderer.enabled);
        }

        [Test]
        public void LegacyGeneratedChild_IsDeletedOnLoad()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.SetActive(false);
            var outline = go.AddComponent<ObjectOutline>();
            var legacy = new GameObject("Outline (generated)") { hideFlags = HideFlags.HideInHierarchy };
            legacy.transform.SetParent(go.transform, false);
            var serialized = new SerializedObject(outline);
            serialized.FindProperty("legacyGeneratedObject").objectReferenceValue = legacy;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(true); // Awake

            Assert.IsTrue(legacy == null);
        }
    }
}
