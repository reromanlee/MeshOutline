using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace reromanlee.MeshOutline.Tests
{
    public class LodTests : OutlineTestBase
    {
        private static ObjectOutline CreateLodObject(out LODGroup group, out Renderer lod0, out Renderer lod1)
        {
            var root = new GameObject("Tree");
            lod0 = CreateMeshObject("LOD0", BuiltinMesh(PrimitiveType.Sphere), root.transform).GetComponent<Renderer>();
            lod1 = CreateMeshObject("LOD1", BuiltinMesh(PrimitiveType.Cube), root.transform).GetComponent<Renderer>();
            group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.5f, new[] { lod0 }), new LOD(0.1f, new[] { lod1 }) });
            var outline = root.AddComponent<ObjectOutline>();
            Flush();
            return outline;
        }

        private static OutlinePart PartOf(ObjectOutline outline, Renderer source) =>
            outline.BuiltParts.Single(p => p.Source == source);

        [Test]
        public void InEditMode_OnlyLod0IsOutlined_AndTheLodGroupIsUntouched()
        {
            ObjectOutline outline = CreateLodObject(out LODGroup group, out Renderer lod0, out Renderer lod1);

            Assert.AreEqual(2, outline.BuiltParts.Count, "every level is baked, for play mode");
            Assert.IsTrue(PartOf(outline, lod0).Renderer.enabled);
            Assert.IsFalse(PartOf(outline, lod1).Renderer.enabled);
            Assert.AreEqual(1, group.GetLODs()[0].renderers.Length);
            Assert.AreEqual(1, group.GetLODs()[1].renderers.Length);
        }

        [UnityTest]
        public IEnumerator AtRuntime_PartsJoinTheirSourcesLodLevel_AndLeaveWithTheOutline()
        {
            yield return StartFresh();
            CreateLodObject(out _, out _, out _);
            yield return new EnterPlayMode();

            var outline = GameObject.Find("Tree").GetComponent<ObjectOutline>();
            LODGroup group = outline.GetComponent<LODGroup>();
            Renderer lod0 = group.GetLODs()[0].renderers[0];
            Renderer lod1 = group.GetLODs()[1].renderers[0];
            CollectionAssert.AreEquivalent(new[] { lod0, PartOf(outline, lod0).Renderer }, group.GetLODs()[0].renderers);
            CollectionAssert.AreEquivalent(new[] { lod1, PartOf(outline, lod1).Renderer }, group.GetLODs()[1].renderers);
            Assert.IsTrue(PartOf(outline, lod1).Renderer.enabled, "at runtime the LODGroup decides visibility");

            Object.Destroy(outline);
            yield return null;
            CollectionAssert.AreEqual(new[] { lod0 }, group.GetLODs()[0].renderers);
            CollectionAssert.AreEqual(new[] { lod1 }, group.GetLODs()[1].renderers);
            yield return new ExitPlayMode();
        }
    }
}
