using UnityEditor;
using UnityEngine;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>
    /// Listens to editor-wide object change events and automatically rebakes an
    /// <see cref="Outline"/> when the mesh on its MeshFilter is swapped, without requiring the
    /// inspector to be open. This is what makes outlines "just work" when meshes change.
    /// </summary>
    [InitializeOnLoad]
    internal static class OutlineMeshChangeWatcher
    {
        static OutlineMeshChangeWatcher()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            // Bakes mutate scene objects; leave play mode alone.
            if (Application.isPlaying) return;

            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    // Fired when a component's serialized properties change,
                    // e.g. a new mesh assigned to a MeshFilter.
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var data);
                        HandleChangedObject(EditorUtility.EntityIdToObject(data.entityId));
                        break;
                    }
                    // Fired when components are added/removed on a GameObject.
                    case ObjectChangeKind.ChangeGameObjectStructure:
                    {
                        stream.GetChangeGameObjectStructureEvent(i, out var data);
                        var go = EditorUtility.EntityIdToObject(data.entityId) as GameObject;
                        if (go != null && go.TryGetComponent(out Outline outline))
                        {
                            RebakeIfStale(outline);
                        }
                        break;
                    }
                }
            }
        }

        private static void HandleChangedObject(Object obj)
        {
            switch (obj)
            {
                case MeshFilter meshFilter when meshFilter.TryGetComponent(out Outline outline):
                    RebakeIfStale(outline);
                    break;
                case GameObject go when go.TryGetComponent(out Outline outline):
                    RebakeIfStale(outline);
                    break;
            }
        }

        private static void RebakeIfStale(Outline outline)
        {
            if (outline == null || !outline.IsCreated || !outline.IsBakeStale) return;
            outline.Recalculate();
            OutlineEditor.MarkDirty(outline);
        }
    }
}
