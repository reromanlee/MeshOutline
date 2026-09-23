using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.MeshOutline
{
    /// <summary>
    /// Builds the outline mesh for a source mesh: the same vertices, with NORMAL replaced by
    /// smoothed (position-averaged) normals and every surface submesh merged into one, plus the
    /// skinning data and blend-shape position deltas needed to deform like the source.
    /// </summary>
    /// <remarks>
    /// The bake is deterministic, so the same source always produces identical data and baked
    /// assets don't churn in version control.
    /// </remarks>
    internal static class OutlineMeshBaker
    {
        /// <summary>Bump whenever the baked data changes; outdated cached bakes are rebaked.</summary>
        internal const int FormatVersion = 1;

        internal static void Bake(Mesh source, Mesh target)
        {
            var vertices = new List<Vector3>(source.vertexCount);
            source.GetVertices(vertices);
            var normals = new List<Vector3>(source.vertexCount);
            source.GetNormals(normals);
            List<int> triangles = GetMergedTriangles(source);

            target.Clear();
            target.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            target.SetVertices(vertices);
            target.SetNormals(CalculateSmoothNormals(vertices, normals, triangles));
            // One submesh: the renderer draws it twice, once per material (mask, then fill).
            target.SetTriangles(triangles, 0, calculateBounds: false);
            CopySkinning(source, target);
            CopyBlendShapes(source, target);
            target.bounds = source.bounds;
        }

        /// <summary>Bind poses and bone weights, so a skinned part deforms exactly like its source.</summary>
        private static void CopySkinning(Mesh source, Mesh target)
        {
            Matrix4x4[] bindposes = source.bindposes;
            if (bindposes.Length == 0) return;
            target.bindposes = bindposes;
            NativeArray<byte> bonesPerVertex = source.GetBonesPerVertex();
            if (bonesPerVertex.Length > 0) target.SetBoneWeights(bonesPerVertex, source.GetAllBoneWeights());
        }

        private static void CopyBlendShapes(Mesh source, Mesh target)
        {
            int vertexCount = source.vertexCount;
            var positions = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var tangents = new Vector3[vertexCount];
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                string shapeName = source.GetBlendShapeName(shape);
                for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
                {
                    source.GetBlendShapeFrameVertices(shape, frame, positions, normals, tangents);
                    // Positions only: the normal deltas belong to the source's split normals, and
                    // applying them to the smoothed ones could open cracks at hard edges mid-morph.
                    target.AddBlendShapeFrame(shapeName, source.GetBlendShapeFrameWeight(shape, frame), positions, null, null);
                }
            }
        }

        /// <summary>
        /// Hash of everything the bake reads, used to detect a source mesh that changed since it
        /// was baked (reimported, or edited in place by a tool such as ProBuilder).
        /// </summary>
        internal static Hash128 Fingerprint(Mesh source)
        {
            var hash = new Hash128();
            hash.Append(FormatVersion);
            hash.Append(source.vertexCount);
            hash.Append(source.subMeshCount);

            var vectors = new List<Vector3>(source.vertexCount);
            source.GetVertices(vectors);
            hash.Append(vectors);
            source.GetNormals(vectors);
            hash.Append(vectors);

            var indices = new List<int>();
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                hash.Append((int)source.GetTopology(submesh));
                source.GetIndices(indices, submesh);
                hash.Append(indices);
            }

            hash.Append(source.bindposes);
            hash.Append(source.GetBonesPerVertex().ToArray());
            hash.Append(source.GetAllBoneWeights().ToArray());

            hash.Append(source.blendShapeCount);
            var deltas = new Vector3[source.vertexCount];
            var unusedNormals = new Vector3[source.vertexCount];
            var unusedTangents = new Vector3[source.vertexCount];
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                hash.Append(source.GetBlendShapeName(shape));
                for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
                {
                    hash.Append(source.GetBlendShapeFrameWeight(shape, frame));
                    source.GetBlendShapeFrameVertices(shape, frame, deltas, unusedNormals, unusedTangents);
                    hash.Append(deltas);
                }
            }
            return hash;
        }

        private static List<int> GetMergedTriangles(Mesh source)
        {
            var merged = new List<int>();
            var indices = new List<int>();
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                switch (source.GetTopology(submesh))
                {
                    case MeshTopology.Triangles:
                        source.GetIndices(indices, submesh);
                        merged.AddRange(indices);
                        break;
                    case MeshTopology.Quads:
                        source.GetIndices(indices, submesh);
                        for (int i = 0; i + 3 < indices.Count; i += 4)
                        {
                            merged.Add(indices[i]);
                            merged.Add(indices[i + 1]);
                            merged.Add(indices[i + 2]);
                            merged.Add(indices[i]);
                            merged.Add(indices[i + 2]);
                            merged.Add(indices[i + 3]);
                        }
                        break;
                    // Lines and points have no surface to outline.
                }
            }
            return merged;
        }

        /// <summary>
        /// Averages the normals of all vertices that share a position, so hard edges (split
        /// vertices) don't tear open when the fill shader extrudes along them.
        /// </summary>
        internal static List<Vector3> CalculateSmoothNormals(List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            int count = vertices.Count;
            var groupOf = new int[count];
            var sums = new List<Vector3>();
            var groups = new Dictionary<Vector3, int>(count);
            for (int i = 0; i < count; i++)
            {
                // Adding zero turns -0 into +0: they compare equal but hash differently.
                Vector3 key = vertices[i] + Vector3.zero;
                if (!groups.TryGetValue(key, out int group))
                {
                    group = sums.Count;
                    groups.Add(key, group);
                    sums.Add(Vector3.zero);
                }
                groupOf[i] = group;
            }

            bool hasNormals = normals.Count == count;
            if (hasNormals)
            {
                for (int i = 0; i < count; i++) sums[groupOf[i]] += normals[i];
            }
            else
            {
                // No authored normals: accumulate area-weighted face normals instead.
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    sums[groupOf[a]] += face;
                    sums[groupOf[b]] += face;
                    sums[groupOf[c]] += face;
                }
            }

            var smooth = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                Vector3 sum = sums[groupOf[i]];
                // Opposing normals (e.g. both sides of a double-sided plane) can cancel out; never
                // emit a zero normal, the shader would normalize it to NaN.
                if (sum.sqrMagnitude > 1e-12f) smooth.Add(sum.normalized);
                else if (hasNormals && normals[i].sqrMagnitude > 1e-12f) smooth.Add(normals[i].normalized);
                else smooth.Add(Vector3.up);
            }
            return smooth;
        }
    }
}
