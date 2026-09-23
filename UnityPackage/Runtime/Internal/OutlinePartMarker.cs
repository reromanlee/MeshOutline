using UnityEngine;

namespace reromanlee.MeshOutline
{
    /// <summary>
    /// Tags a hidden outline part. <c>Instantiate</c>, duplicate and copy/paste copy hidden
    /// children along with their parent (with <see cref="Owner"/> pointing at the copy), so a new
    /// outline uses this marker to find and delete the parts it inherited before building its own.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    internal sealed class OutlinePartMarker : MonoBehaviour
    {
        // Serialized so Instantiate re-points them at the copy's objects.
        [SerializeField] private ObjectOutline owner;
        [SerializeField] private Renderer source;

        internal ObjectOutline Owner => owner;
        internal Renderer Source => source;

        internal void Initialize(ObjectOutline outline, Renderer sourceRenderer)
        {
            owner = outline;
            source = sourceRenderer;
        }
    }
}
