using UnityEngine;

namespace AlbedoGambit.Prototype
{
    // Coordinates only. No chess rules, mode switching, snapping or party placement.
    public sealed class BoardGrid : MonoBehaviour
    {
        [SerializeField] private Transform[] cells = new Transform[64];
        public void Configure(Transform[] orderedCells) { cells = orderedCells; }
        public Vector3 CellCenter(int file, int rank)
        {
            if (file < 0 || file >= 8 || rank < 0 || rank >= 8)
                throw new System.ArgumentOutOfRangeException("Cell coordinates must be 0..7.");
            var cell = cells[rank * 8 + file];
            return cell.TransformPoint(Vector3.up * .5f);
        }
        public bool TryGetCell(Vector3 world, out Vector2Int coordinate)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == null) continue;
                var local = cells[i].InverseTransformPoint(world);
                if (local.x >= -.5f && local.x < .5f && local.z >= -.5f && local.z < .5f)
                { coordinate = new Vector2Int(i % 8, i / 8); return true; }
            }
            coordinate = default;
            return false;
        }
    }
}
