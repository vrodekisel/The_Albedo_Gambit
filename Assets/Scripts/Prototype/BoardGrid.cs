using UnityEngine;

namespace AlbedoGambit.Prototype
{
    // Scene geometry only. BattleSession owns logical occupancy.
    public sealed class BoardGrid : MonoBehaviour
    {
        [SerializeField] private Transform[] cells = new Transform[64];
        [Tooltip("Cell indices (rank * 8 + file) excluded from placement. Missing cell transforms are holes.")]
        [SerializeField] private int[] blockedCells = new int[0];
        public Transform CellTransform(int index) => index >= 0 && index < 64 && index < cells.Length ? cells[index] : null;
        public ulong ActiveMask
        {
            get
            {
                ulong mask = 0;
                for (int i = 0; i < 64; i++) if (CellTransform(i) != null) mask |= 1UL << i;
                return mask;
            }
        }
        public ulong BlockedMask
        {
            get
            {
                ulong mask = 0;
                foreach (int cell in blockedCells) if (cell >= 0 && cell < 64) mask |= 1UL << cell;
                return mask;
            }
        }
        public void Configure(Transform[] orderedCells) { cells = orderedCells; }
        public Vector3 CellCenter(int file, int rank)
        {
            if (file < 0 || file >= 8 || rank < 0 || rank >= 8)
                throw new System.ArgumentOutOfRangeException("Cell coordinates must be 0..7.");
            var cell = CellTransform(rank * 8 + file);
            if (cell == null) throw new System.InvalidOperationException("The requested board cell is missing.");
            return cell.TransformPoint(Vector3.up * .5f);
        }
        public bool TryGetCell(Vector3 world, out Vector2Int coordinate)
        {
            for (int i = 0; i < Mathf.Min(64, cells.Length); i++)
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
