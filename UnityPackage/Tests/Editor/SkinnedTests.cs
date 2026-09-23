using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace reromanlee.MeshOutline.Tests
{
    public class SkinnedTests : OutlineTestBase
    {
        /// <summary>A two-bone strip with one blend shape, like a tiny character limb.</summary>
        private static SkinnedMeshRenderer CreateSkinnedStrip(string name)
        {
            var root = new GameObject(name);
            Transform lower = new GameObject("Lower").transform;
            lower.SetParent(root.transform, false);
            Transform upper = new GameObject("Upper").transform;
            upper.SetParent(lower, false);
            upper.localPosition = Vector3.up;

            var mesh = new Mesh { name = "Strip" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0) };
            mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                new BoneWeight { boneIndex0 = 1, weight0 = 1 }, new BoneWeight { boneIndex0 = 1, weight0 = 1 }
            };
            mesh.bindposes = new[] { lower.worldToLocalMatrix * root.transform.localToWorldMatrix, upper.worldToLocalMatrix * root.transform.localToWorldMatrix };
            var bulge = new Vector3[4];
            bulge[2] = bulge[3] = new Vector3(0, 0, -0.5f);
            mesh.AddBlendShapeFrame("Bulge", 100f, bulge, null, null);

            var skinned = root.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = mesh;
            skinned.bones = new[] { lower, upper };
            skinned.rootBone = lower;
            return skinned;
        }

        [Test]
        public void SkinnedSource_GetsASkinnedPartThatSharesItsBones()
        {
            SkinnedMeshRenderer source = CreateSkinnedStrip("Limb");
            var outline = source.gameObject.AddComponent<ObjectOutline>();
            Flush();

            OutlinePart part = outline.BuiltParts.Single();
            var skinned = part.Renderer as SkinnedMeshRenderer;
            Assert.IsNotNull(skinned, "a skinned source needs a skinned part to deform with it");
            CollectionAssert.AreEqual(source.bones, skinned.bones);
            Assert.AreSame(source.rootBone, skinned.rootBone);

            Assert.AreEqual(2, part.Mesh.bindposes.Length);
            Assert.AreEqual(4, part.Mesh.boneWeights.Length);
            Assert.AreEqual(1, part.Mesh.blendShapeCount);
            Assert.AreEqual("Bulge", part.Mesh.GetBlendShapeName(0));
        }

        [Test]
        public void BlendShapeWeights_AreCopiedWhenEnabledAndOnRefresh()
        {
            SkinnedMeshRenderer source = CreateSkinnedStrip("Limb");
            source.SetBlendShapeWeight(0, 40f);
            var outline = source.gameObject.AddComponent<ObjectOutline>();
            Flush();
            var skinned = (SkinnedMeshRenderer)outline.BuiltParts.Single().Renderer;
            Assert.AreEqual(40f, skinned.GetBlendShapeWeight(0));

            source.SetBlendShapeWeight(0, 75f);
            Assert.AreEqual(40f, skinned.GetBlendShapeWeight(0), "no per-frame work unless tracking is on");
            outline.Refresh();
            Assert.AreEqual(75f, skinned.GetBlendShapeWeight(0));
        }

        [Test]
        public void TrackingEveryFrame_CopiesSourceStateBeforeEachRender()
        {
            SkinnedMeshRenderer source = CreateSkinnedStrip("Limb");
            var outline = source.gameObject.AddComponent<ObjectOutline>();
            Flush();
            OutlinePart part = outline.BuiltParts.Single();

            Assert.IsFalse(OutlineTracking.IsTracked(outline));
            outline.TrackSourceEveryFrame = true;
            Assert.IsTrue(OutlineTracking.IsTracked(outline));

            source.SetBlendShapeWeight(0, 30f);
            source.enabled = false;
            OutlineTracking.Tick(); // what Application.onBeforeRender runs each frame
            Assert.AreEqual(30f, ((SkinnedMeshRenderer)part.Renderer).GetBlendShapeWeight(0));
            Assert.IsFalse(part.Renderer.enabled);

            outline.enabled = false;
            Assert.IsFalse(OutlineTracking.IsTracked(outline), "disabled outlines are never tracked");
            outline.enabled = true;
            Assert.IsTrue(OutlineTracking.IsTracked(outline));
            outline.TrackSourceEveryFrame = false;
            Assert.IsFalse(OutlineTracking.IsTracked(outline));
        }
    }
}
