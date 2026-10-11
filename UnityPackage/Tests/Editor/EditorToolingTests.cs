using System.Linq;
using NUnit.Framework;
using reromanlee.MeshOutline.Editor;
using UnityEditor;
using UnityEngine;
using PartStatus = reromanlee.MeshOutline.Editor.ObjectOutlineEditor.PartStatus;

namespace reromanlee.MeshOutline.Tests
{
    public class EditorToolingTests : OutlineTestBase
    {
        [Test]
        public void Inspector_ReportsEachPartsBakeStatus()
        {
            var root = new GameObject("Root");
            Renderer cached = CreateMeshObject("Cached", BuiltinMesh(PrimitiveType.Cube), root.transform).GetComponent<Renderer>();
            Renderer procedural = CreateMeshObject("Procedural", CreateQuadMesh(), root.transform).GetComponent<Renderer>();
            Renderer excluded = CreateMeshObject("Excluded", BuiltinMesh(PrimitiveType.Sphere), root.transform).GetComponent<Renderer>();
            var outline = root.AddComponent<ObjectOutline>();
            outline.SetExcluded(excluded, true);

            Assert.AreEqual(PartStatus.Baking, ObjectOutlineEditor.GetStatus(outline, cached));
            Flush();
            Assert.AreEqual(PartStatus.Baked, ObjectOutlineEditor.GetStatus(outline, cached));
            Assert.AreEqual(PartStatus.BakedInScene, ObjectOutlineEditor.GetStatus(outline, procedural));
            Assert.AreEqual(PartStatus.Excluded, ObjectOutlineEditor.GetStatus(outline, excluded));

            cached.GetComponent<MeshFilter>().sharedMesh = BuiltinMesh(PrimitiveType.Capsule);
            Assert.AreEqual(PartStatus.Baking, ObjectOutlineEditor.GetStatus(outline, cached), "a swapped mesh shows as pending until rebaked");
        }

        [Test]
        public void SelectingAHiddenPart_SelectsItsSourceInstead()
        {
            GameObject go = CreateOutlined("A");
            Flush();
            GameObject part = go.GetComponent<ObjectOutline>().BuiltParts.Single().GameObject;

            // Selection.selectionChanged only fires in the interactive editor, not in batch mode:
            // check the subscription, then run the handler the way the editor would.
            Assert.IsTrue(Selection.selectionChanged.GetInvocationList().Any(d => d.Method.Name == nameof(OutlineSelectionRedirect.Redirect)));
            Selection.objects = new Object[] { part, go };
            OutlineSelectionRedirect.Redirect();

            CollectionAssert.AreEqual(new Object[] { go }, Selection.objects);
        }

        [Test]
        public void CleanUp_FindsOnlyBakesNothingUses()
        {
            GameObject used = CreateOutlined("Used", PrimitiveType.Cube);
            GameObject unused = CreateOutlined("Unused", PrimitiveType.Sphere);
            Flush();
            string usedPath = AssetDatabase.GetAssetPath(used.GetComponent<ObjectOutline>().BuiltParts[0].Mesh);
            string unusedPath = AssetDatabase.GetAssetPath(unused.GetComponent<ObjectOutline>().BuiltParts[0].Mesh);
            Object.DestroyImmediate(unused);

            CollectionAssert.AreEqual(new[] { unusedPath }, OutlineCacheTools.FindUnusedEntries());
            OutlineCacheTools.DeleteEntries(OutlineCacheTools.FindUnusedEntries());

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Mesh>(unusedPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Mesh>(usedPath));
        }

        [Test]
        public void MovingTheCache_KeepsEveryReferenceValid()
        {
            var outline = CreateOutlined("A").GetComponent<ObjectOutline>();
            Flush();
            Mesh baked = outline.BuiltParts[0].Mesh;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(baked));

            OutlineCacheTools.MoveCache(TestFolder + "/Moved");

            StringAssert.StartsWith(TestFolder + "/Moved/", AssetDatabase.GUIDToAssetPath(guid));
            Assert.AreSame(baked, outline.BakedParts[0].mesh);
            Assert.AreEqual(TestFolder + "/Moved", OutlineBakeCache.Folder);

            // New bakes go to the new folder, and the moved entry is still found by its source.
            OutlineBakeValidator.Rebake(outline);
            Assert.AreSame(baked, outline.BakedParts[0].mesh);
        }

        [Test]
        public void RebakeAll_RestoresEveryCachedMesh()
        {
            var outline = CreateOutlined("A").GetComponent<ObjectOutline>();
            Flush();
            Mesh baked = outline.BuiltParts[0].Mesh;
            int vertexCount = baked.vertexCount;
            baked.Clear();

            Assert.AreEqual(1, OutlineCacheTools.RebakeAll(out int orphaned));
            Assert.AreEqual(0, orphaned);
            Assert.AreEqual(vertexCount, baked.vertexCount);
        }
    }
}
