using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace reromanlee.MeshOutline
{
    // TODO: make outline automatically being created and recalculated when mesh is changed, or when adding this component onto a game object with a mesh filter
    // TODO: make optional ability to create instance of outline material for each outline object, so that each outline can have its own color and width
    // TODO: make optional ability to sync visible state of outline with all child outline objects, so that when parent outline is hidden, all child outlines are also hidden
    // TODO: make fields for outline color and width to sync them between editor inspector and outline material, so that when changing color or width in inspector, it will also change the color and width of the outline material for concenience

    /// <summary>
    /// 
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public class Outline : MonoBehaviour
    {
        [SerializeField, HideInInspector] private MeshFilter meshFilter;
        [SerializeField, HideInInspector] private GameObject outlineGameObject;
        [SerializeField, HideInInspector] private MeshFilter outlineMeshFilter;
        [SerializeField, HideInInspector] private MeshRenderer outlineMeshRenderer;

        /// <summary>
        /// 
        /// </summary>
        public bool IsCreated
        {
            get => outlineGameObject != null;
        }

        /// <summary>
        /// 
        /// </summary>
        public bool IsVisible
        {
            get => outlineGameObject.activeSelf;
            set => outlineGameObject.SetActive(value);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="outlineMask"></param>
        /// <param name="outlineFill"></param>
        public void Create(Material outlineMask, Material outlineFill)
        {
            outlineGameObject = new GameObject("Outline (generated)");
            // TODO: make it invisible in the hierarchy, but still be able to select it in the scene view and save it into the scene
            outlineGameObject.transform.SetParent(transform);
            outlineGameObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(Vector3.zero));
            outlineGameObject.transform.localScale = Vector3.one;
            outlineMeshFilter = outlineGameObject.AddComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }
            Mesh sharedMesh = meshFilter.sharedMesh;
            // TODO: reuse mesh if it exists within outline object to avoid memory leaks
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="outlineMask"></param>
        /// <param name="outlineFill"></param>
        public void Recalculate(Material outlineMask, Material outlineFill)
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }
            Mesh sharedMesh = meshFilter.sharedMesh;
            // TODO: reuse mesh if it exists within outline object to avoid memory leaks
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
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