using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace AlbedoGambit.Prototype
{
    // Navigation supplies a route; Rigidbody remains responsible for actual motion/collisions.
    // Rebuild after changing static level geometry at runtime.
    public sealed class PrototypeNavigation : MonoBehaviour
    {
        private NavMeshData data;
        private NavMeshDataInstance instance;
        public bool IsReady => instance.valid;

        public bool EnsureReady()
        {
            if (instance.valid) return true;
            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(transform.position, Vector3.one);
            foreach (var box in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            {
                if (!box.enabled || box.isTrigger || box.attachedRigidbody != null) continue;
                sources.Add(new NavMeshBuildSource {
                    shape = NavMeshBuildSourceShape.Box, size = box.size,
                    transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center), area = 0 });
                bounds.Encapsulate(box.bounds);
            }
            if (sources.Count == 0) return false;
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = .43f;
            settings.agentHeight = 1.6f;
            settings.agentClimb = .3f;
            settings.agentSlope = 50;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .08f;
            bounds.Expand(2);
            data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) return false;
            instance = NavMesh.AddNavMeshData(data);
            return instance.valid;
        }

        public bool FindPath(Vector3 from, Vector3 to, NavMeshPath path)
        {
            if (!EnsureReady()) return false;
            if (!NavMesh.SamplePosition(from, out var start, 2, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to, out var end, .8f, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete;
        }

        public void Rebuild() { Release(); EnsureReady(); }
        private void OnDisable() { Release(); }
        private void Release()
        {
            if (instance.valid) instance.Remove();
            if (data != null) DestroyImmediate(data);
            data = null;
        }
    }
}
