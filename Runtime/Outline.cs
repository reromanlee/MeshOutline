using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace reromanlee.MeshOutline
{
    public class Outline : MonoBehaviour
    {
        [SerializeField, HideInInspector] private MeshFilter meshFilter;
        [SerializeField, HideInInspector] private GameObject outlineGameObject;
        [SerializeField, HideInInspector] private MeshFilter outlineMeshFilter;
        [SerializeField, HideInInspector] private MeshRenderer outlineMeshRenderer;

        public bool IsCreated
        {
            get => outlineGameObject != null;
        }

        public bool IsVisible
        {
            get => outlineGameObject.activeSelf;
            set => outlineGameObject.SetActive(value);
        }

        public void Create(Material outlineMask, Material outlineFill)
        {
            outlineGameObject = new GameObject("Outline");
            outlineGameObject.transform.SetParent(transform);
            outlineGameObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(Vector3.zero));
            outlineGameObject.transform.localScale = Vector3.one;
            outlineMeshFilter = outlineGameObject.AddComponent<MeshFilter>();
            Mesh sharedMesh = meshFilter.sharedMesh;
            Mesh outlineMesh = new()
            {
                vertices = sharedMesh.vertices,
                triangles = sharedMesh.triangles
            };
            outlineMesh.RecalculateBounds();
            outlineMesh.RecalculateNormals();
            outlineMesh.RecalculateTangents();
            outlineMesh.SetUVs(
                channel: 3,
                CalculateNormals(sharedMesh)
            );
            outlineMesh.name = "Outline (generated)";
            outlineMeshFilter.sharedMesh = outlineMesh;
            outlineMeshRenderer = outlineGameObject.AddComponent<MeshRenderer>();
            outlineMeshRenderer.materials = new Material[] {
                outlineMask,
                outlineFill
            };
        }

        public void Recalculate(Material outlineMask, Material outlineFill)
        {
            Mesh sharedMesh = meshFilter.sharedMesh;
            Mesh outlineMesh = new()
            {
                vertices = sharedMesh.vertices,
                triangles = sharedMesh.triangles
            };
            outlineMesh.RecalculateBounds();
            outlineMesh.RecalculateNormals();
            outlineMesh.RecalculateTangents();
            outlineMesh.SetUVs(
                channel: 3,
                CalculateNormals(sharedMesh)
            );
            outlineMesh.name = "Outline (generated)";
            outlineMeshFilter.sharedMesh = outlineMesh;
            outlineMeshRenderer.materials = new Material[] {
                outlineMask,
                outlineFill
            };
        }

        private List<Vector3> CalculateNormals(Mesh mesh)
        {
            var groups = mesh.vertices.Select(
                (vertex, index) => new KeyValuePair<Vector3, int>(vertex, index)
            ).GroupBy(pair => pair.Key);
            List<Vector3> smoothNormals = new(mesh.normals);
            foreach (var group in groups)
            {
                if (group.Count() == 1) continue;
                Vector3 smoothNormal = Vector3.zero;
                foreach (var pair in group)
                {
                    smoothNormal += mesh.normals[pair.Value];
                }
                smoothNormal.Normalize();
                foreach (var pair in group)
                {
                    smoothNormals[pair.Value] = smoothNormal;
                }
            }
            return smoothNormals;
        }

    }
}