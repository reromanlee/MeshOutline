using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace reromanlee.MeshOutline.Samples
{
    /// <summary>
    /// Outlines the object under the pointer, and keeps clicked objects outlined in another color
    /// until they're clicked again. Put it on the camera. Pickable objects need a collider and an
    /// <see cref="ObjectOutline"/> (on them or a parent), usually disabled to start with.
    /// </summary>
    /// <remarks>
    /// Showing and hiding an outline is just <c>outline.enabled</c>: no allocation, no rebaking.
    /// Works with both the Input System package and the legacy Input Manager.
    /// </remarks>
    public sealed class OutlineOnPointer : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Color hoverColor = Color.white;
        [SerializeField, ColorUsage(true, true)] private Color selectedColor = new Color(1f, 0.6f, 0.1f);
        [SerializeField] private LayerMask pickableLayers = ~0;

        private ObjectOutline hovered;
        private readonly HashSet<ObjectOutline> selected = new HashSet<ObjectOutline>();

        private void Awake()
        {
            if (viewCamera == null) viewCamera = GetComponent<Camera>();
        }

        private void Update()
        {
            if (viewCamera == null || !TryReadPointer(out Vector2 position, out bool clicked)) return;

            ObjectOutline target = null;
            if (Physics.Raycast(viewCamera.ScreenPointToRay(position), out RaycastHit hit, float.PositiveInfinity, pickableLayers))
            {
                target = hit.collider.GetComponentInParent<ObjectOutline>();
            }

            if (target != hovered)
            {
                ObjectOutline previous = hovered;
                hovered = target;
                if (previous != null) Apply(previous);
                if (hovered != null) Apply(hovered);
            }

            if (clicked && target != null)
            {
                if (!selected.Remove(target)) selected.Add(target);
                Apply(target);
            }
        }

        private void Apply(ObjectOutline outline)
        {
            bool isSelected = selected.Contains(outline);
            outline.Color = isSelected ? selectedColor : hoverColor;
            outline.enabled = isSelected || outline == hovered;
        }

        private static bool TryReadPointer(out Vector2 position, out bool clicked)
        {
#if ENABLE_INPUT_SYSTEM
            Pointer pointer = Pointer.current;
            if (pointer == null)
            {
                position = default;
                clicked = false;
                return false;
            }
            position = pointer.position.ReadValue();
            clicked = pointer.press.wasPressedThisFrame;
            return true;
#else
            position = Input.mousePosition;
            clicked = Input.GetMouseButtonDown(0);
            return true;
#endif
        }
    }
}
