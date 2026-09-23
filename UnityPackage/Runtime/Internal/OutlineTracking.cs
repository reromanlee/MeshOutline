using System.Collections.Generic;
using UnityEngine;

namespace reromanlee.MeshOutline
{
    /// <summary>
    /// Drives <see cref="ObjectOutline.TrackSourceEveryFrame"/>: once per frame, right before
    /// rendering (after animation and LateUpdate), copies each tracked outline's source state to
    /// its parts. Subscribed only while at least one outline is tracked, so the default costs
    /// nothing per frame.
    /// </summary>
    internal static class OutlineTracking
    {
        private static readonly List<ObjectOutline> tracked = new List<ObjectOutline>();

        internal static bool IsTracked(ObjectOutline outline) => tracked.Contains(outline);

        internal static void Add(ObjectOutline outline)
        {
            if (tracked.Contains(outline)) return;
            if (tracked.Count == 0) Application.onBeforeRender += Tick;
            tracked.Add(outline);
        }

        internal static void Remove(ObjectOutline outline)
        {
            if (!tracked.Remove(outline)) return;
            if (tracked.Count == 0) Application.onBeforeRender -= Tick;
        }

        internal static void Tick()
        {
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                ObjectOutline outline = tracked[i];
                if (outline == null) tracked.RemoveAt(i);
                else outline.SyncState();
            }
            if (tracked.Count == 0) Application.onBeforeRender -= Tick;
        }
    }
}
