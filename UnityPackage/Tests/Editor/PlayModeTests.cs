using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace reromanlee.MeshOutline.Tests
{
    public class PlayModeTests : OutlineTestBase
    {
        // Issues #5 and #6 at runtime: spawning copies of an outlined object.
        [UnityTest]
        public IEnumerator InstantiatingAndDestroyingCopies_KeepsTheOriginalIntact()
        {
            yield return StartFresh();
            CreateOutlined("Original");
            Flush();
            yield return new EnterPlayMode();

            var original = GameObject.Find("Original").GetComponent<ObjectOutline>();
            GameObject copy = Object.Instantiate(original.gameObject);
            var copyOutline = copy.GetComponent<ObjectOutline>();
            yield return null;

            Assert.AreEqual(1, PartObjectsUnder(copy));
            Assert.AreNotSame(original.FillMaterial, copyOutline.FillMaterial);
            Assert.AreNotEqual(original.StencilRef, copyOutline.StencilRef);
            Assert.AreSame(original.BuiltParts[0].Mesh, copyOutline.BuiltParts[0].Mesh, "copies share the baked mesh");

            Object.Destroy(copy);
            yield return null;

            OutlinePart part = original.BuiltParts.Single();
            Assert.IsNotNull(part.Mesh);
            Assert.IsNotNull(original.FillMaterial);
            Assert.IsTrue(part.Renderer.enabled);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator TogglingEnabled_DoesNotAllocate()
        {
            yield return StartFresh();
            CreateOutlined("A");
            Flush();
            yield return new EnterPlayMode();

            var outline = GameObject.Find("A").GetComponent<ObjectOutline>();
            TestDelegate toggle = Toggle(outline);
            toggle(); // warm up
            Assert.That(toggle, Is.Not.AllocatingGCMemory());
            yield return new ExitPlayMode();
        }

        // Built outside the test's iterator: a lambda capturing its locals would hoist them into a
        // closure object that doesn't survive the domain reload of entering play mode.
        private static TestDelegate Toggle(ObjectOutline outline) => () =>
        {
            outline.enabled = false;
            outline.enabled = true;
        };

        [UnityTest]
        public IEnumerator AddComponentAtRuntime_BakesReadableMeshesOnce()
        {
            yield return StartFresh();
            yield return new EnterPlayMode();

            Mesh mesh = CreateQuadMesh();
            var a = CreateMeshObject("A", mesh).AddComponent<ObjectOutline>();
            var b = CreateMeshObject("B", mesh).AddComponent<ObjectOutline>();

            Assert.AreEqual(1, a.BuiltParts.Count);
            Assert.IsTrue(RuntimeBakeCache.Owns(a.BuiltParts[0].Mesh));
            Assert.AreSame(a.BuiltParts[0].Mesh, b.BuiltParts[0].Mesh);

            Mesh baked = a.BuiltParts[0].Mesh;
            Object.Destroy(a.gameObject);
            Object.Destroy(b.gameObject);
            yield return null;
            Assert.IsTrue(baked == null, "the runtime bake is freed with its last user");
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator AddComponentAtRuntime_UnreadableMesh_LogsOneClearError()
        {
            yield return StartFresh();
            yield return new EnterPlayMode();

            Mesh mesh = CreateQuadMesh();
            mesh.UploadMeshData(markNoLongerReadable: true);
            LogAssert.Expect(LogType.Error, new Regex("isn't readable.*Read/Write"));
            var outline = CreateMeshObject("A", mesh).AddComponent<ObjectOutline>();
            CreateMeshObject("B", mesh).AddComponent<ObjectOutline>(); // no second error

            Assert.AreEqual(0, outline.BuiltParts.Count);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator EnteringAndExitingPlayMode_KeepsExactlyOnePartPerSource()
        {
            yield return StartFresh();
            CreateOutlined("A");
            Flush();
            yield return new EnterPlayMode();
            Assert.AreEqual(1, LivePartObjects());
            Assert.AreEqual(1, LiveMaskMaterials());
            yield return new ExitPlayMode();
            Assert.AreEqual(1, LivePartObjects());
            Assert.AreEqual(1, LiveMaskMaterials());
            Assert.AreEqual(1, GameObject.Find("A").GetComponent<ObjectOutline>().BuiltParts.Count);
        }

        [UnityTest]
        public IEnumerator DomainReload_RebuildsWithoutLeaking()
        {
            yield return StartFresh();
            CreateOutlined("A");
            Flush();
            MarkResuming();
            // Some packages (e.g. URP 17.5) log unrelated errors during batch mode domain reloads,
            // which the test framework would count against this test.
            LogAssert.ignoreFailingMessages = true;
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();

            Assert.AreEqual(1, GameObject.Find("A").GetComponent<ObjectOutline>().BuiltParts.Count);
            Assert.AreEqual(1, LivePartObjects());
            Assert.AreEqual(1, LiveMaskMaterials());
        }
    }
}
