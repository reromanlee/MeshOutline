using UnityEngine;

namespace reromanlee.MeshOutline
{
    /// <summary>How <see cref="ObjectOutline.Width"/> is measured.</summary>
    public enum OutlineWidthMode
    {
        /// <summary>
        /// Pixels at 1080p: the outline covers the same share of the screen at any resolution
        /// (8 at 1080p is 16 pixels at 2160p) and keeps its thickness at any distance.
        /// </summary>
        [InspectorName("Pixels at 1080p")]
        PixelsAt1080p,

        /// <summary>Exact screen pixels, at any resolution and any distance.</summary>
        [InspectorName("Exact Pixels")]
        Pixels,

        /// <summary>
        /// A fixed thickness in world units, like part of the object: <see cref="ObjectOutline.Width"/>
        /// pixels (at 1080p with a 60° field of view) when the object is at
        /// <see cref="ObjectOutline.ReferenceDistance"/>, thinner farther away, thicker up close.
        /// </summary>
        [InspectorName("Scales with Distance")]
        ScalesWithDistance
    }
}
