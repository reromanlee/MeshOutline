using UnityEngine;

namespace reromanlee.MeshOutline
{
    /// <summary>How an outline interacts with geometry in front of it.</summary>
    public enum OutlineOcclusion
    {
        /// <summary>Hidden behind other geometry, like any object.</summary>
        Normal,

        /// <summary>Always visible, even through walls.</summary>
        [InspectorName("X-Ray")]
        XRay
    }
}
