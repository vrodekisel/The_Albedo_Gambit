using UnityEngine;

namespace AlbedoGambit.Prototype
{
    // Temporary room-only test driver. All actions use the normal presentation commands.
    public sealed class BattleAutoPlay : MonoBehaviour
    {
        private PartyChainController party;
        private int mode, steps;
        private float wait;
        private string notice = "";
        private bool knockoutTest;

        private void Awake() { party = GetComponent<PartyChainController>(); }
        private void Update()
        {
            if (party == null) return;
            var s = party.Battle;
            if (s.Phase == BattlePhase.Paused) return;
            if (s.Phase != BattlePhase.Battle) { mode = 0; knockoutTest = false; notice = ""; return; }
            if ((mode == 0 && !knockoutTest) || party.IsBattleMoving) return;
            bool heroes = false, enemies = false;
            for (int i = 0; i < s.ParticipantCount; i++)
                if (s.HealthOf(i) > 0) { if (i < 4) heroes = true; else enemies = true; }
            if (!heroes || !enemies || s.Result != BattleResult.Ongoing)
            {
                mode = 0;
                knockoutTest = false;
                notice = !enemies ? "All enemies down" : "All heroes down";
                return;
            }
            if (mode == 0) return;
            if (s.Side != BattleSide.Player) return;
            wait -= Time.deltaTime;
            if (wait > 0) return;
            wait = .6f;
            if (++steps > 600) { mode = 0; notice = "Stopped: turn limit (possible stalemate)"; return; }
            int actor = s.SelectedMember;
            if (mode == 2 || !s.CanAct(actor)) { party.EndPreviewTurn(); return; }
            int target = First(s.AttackTargets(actor));
            if (target >= 0 && party.BeginAbility(0) && party.TryUseAbilityOn(target)) return;
            ulong moves = s.MoveTargets(actor);
            int best = -1, score = int.MaxValue;
            for (int cell = 0; cell < 64; cell++)
            {
                if ((moves & (1UL << cell)) == 0) continue;
                for (int enemy = 4; enemy < s.ParticipantCount; enemy++)
                {
                    if (s.IsKnockedOut(enemy)) continue;
                    int other = s.CellOf(enemy);
                    int dx = cell % 8 - other % 8, dy = cell / 8 - other / 8;
                    int distance = dx * dx + dy * dy;
                    if (distance < score) { score = distance; best = cell; }
                }
            }
            if (best >= 0 && party.TryMoveSelected(best)) return;
            party.EndPreviewTurn();
        }

        private static int First(ulong cells)
        { for (int i = 0; i < 64; i++) if ((cells & (1UL << i)) != 0) return i; return -1; }

        private void OnGUI()
        {
            if (party == null || party.Battle.Phase != BattlePhase.Battle && party.Battle.Phase != BattlePhase.Paused) return;
            GUILayout.BeginArea(new Rect(12, 12, 290, 250), GUI.skin.box);
            GUILayout.Label("TEMP: battle testing");
            if (GUILayout.Button(mode == 1 ? "Stop auto battle" : "Auto battle")) Toggle(1);
            if (GUILayout.Button(mode == 2 ? "Stop skipping" : "Skip hero turns (defeat test)")) Toggle(2);
#if UNITY_EDITOR
            GUI.enabled = party.Battle.Phase == BattlePhase.Battle && !party.IsBattleMoving;
            if (GUILayout.Button("Load last enemy KO test (resets battle)")) LoadKnockoutTest();
            GUI.enabled = true;
#endif
            var enemies = GetComponent<BattleEnemySpawner>();
            if (enemies != null)
            {
                GUILayout.Label("Enemy second choice: " + Mathf.RoundToInt(enemies.RoomSecondChoiceChance * 100) + "%");
                enemies.RoomSecondChoiceChance = GUILayout.HorizontalSlider(enemies.RoomSecondChoiceChance, 0f, 1f);
            }
            GUILayout.Label(notice);
            GUILayout.EndArea();
        }

        private void Toggle(int value)
        { mode = mode == value ? 0 : value; steps = 0; wait = .6f; notice = mode == 0 ? "Stopped" : "Running"; }

#if UNITY_EDITOR
        private void LoadKnockoutTest()
        {
            var board = Object.FindFirstObjectByType<BoardGrid>();
            if (board == null) return;
            mode = 0; knockoutTest = false;
            party.ExitChessMode();
            if (!party.EnterChessMode(board) || !party.Battle.PrepareLastEnemyKnockoutTest())
            { notice = "Fixture unavailable: check G4 / H4"; return; }
            GetComponent<BattleEnemySpawner>().Show(party.Battle, board);
            party.SelectMember(1);
            knockoutTest = true;
            notice = "Mia: use an ability on enemy at G4 (30 HP)";
        }
#endif
    }
}
