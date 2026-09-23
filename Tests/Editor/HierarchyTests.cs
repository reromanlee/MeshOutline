using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace reromanlee.MeshOutline.Tests
{
    public class HierarchyTests : OutlineTestBase
    {
        private static ObjectOutline CreateRig(out Renderer body, out Renderer wheel)
        {
            // An empty root, like most imported models: no MeshFilter required.
            var root = new GameObject("Car");
            body = CreateMeshObject("Body", BuiltinMesh(PrimitiveType.Cube), root.transform).GetComponent<Renderer>();
            wheel = CreateMeshObject("Wheel", BuiltinMesh(PrimitiveType.Cylinder), body.transform).GetComponent<Renderer>();
            var outline = root.AddComponent<ObjectOutline>();
            Flush();
            return outline;
        }

        [Test]
        public void ChildRenderers_AreOutlinedAsOneSilhouette()
        {
            ObjectOutline outline = CreateRig(out Renderer body, out Renderer wheel);

            CollectionAssert.AreEqual(new[] { body, wheel }, outline.Parts);
            Assert.AreEqual(2, outline.BuiltParts.Count);
            // One stencil reference, one material pair for every part.
            Assert.IsTrue(outline.BuiltParts.All(p => p.Renderer.sharedMaterials[0] == outline.MaskMaterial &&
                                                      p.Renderer.sharedMaterials[1] == outline.FillMaterial));
            Assert.IsNull(outline.GetComponent<MeshFilter>(), "no MeshFilter should be added to the root");
        }

        [Test]
        public void NestedOutline_OwnsItsSubtree()
        {
            ObjectOutline outline = CreateRig(out Renderer body, out Renderer wheel);
            var nested = wheel.gameObject.AddComponent<ObjectOutline>();
            outline.Refresh();
            Flush();

            CollectionAssert.AreEqual(new[] { body }, outline.Parts);
            CollectionAssert.AreEqual(new[] { wheel }, nested.Parts);
            Assert.AreNotEqual(outline.StencilRef, nested.StencilRef);
        }

        [Test]
        public void ExcludedRenderer_IsNotOutlined()
        {
            ObjectOutline outline = CreateRig(out Renderer body, out Renderer wheel);
            outline.SetExcluded(wheel, true);
            Flush();
            CollectionAssert.AreEqual(new[] { body }, outline.Parts);
            Assert.AreEqual(1, PartObjectsUnder(outline.gameObject));

            outline.SetExcluded(wheel, false);
            Flush();
            CollectionAssert.AreEqual(new[] { body, wheel }, outline.Parts);
        }

        [Test]
        public void IncludeChildrenOff_OutlinesOnlyTheOwnRenderer()
        {
            GameObject go = CreateOutlined("A");
            CreateMeshObject("Child", BuiltinMesh(PrimitiveType.Sphere), go.transform);
            var outline = go.GetComponent<ObjectOutline>();
            outline.Refresh();
            Flush();
            Assert.AreEqual(2, outline.Parts.Count);

            outline.IncludeChildren = false;
            Flush();
            CollectionAssert.AreEqual(new[] { go.GetComponent<Renderer>() }, outline.Parts);
        }

        [Test]
        public void InactiveChildren_AreOutlinedAheadOfTime()
        {
            ObjectOutline outline = CreateRig(out Renderer _, out Renderer wheel);
            wheel.gameObject.SetActive(false);
            outline.Refresh();
            Flush();
            Assert.Contains(wheel, outline.Parts.ToList());
        }

        [Test]
        public void ReparentedRenderer_IsPickedUpByRefresh()
        {
            ObjectOutline outline = CreateRig(out Renderer body, out Renderer _);
            var extra = CreateMeshObject("Extra", BuiltinMesh(PrimitiveType.Sphere)).GetComponent<Renderer>();
            extra.transform.SetParent(body.transform, false);
            outline.Refresh();
            Flush();
            Assert.Contains(extra, outline.Parts.ToList());
        }
    }
}
