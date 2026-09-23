using System.Collections;
using System.Linq;
using NUnit.Framework;
using reromanlee.MeshOutline.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace reromanlee.MeshOutline.Tests
{
    public class CacheTests : OutlineTestBase
    {
        [UnityTest]
        public IEnumerator ReimportedSource_IsRebakedInPlace()
        {
            string sourcePath = TestFolder + "/Source.asset";
            AssetDatabase.CreateAsset(CreateQuadMesh(), sourcePath);
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath);
            var outline = CreateMeshObject("A", source).AddComponent<ObjectOutline>();
            Flush();
            Mesh baked = outline.BuiltParts.Single().Mesh;
            string bakedPath = AssetDatabase.GetAssetPath(baked);
            Assert.AreEqual(4, baked.vertexCount);

            // Change the source on disk and reimport it, like an artist re-exporting a model.
            Vector3[] vertices = source.vertices.Concat(new[] { new Vector3(0, 2, 0) }).ToArray();
            Vector3[] normals = source.normals.Concat(new[] { Vector3.back }).ToArray();
            int[] triangles = source.triangles.Concat(new[] { 3, 4, 2 }).ToArray();
            source.Clear();
            source.vertices = vertices;
            source.normals = normals;
            source.triangles = triangles;
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssetIfDirty(source);
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
            yield return WaitFor(() => outline.BuiltParts.Single().Mesh.vertexCount == 5);

            Assert.AreEqual(bakedPath, AssetDatabase.GetAssetPath(outline.BuiltParts.Single().Mesh), "rebaked in place, keeping the asset");
            Assert.AreEqual(5, outline.BuiltParts.Single().Mesh.vertexCount);
        }

        [Test]
        public void OutdatedFormatVersion_IsRebakedOnUse()
        {
            var outline = CreateOutlined("A").GetComponent<ObjectOutline>();
            Flush();
            string path = AssetDatabase.GetAssetPath(outline.BuiltParts.Single().Mesh);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            importer.userData = importer.userData.Replace($"\"version\":{OutlineMeshBaker.FormatVersion}", "\"version\":0");
            importer.SaveAndReimport();
            OutlineBakeCache.Invalidate();

            OutlineBakeValidator.Request(outline);
            Flush();

            StringAssert.Contains($"\"version\":{OutlineMeshBaker.FormatVersion}", AssetImporter.GetAtPath(path).userData);
            Assert.AreEqual(path, AssetDatabase.GetAssetPath(outline.BuiltParts.Single().Mesh));
        }

        [Test]
        public void ProceduralSource_IsBakedIntoTheScene_AndRebakedWhenEditedInPlace()
        {
            Mesh source = CreateQuadMesh();
            var outline = CreateMeshObject("A", source).AddComponent<ObjectOutline>();
            Flush();
            ObjectOutline.BakedPart first = outline.BakedParts.Single();
            Assert.IsFalse(EditorUtility.IsPersistent(first.mesh), "a mesh that isn't an asset can't be cached");
            Assert.IsNotEmpty(first.sourceHash);

            // Edit the mesh in place, the way ProBuilder does.
            Vector3[] vertices = source.vertices;
            vertices[0] += Vector3.left;
            source.vertices = vertices;
            OutlineBakeValidator.Request(outline);
            Flush();

            ObjectOutline.BakedPart second = outline.BakedParts.Single();
            Assert.AreNotSame(first.mesh, second.mesh);
            Assert.IsTrue(first.mesh == null, "the replaced scene bake should be destroyed");
            Assert.AreEqual(vertices[0], outline.BuiltParts.Single().Mesh.vertices[0]);
        }

        [UnityTest]
        public IEnumerator SwappingTheMesh_RebakesAutomatically()
        {
            GameObject go = CreateOutlined("A");
            Flush();
            var filter = go.GetComponent<MeshFilter>();
            Mesh sphere = BuiltinMesh(PrimitiveType.Sphere);

            Undo.RecordObject(filter, "Swap Mesh");
            filter.sharedMesh = sphere;
            var outline = go.GetComponent<ObjectOutline>();
            yield return WaitFor(() => outline.BuiltParts.Count == 1 && outline.BuiltParts[0].SourceMesh == sphere);

            Assert.AreSame(sphere, outline.BuiltParts.Single().SourceMesh);
        }
    }
}
