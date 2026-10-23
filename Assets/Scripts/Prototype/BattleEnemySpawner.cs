using UnityEngine;

namespace AlbedoGambit.Prototype
{
    // Only creates views after the entire eight-participant placement has been accepted.
    // No AI, turn processing or damage belongs to this component.
    [DisallowMultipleComponent]
    public sealed class BattleEnemySpawner : MonoBehaviour
    {
        [SerializeField] private BattleEnemyView enemyPrefab;
        private readonly BattleEnemyView[] instances = new BattleEnemyView[4];
        public bool IsConfigured => enemyPrefab != null;
        public BattleEnemyView EnemyAt(int index) => instances[index];
        public int VisibleCount
        {
            get { int count = 0; foreach (var enemy in instances) if (enemy != null && enemy.gameObject.activeInHierarchy) count++; return count; }
        }
        public void Configure(BattleEnemyView prefab) { enemyPrefab = prefab; }
        public void Show(BattleSession state, BoardGrid board)
        {
            if (!IsConfigured || state.Phase != BattlePhase.Battle || state.ParticipantCount != 8) return;
            for (int i = 0; i < 4; i++)
            {
                if (instances[i] == null)
                {
                    instances[i] = Instantiate(enemyPrefab, transform);
                    instances[i].name = "Enemy " + (i + 1);
                    instances[i].Bind(4 + i);
                }
                int cell = state.CellOf(4 + i);
                var position = board.CellCenter(cell % 8, cell / 8) + Vector3.up * .015f;
                instances[i].transform.SetPositionAndRotation(position, Quaternion.identity);
                var body = instances[i].GetComponent<Rigidbody>();
                body.position = position; body.rotation = Quaternion.identity;
                instances[i].gameObject.SetActive(true);
            }
            Physics.SyncTransforms();
        }
        public void Hide() { foreach (var enemy in instances) if (enemy != null) enemy.gameObject.SetActive(false); }
        private void OnDisable() { Hide(); }
    }
}
