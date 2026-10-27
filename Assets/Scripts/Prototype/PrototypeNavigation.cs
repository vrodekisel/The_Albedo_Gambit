using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace AlbedoGambit.Prototype
{
    // Navigation supplies a route; Rigidbody remains responsible for actual motion/collisions.
    // Rebuild after changing static level geometry at runtime.
    public sealed class PrototypeNavigation : MonoBehaviour
    {
        [SerializeField] private MeshCollider groundSurface;
        public void ConfigureGround(MeshCollider ground) { groundSurface = ground; }
        private NavMeshData data;
        private NavMeshDataInstance instance;
        private float groundElevation;
        public bool IsReady => instance.valid;

        public bool EnsureReady()
        {
            if (instance.valid) return true;
            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(transform.position, Vector3.one);
            groundElevation = float.PositiveInfinity;
            if (groundSurface && groundSurface.sharedMesh)
            {
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = groundSurface.sharedMesh, transform = groundSurface.transform.localToWorldMatrix, area = 0 });
                bounds.Encapsulate(groundSurface.bounds);
                groundElevation = groundSurface.bounds.min.y;
            }
            foreach (var box in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            {
                if (!box.enabled || box.isTrigger || box.attachedRigidbody != null) continue;
                sources.Add(new NavMeshBuildSource {
                    shape = NavMeshBuildSourceShape.Box, size = box.size,
                    transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center), area = 0 });
                bounds.Encapsulate(box.bounds);
                if (!groundSurface) groundElevation = Mathf.Min(groundElevation, box.bounds.max.y);
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
            if (!TryProjectGround(from, out var start, 2) ||
                !TryProjectGround(to, out var end)) return false;
            return NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete;
        }

        public bool TryProjectGround(Vector3 position, out Vector3 projected, float radius = .8f)
        {
            projected = default;
            if (!EnsureReady()) return false;
            // This prototype deliberately has one ground level plus the low board lip.
            // Query at floor height, not jump height: nearby disconnected cube roofs must
            // never become the walking destination of an airborne character.
            position.y = groundElevation;
            if (!NavMesh.SamplePosition(position, out var hit, radius, NavMesh.AllAreas) ||
                hit.position.y > groundElevation + .4f) return false;
            projected = hit.position;
            return true;
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
