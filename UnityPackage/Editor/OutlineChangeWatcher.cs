using UnityEditor;
using UnityEngine;

namespace reromanlee.MeshOutline.Editor
{
    /// <summary>
    /// Watches editor changes (inspector edits, hierarchy edits, undo, prefab updates, mesh
    /// assets edited in place) and asks the outlines they affect to re-validate, without any
    /// inspector having to be open.
    /// </summary>
    [InitializeOnLoad]
    internal static class OutlineChangeWatcher
    {
        static OutlineChangeWatcher()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            // Undo can restore any state, including an outline's baked parts.
            Undo.undoRedoPerformed += OutlineBakeValidator.RequestAll;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    // A mesh swapped, a renderer toggled, a layer changed, a mesh edited by a tool...
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var e);
                        Object changed = Resolve(e);
                        // Moving things around never affects outlines; skip it so dragging stays free.
                        if (changed is Transform) break;
                        RequestOwner(changed is Component component ? component.gameObject : changed as GameObject);
                        break;
                    }
                    // Components added or removed (a renderer, or an outline changing ownership).
                    case ObjectChangeKind.ChangeGameObjectStructure:
                    {
                        stream.GetChangeGameObjectStructureEvent(i, out var e);
                        RequestAncestors(Resolve(e) as GameObject);
                        break;
                    }
                    // Children added, removed or reordered.
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    {
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out var e);
                        RequestAncestors(Resolve(e) as GameObject);
                        break;
                    }
                    case ObjectChangeKind.ChangeGameObjectParent:
                    {
                        stream.GetChangeGameObjectParentEvent(i, out var e);
                        ResolveParents(e, out Object previous, out Object next);
                        RequestAncestors(previous as GameObject);
                        RequestAncestors(next as GameObject);
                        break;
                    }
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    {
                        stream.GetCreateGameObjectHierarchyEvent(i, out var e);
                        if (Resolve(e) is GameObject created && created.transform.parent != null)
                        {
                            RequestAncestors(created.transform.parent.gameObject);
                        }
                        break;
                    }
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        stream.GetDestroyGameObjectHierarchyEvent(i, out var e);
                        RequestAncestors(ResolveParent(e) as GameObject);
                        break;
                    }
                    case ObjectChangeKind.UpdatePrefabInstances:
                    {
                        stream.GetUpdatePrefabInstancesEvent(i, out var e);
                        RequestPrefabInstances(e);
                        break;
                    }
                    // A mesh asset edited in place by a tool (not reimported).
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                    {
                        stream.GetChangeAssetObjectPropertiesEvent(i, out var e);
                        if (Resolve(e) is Mesh) OutlineBakeValidator.RequestAll();
                        break;
                    }
                }
            }
        }

        /// <summary>The outline that owns this object's renderers: the nearest one above it.</summary>
        private static void RequestOwner(GameObject gameObject)
        {
            if (gameObject == null) return;
            OutlineBakeValidator.Request(gameObject.GetComponentInParent<ObjectOutline>(true));
        }

        /// <summary>Every outline above (and on) this object: structural changes can move ownership.</summary>
        private static void RequestAncestors(GameObject gameObject)
        {
            if (gameObject == null) return;
            foreach (ObjectOutline outline in gameObject.GetComponentsInParent<ObjectOutline>(true))
            {
                OutlineBakeValidator.Request(outline);
            }
        }

        private static void RequestPrefabInstances(UpdatePrefabInstancesEventArgs e)
        {
#if UNITY_6000_4_OR_NEWER
            foreach (var id in e.entityIds)
            {
                if (EditorUtility.EntityIdToObject(id) is GameObject root) RequestAll(root);
            }
#else
            foreach (int id in e.instanceIds)
            {
                if (EditorUtility.InstanceIDToObject(id) is GameObject root) RequestAll(root);
            }
#endif
        }

        private static void RequestAll(GameObject root)
        {
            foreach (ObjectOutline outline in root.GetComponentsInChildren<ObjectOutline>(true))
            {
                OutlineBakeValidator.Request(outline);
            }
            RequestAncestors(root);
        }

        // Object IDs moved from int instance IDs to EntityId in Unity 6000.4.
#if UNITY_6000_4_OR_NEWER
        private static Object Resolve(ChangeGameObjectOrComponentPropertiesEventArgs e) => EditorUtility.EntityIdToObject(e.entityId);
        private static Object Resolve(ChangeGameObjectStructureEventArgs e) => EditorUtility.EntityIdToObject(e.entityId);
        private static Object Resolve(ChangeGameObjectStructureHierarchyEventArgs e) => EditorUtility.EntityIdToObject(e.entityId);
        private static Object Resolve(CreateGameObjectHierarchyEventArgs e) => EditorUtility.EntityIdToObject(e.entityId);
        private static Object Resolve(ChangeAssetObjectPropertiesEventArgs e) => EditorUtility.EntityIdToObject(e.entityId);
        private static Object ResolveParent(DestroyGameObjectHierarchyEventArgs e) => EditorUtility.EntityIdToObject(e.parentEntityId);

        private static void ResolveParents(ChangeGameObjectParentEventArgs e, out Object previous, out Object next)
        {
            previous = EditorUtility.EntityIdToObject(e.previousParentEntityId);
            next = EditorUtility.EntityIdToObject(e.newParentEntityId);
        }
#else
#pragma warning disable CS0618 // Obsolete in 6000.3, where EntityIdToObject exists but the event args don't carry EntityIds yet.
        private static Object Resolve(ChangeGameObjectOrComponentPropertiesEventArgs e) => EditorUtility.InstanceIDToObject(e.instanceId);
        private static Object Resolve(ChangeGameObjectStructureEventArgs e) => EditorUtility.InstanceIDToObject(e.instanceId);
        private static Object Resolve(ChangeGameObjectStructureHierarchyEventArgs e) => EditorUtility.InstanceIDToObject(e.instanceId);
        private static Object Resolve(CreateGameObjectHierarchyEventArgs e) => EditorUtility.InstanceIDToObject(e.instanceId);
        private static Object Resolve(ChangeAssetObjectPropertiesEventArgs e) => EditorUtility.InstanceIDToObject(e.instanceId);
        private static Object ResolveParent(DestroyGameObjectHierarchyEventArgs e) => EditorUtility.InstanceIDToObject(e.parentInstanceId);

        private static void ResolveParents(ChangeGameObjectParentEventArgs e, out Object previous, out Object next)
        {
            previous = EditorUtility.InstanceIDToObject(e.previousParentInstanceId);
            next = EditorUtility.InstanceIDToObject(e.newParentInstanceId);
        }
#pragma warning restore CS0618
#endif
    }
}
