using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace reromanlee.MeshOutline.Tests
{
    public class RobustnessTests : OutlineTestBase
    {
        // Awake runs for a disabled component too, and no OnEnable follows to hide what it built.
        [UnityTest]
        public IEnumerator OutlineDisabledWhenLoaded_StaysHidden()
        {
            yield return StartFresh();
            CreateOutlined("A").GetComponent<ObjectOutline>().enabled = false;
            Flush();
            yield return new EnterPlayMode();

            var outline = GameObject.Find("A").GetComponent<ObjectOutline>();
            Assert.IsFalse(outline.BuiltParts.Single().Renderer.enabled, "a disabled outline must not draw");
            outline.enabled = true;
            Assert.IsTrue(outline.BuiltParts.Single().Renderer.enabled);
            yield return new ExitPlayMode();
        }

        // Static batching swaps a renderer's mesh for the whole batch's combined mesh.
        [UnityTest]
        public IEnumerator StaticBatchedSource_KeepsItsBakedOutlineMesh()
        {
            yield return StartFresh();
            var root = new GameObject("Root");
            foreach (string name in new[] { "A", "B" })
            {
                GameObject go = CreateOutlined(name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = name == "A" ? Vector3.left * 2f : Vector3.right * 2f;
            }
            Flush();
            yield return new EnterPlayMode();

            GameObject a = GameObject.Find("A");
            StaticBatchingUtility.Combine(GameObject.Find("Root"));
            Assert.IsTrue(a.GetComponent<Renderer>().isPartOfStaticBatch, "precondition: the source is static batched");

            var outline = a.GetComponent<ObjectOutline>();
            outline.Refresh();
            Mesh mesh = outline.BuiltParts.Single().Mesh;
            Assert.IsTrue(EditorUtility.IsPersistent(mesh), $"the outline should keep its baked mesh, not '{mesh.name}'");
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator StaticBatchedSourceWithoutABake_LogsOneClearError()
        {
            yield return StartFresh();
            yield return new EnterPlayMode();

            var root = new GameObject("Root");
            GameObject a = GameObject.CreatePrimitive(PrimitiveType.Cube);
            a.transform.SetParent(root.transform, false);
            GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.transform.SetParent(root.transform, false);
            b.transform.localPosition = Vector3.right * 2f;
            StaticBatchingUtility.Combine(root);

            LogAssert.Expect(LogType.Error, new Regex("static batched"));
            var outline = a.AddComponent<ObjectOutline>();
            Assert.AreEqual(0, outline.BuiltParts.Count);
            yield return new ExitPlayMode();
        }

        [Test]
        public void NonFiniteAppearanceValues_AreIgnoredWithAnError()
        {
            var outline = CreateOutlined("A").GetComponent<ObjectOutline>();
            outline.Width = 6f;
            outline.ReferenceDistance = 12f;

            LogAssert.Expect(LogType.Error, new Regex("Width of 'A' must be a finite"));
            outline.Width = float.NaN;
            LogAssert.Expect(LogType.Error, new Regex("ReferenceDistance of 'A' must be a finite"));
            outline.ReferenceDistance = float.PositiveInfinity;
            LogAssert.Expect(LogType.Error, new Regex("Color of 'A' must have finite"));
            outline.Color = new Color(float.NaN, 0f, 0f);

            Assert.AreEqual(6f, outline.Width);
            Assert.AreEqual(12f, outline.ReferenceDistance);
            Assert.AreEqual(Color.white, outline.Color);
            Assert.AreEqual(6f, outline.FillMaterial.GetFloat(OutlineShaders.WidthId));
        }

        [Test]
        public void CustomFillWithoutStencilProperties_WarnsOncePerMaterial()
        {
            var custom = new Material(Shader.Find("Unlit/Color")) { name = "Plain Unlit" };
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("'Plain Unlit'.*_StencilRef"));
                CreateOutlined("A").GetComponent<ObjectOutline>().CustomFillMaterial = custom;
                CreateOutlined("B").GetComponent<ObjectOutline>().CustomFillMaterial = custom;
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(custom);
            }
        }

        [Test]
        public void IsExcluded_ReportsExclusions()
        {
            GameObject go = CreateOutlined("A");
            var outline = go.GetComponent<ObjectOutline>();
            var renderer = go.GetComponent<Renderer>();

            Assert.IsFalse(outline.IsExcluded(renderer));
            outline.SetExcluded(renderer, true);
            Assert.IsTrue(outline.IsExcluded(renderer));
            Assert.IsFalse(outline.IsExcluded(null));
        }
    }
}
