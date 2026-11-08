using UnityEngine;

namespace AlbedoGambit.Prototype
{
    // Only creates views after the entire eight-participant placement has been accepted.
    // Owns pooled views and movement playback; legal moves remain in BattleSession.
    [DisallowMultipleComponent]
    public sealed class BattleEnemySpawner : MonoBehaviour
    {
        [SerializeField] private BattleEnemyView enemyPrefab;
        [Tooltip("Opt-in scene milestone: mirrored roles, enemy first, fixed alternating queues.")]
        [SerializeField] private bool enableMovement;
        private float wait, moveElapsed;
        private int moving = -1;
        private Vector3 moveFrom, moveTo;
        private const float ThinkSeconds = .55f, MoveSeconds = .3f;
        public bool MovementEnabled => enableMovement;
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
            moving = -1; wait = ThinkSeconds;
            var party = GetComponent<PartyChainController>();
            if (enableMovement && gameObject.scene.name == "ChessRoomBlockout" && GetComponent<BattleAutoPlay>() == null)
                gameObject.AddComponent<BattleAutoPlay>();
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
                if (enableMovement)
                {
                    var visual = party.Members[(int)state.RoleOf(4 + i)].transform.Find("Capsule Visual");
                    instances[i].SetCap(visual.GetComponent<Renderer>().sharedMaterial);
                }
            }
            Physics.SyncTransforms();
        }
        public void TickMovement(BattleSession state, BoardGrid board, float dt)
        {
            if (!enableMovement || state.Phase != BattlePhase.Battle || state.Side != BattleSide.Enemy) return;
            var abilities = GetComponent<BattleAbilityController>();
            if (abilities != null && abilities.IsProjectileFlying) return;
            if (moving >= 0)
            {
                moveElapsed += Mathf.Max(0, dt);
                float t = Mathf.Clamp01(moveElapsed / MoveSeconds);
                Vector3 position = Vector3.Lerp(moveFrom, moveTo, Mathf.SmoothStep(0, 1, t));
                if (state.RoleOf(moving) == ChessRole.Knight) position += Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.8f;
                var view = instances[moving - 4];
                view.transform.position = position; view.GetComponent<Rigidbody>().position = position;
                Physics.SyncTransforms();
                if (t >= 1) { moving = -1; wait = .2f; }
                return;
            }
            wait -= Mathf.Max(0, dt);
            if (wait > 0) return;
            int actor = state.ActingMember;
            if (state.ActionSpent) { state.CompleteEnemyTurn(); wait = ThinkSeconds; return; }
            int attackTarget = ChooseAttackTarget(state, actor);
            if (attackTarget >= 0 && abilities != null && abilities.TryFireEnemy(actor, attackTarget))
            { wait = .2f; return; }
            int destination = state.MovementSpent ? -1 : BattleEnemyMovement.ChooseDestination(state, actor);
            if (destination >= 0 && state.TryMove(actor, destination))
            {
                moving = actor; moveElapsed = 0;
                moveFrom = instances[actor - 4].transform.position;
                moveTo = board.CellCenter(destination % 8, destination / 8) + Vector3.up * .015f;
            }
            else { state.CompleteEnemyTurn(); wait = ThinkSeconds; }
        }
        public void Hide()
        {
            moving = -1; wait = ThinkSeconds;
            foreach (var enemy in instances) if (enemy != null) enemy.gameObject.SetActive(false);
        }
        private void OnDisable() { Hide(); }
        public static int ChooseAttackTarget(BattleSession state, int actor)
        {
            if (state.Side != BattleSide.Enemy || actor != state.ActingMember) return -1;
            ulong targets = state.AttackTargets(actor);
            int origin = state.CellOf(actor), best = -1, distance = int.MaxValue;
            for (int hero = 0; hero < 4; hero++)
            {
                int cell = state.CellOf(hero);
                if (cell < 0 || (targets & (1UL << cell)) == 0) continue;
                int x = cell % 8 - origin % 8, y = cell / 8 - origin / 8;
                int score = x * x + y * y;
                if (score < distance) { best = cell; distance = score; }
            }
            return best;
        }
    }
}
