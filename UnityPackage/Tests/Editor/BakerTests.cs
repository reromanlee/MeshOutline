using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace reromanlee.MeshOutline.Tests
{
    public class BakerTests
    {
        private static Mesh Bake(Mesh source)
        {
            var baked = new Mesh();
            OutlineMeshBaker.Bake(source, baked);
            return baked;
        }

        [Test]
        public void SmoothNormals_AverageAllNormalsSharingAPosition()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh cube = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            Mesh baked = Bake(cube);

            // A unit cube's corners are split into three vertices with axis normals; averaged,
            // each points straight out of its corner.
            Vector3[] vertices = baked.vertices;
            Vector3[] normals = baked.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.That(normals[i], Is.EqualTo(vertices[i].normalized).Using(Vector3EqualityComparer.Instance));
            }
            Assert.AreEqual(cube.vertexCount, baked.vertexCount);
            Assert.AreEqual(1, baked.subMeshCount);
        }

        [Test]
        public void Bake_IsDeterministic()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Mesh capsule = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);

            Mesh first = Bake(capsule);
            Mesh second = Bake(capsule);
            CollectionAssert.AreEqual(first.vertices, second.vertices);
            CollectionAssert.AreEqual(first.normals, second.normals);
            CollectionAssert.AreEqual(first.triangles, second.triangles);
            Assert.AreEqual(OutlineMeshBaker.Fingerprint(capsule), OutlineMeshBaker.Fingerprint(capsule));
        }

        [Test]
        public void Bake_MergesSurfaceSubmeshesAndTriangulatesQuads()
        {
            var source = new Mesh();
            source.vertices = new[]
            {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
                new Vector3(2, 0, 0), new Vector3(3, 0, 0), new Vector3(3, 1, 0), new Vector3(2, 1, 0)
            };
            source.subMeshCount = 3;
            source.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
            source.SetIndices(new[] { 3, 4, 5, 6 }, MeshTopology.Quads, 1);
            source.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 2);

            Mesh baked = Bake(source);

            Assert.AreEqual(1, baked.subMeshCount);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 3, 5, 6 }, baked.triangles);
            Assert.AreEqual(source.vertexCount, baked.normals.Length, "normals are computed when the source has none");
        }

        [Test]
        public void SmoothNormals_TreatNegativeZeroAsTheSamePosition()
        {
            var vertices = new List<Vector3> { new Vector3(0f, 1f, 0f), new Vector3(-0f, 1f, 0f) };
            var normals = new List<Vector3> { Vector3.right, Vector3.up };

            List<Vector3> smooth = OutlineMeshBaker.CalculateSmoothNormals(vertices, normals, new List<int>());

            Vector3 expected = (Vector3.right + Vector3.up).normalized;
            Assert.That(smooth[0], Is.EqualTo(expected).Using(Vector3EqualityComparer.Instance));
            Assert.That(smooth[1], Is.EqualTo(expected).Using(Vector3EqualityComparer.Instance));
        }

        [Test]
        public void SmoothNormals_AreNeverZero()
        {
            // Both sides of a double-sided plane: the normals cancel out.
            var vertices = new List<Vector3> { Vector3.zero, Vector3.zero };
            var normals = new List<Vector3> { Vector3.forward, Vector3.back };

            List<Vector3> smooth = OutlineMeshBaker.CalculateSmoothNormals(vertices, normals, new List<int>());

            Assert.IsTrue(smooth.All(n => Mathf.Approximately(n.magnitude, 1f)));
        }

        [Test]
        public void Fingerprint_ChangesWhenTheMeshChanges()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            Hash128 before = OutlineMeshBaker.Fingerprint(mesh);
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up * 2 };
            Assert.AreNotEqual(before, OutlineMeshBaker.Fingerprint(mesh));
        }
    }
}
