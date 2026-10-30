using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using reromanlee.MeshOutline.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace reromanlee.MeshOutline.Tests
{
    public class BakingTests : OutlineTestBase
    {
        /// <summary>
        /// A prefab asset whose outline nothing has baked yet, as when Add Component is used on a
        /// prefab selected in the Project window. (Inactive while set up, so Awake doesn't bake it.)
        /// </summary>
        private static ObjectOutline CreateUnbakedPrefab(string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.SetActive(false);
            go.AddComponent<ObjectOutline>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{TestFolder}/{name}.prefab");
            Object.DestroyImmediate(go);
            var outline = prefab.GetComponent<ObjectOutline>();
            Assert.AreEqual(0, outline.BakedParts.Count, "precondition: nothing has baked the prefab yet");
            return outline;
        }

        [UnityTest]
        public IEnumerator ExcludedRenderer_IsBakedAhead_SoIncludingItAtRuntimeUsesTheBake()
        {
            yield return StartFresh();
            GameObject weapon = CreateOutlined("Excluding Weapon");
            GameObject scope = CreateMeshObject("Excluded Scope", BuiltinMesh(PrimitiveType.Sphere), weapon.transform);
            var outline = weapon.GetComponent<ObjectOutline>();
            outline.Refresh();
            outline.SetExcluded(scope.GetComponent<Renderer>(), true);
            Flush();
            Assert.AreEqual(2, outline.BakedParts.Count, "the excluded renderer is baked too");
            Assert.AreEqual(1, outline.BuiltParts.Count, "but not outlined");
            yield return new EnterPlayMode();

            GameObject found = GameObject.Find("Excluding Weapon");
            Assert.IsNotNull(found, "the outlined object survives entering play mode");
            outline = found.GetComponent<ObjectOutline>();
            Transform scopeTransform = found.transform.Find("Excluded Scope");
            Assert.IsNotNull(scopeTransform, "the excluded child survives entering play mode");
            var renderer = scopeTransform.GetComponent<Renderer>();
            outline.SetExcluded(renderer, false);
            Assert.IsTrue(EditorUtility.IsPersistent(PartOf(outline, renderer).Mesh), "included again from the editor bake, not baked at runtime");
            yield return new ExitPlayMode();
        }

        // Not a lambda in the test: a closure over the iterator's locals doesn't survive the domain
        // reload of entering play mode.
        private static OutlinePart PartOf(ObjectOutline outline, Renderer source) =>
            outline.BuiltParts.Single(p => p.Source == source);

        [Test]
        public void PrefabAsset_IsBakedAndSavedWhenShownInTheInspector()
        {
            ObjectOutline outline = CreateUnbakedPrefab("Shown");

            Object.DestroyImmediate(UnityEditor.Editor.CreateEditor(outline));
            Flush();

            Mesh baked = outline.BakedParts.Single().mesh;
            Assert.IsTrue(EditorUtility.IsPersistent(baked), "a prefab can only reference cached assets");
            string bakedGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(baked));
            StringAssert.Contains(bakedGuid, File.ReadAllText(AssetDatabase.GetAssetPath(outline)), "saved to the prefab file");
        }

        [Test]
        public void PrefabAsset_ProceduralMeshIsLeftToItsInstances()
        {
            var go = new GameObject("Procedural", typeof(MeshFilter), typeof(MeshRenderer));
            go.SetActive(false);
            go.AddComponent<ObjectOutline>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{TestFolder}/Procedural.prefab");
            Object.DestroyImmediate(go);
            // A prefab can't store a mesh that isn't an asset; give the asset's renderer one in memory.
            prefab.GetComponent<MeshFilter>().sharedMesh = CreateQuadMesh();
            var outline = prefab.GetComponent<ObjectOutline>();

            OutlineBakeValidator.Rebake(outline);

            Assert.AreEqual(0, outline.BakedParts.Count);
            Assert.AreEqual(ObjectOutlineEditor.PartStatus.RuntimeOnly,
                ObjectOutlineEditor.GetStatus(outline, prefab.GetComponent<Renderer>()));
        }

        [Test]
        public void BakePrefabs_BakesEveryPrefabOnce()
        {
            ObjectOutline first = CreateUnbakedPrefab("First");
            ObjectOutline second = CreateUnbakedPrefab("Second");

            Assert.AreEqual(2, OutlineBakeValidator.BakePrefabs(new[] { TestFolder }));
            Assert.AreEqual(1, first.BakedParts.Count);
            Assert.AreEqual(1, second.BakedParts.Count);
            Assert.AreEqual(0, OutlineBakeValidator.BakePrefabs(new[] { TestFolder }), "up-to-date prefabs aren't saved again");
        }

        [Test]
        public void Build_BakesMissingPartsWithoutWritingAssets()
        {
            var cached = CreateOutlined("Cached").GetComponent<ObjectOutline>();
            var procedural = CreateMeshObject("Procedural", CreateQuadMesh()).AddComponent<ObjectOutline>();
            Flush();
            Mesh cachedBake = cached.BakedParts.Single().mesh;
            // As if a script set the scene up without ever letting the editor bake it.
            cached.SetBakedParts(new List<ObjectOutline.BakedPart>());
            procedural.SetBakedParts(new List<ObjectOutline.BakedPart>());
            int cacheEntries = AssetDatabase.FindAssets("t:Mesh", new[] { CacheFolder }).Length;

            List<string> baked = OutlineBuildProcessor.BakeMissing(SceneManager.GetActiveScene());

            Assert.AreEqual(2, baked.Count);
            Assert.AreSame(cachedBake, cached.BakedParts.Single().mesh, "a valid cache entry is reused");
            Assert.IsFalse(EditorUtility.IsPersistent(procedural.BakedParts.Single().mesh), "anything else is baked into the built scene");
            Assert.AreEqual(cacheEntries, AssetDatabase.FindAssets("t:Mesh", new[] { CacheFolder }).Length, "no asset is written");
            Assert.IsEmpty(OutlineBuildProcessor.BakeMissing(SceneManager.GetActiveScene()), "up-to-date bakes are left alone");
        }
    }
}
