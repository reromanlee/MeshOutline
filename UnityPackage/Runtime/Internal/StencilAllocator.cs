using UnityEngine;

namespace reromanlee.MeshOutline
{
    /// <summary>
    /// Hands each enabled outline its own stencil reference, so its fill skips only its own
    /// silhouette and overlapping outlines resolve per pixel.
    /// </summary>
    /// <remarks>
    /// Values whose low four bits are zero are never handed out: URP's deferred path stamps
    /// exactly such values (bits 4-7) on every opaque pixel, and an outline sharing one would be
    /// clipped against lit geometry. That leaves 240 references; beyond that, outlines share
    /// them round-robin, and two outlines sharing one only interfere where they overlap.
    /// </remarks>
    internal static class StencilAllocator
    {
        internal const int Capacity = 240;

        private static readonly ObjectOutline[] owners = new ObjectOutline[256];
        private static int overflowCursor;
        private static bool warnedOverflow;

        internal static int Acquire(ObjectOutline owner)
        {
            for (int value = 1; value < owners.Length; value++)
            {
                if (!IsUsable(value)) continue;
                // Unity's == treats destroyed owners as null, so slots leaked by a missed
                // Release are reclaimed automatically.
                if (owners[value] == null)
                {
                    owners[value] = owner;
                    return value;
                }
            }

            if (!warnedOverflow)
            {
                warnedOverflow = true;
                Debug.LogWarning($"[MeshOutline] More than {Capacity} outlines are enabled at once. Some now share a " +
                                 "stencil reference, so their fills may merge where they overlap on screen.", owner);
            }
            return ValueAt(overflowCursor++ % Capacity);
        }

        internal static void Release(int value, ObjectOutline owner)
        {
            if (value > 0 && value < owners.Length && ReferenceEquals(owners[value], owner))
            {
                owners[value] = null;
            }
        }

        internal static bool IsUsable(int value) => value > 0 && value < 256 && (value & 0x0F) != 0;

        /// <summary>The index-th usable value: 1..15, 17..31, 33..47, ...</summary>
        internal static int ValueAt(int index) => 16 * (index / 15) + index % 15 + 1;
    }
}
