using UnityEngine;

namespace reromanlee.MeshOutline.Samples
{
    /// <summary>Swings a bone back and forth, to show an outline following skinned animation.</summary>
    public sealed class SampleBend : MonoBehaviour
    {
        [SerializeField] private float angle = 40f;
        [SerializeField] private float speed = 1.5f;

        private void Update()
        {
            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * speed) * angle);
        }
    }
}
