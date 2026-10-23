using System;
using System.IO;
using AlbedoGambit.Prototype;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BattleEnemyValidation
{
    static BattleEnemyValidation() { EditorApplication.playModeStateChanged += OnState; }
    public static void BuildAndPlay()
    {
        BattleEnemySetup.Build(); SessionState.SetBool("Albedo.EnemyTest", true); EditorApplication.EnterPlaymode();
    }
    private static void OnState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Albedo.EnemyTest", false)) return;
        SessionState.SetBool("Albedo.EnemyTest", false); EditorApplication.update += RunOnce;
    }
    private static void RunOnce()
    {
        EditorApplication.update -= RunOnce;
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static void Require(bool pass, string message) { if (!pass) throw new Exception("Enemy spawn: " + message); }
    private static void Run()
    {
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        var board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
        var spawner = party.GetComponent<BattleEnemySpawner>();
        var hud = UnityEngine.Object.FindFirstObjectByType<BattleHud>();
        party.Initialize(); hud.Bind(party);
        Require(spawner != null && spawner.VisibleCount == 0, "No enemies in exploration");
        Require(Vector3.Distance(party.Members[0].transform.position, party.Members[2].transform.position) < Vector3.Distance(party.Members[0].transform.position, party.Members[1].transform.position), "Green starts second");
        var cells = new Transform[64]; for (int i = 0; i < 64; i++) cells[i] = board.CellTransform(i);
        var missing = (Transform[])cells.Clone(); missing[58] = null; board.Configure(missing);
        Require(!party.EnterChessMode(board) && spawner.VisibleCount == 0 && party.Battle.ParticipantCount == 0, "Invalid enemy cell rejects entire encounter");
        board.Configure(cells);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Require(party.EnterChessMode(board), "Enter battle"); hud.Refresh();
            Require(spawner.VisibleCount == 4 && party.Battle.ParticipantCount == 8, "Exactly four registered opponents");
            Require(party.Battle.CellOf(2) == 3 && party.Battle.CellOf(1) == 4, "Green second, red third on board");
            for (int i = 0; i < 4; i++)
            {
                var enemy = spawner.EnemyAt(i);
                Require(enemy.Participant == i + 4 && party.Battle.OccupantAt(58 + i) == i + 4, "View and occupancy agree");
                Require(Vector3.Distance(enemy.transform.position, board.CellCenter(2 + i, 7) + Vector3.up * .015f) < .001f, "Enemy position");
                Require(enemy.GetComponent<Rigidbody>().isKinematic && enemy.GetComponent<Collider>().enabled, "Stable enemy physics");
            }
            Require(!party.EnterChessMode(board), "Repeat entry does not duplicate");
            spawner.Show(party.Battle, board); Require(spawner.GetComponentsInChildren<BattleEnemyView>(true).Length == 4, "Pool reuses four instances");
            hud.heroButtons[2].onClick.Invoke(); Require(hud.heroName.text == "Joseph", "Green card identity retained");
            hud.heroButtons[1].onClick.Invoke(); Require(hud.heroName.text == "Mia", "Red card identity retained");
            int turn = party.Battle.PlayerTurn; hud.endTurnButton.onClick.Invoke();
            Require(party.Battle.PlayerTurn == turn + 1 && party.Battle.Side == BattleSide.Player, "Spawn-only enemy pass does not stall");
            party.TogglePause(); Require(spawner.VisibleCount == 4, "Pause retains enemies"); party.TogglePause();
            if (cycle == 1) party.ResetParty(); else party.ExitChessMode();
            Require(spawner.VisibleCount == 0 && party.Battle.ParticipantCount == 0, "Exit/reset clears encounter");
        }
        // Exercise the actual follow/jump simulation after the reordered starting positions.
        var previous = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
        try
        {
            foreach (var motor in party.Members) motor.Body.interpolation = RigidbodyInterpolation.None;
            party.ResetParty();
            for (int f = 0; f < 50; f++) { party.Tick(Vector3.zero, false, .02f); Physics.Simulate(.02f); }
            int[] counts = new int[4]; for (int i = 0; i < 4; i++) counts[i] = party.Members[i].JumpCount;
            int[] jumpFrame = { -1, -1, -1, -1 };
            for (int f = 0; f < 300; f++)
            {
                party.Tick(f < 100 ? Vector3.right : Vector3.zero, f == 5, .02f); Physics.Simulate(.02f);
                for (int i = 0; i < 4; i++) if (jumpFrame[i] < 0 && party.Members[i].JumpCount > counts[i]) jumpFrame[i] = f;
            }
            Require(jumpFrame[0] >= 0 && jumpFrame[0] < jumpFrame[2] && jumpFrame[2] < jumpFrame[1] && jumpFrame[1] < jumpFrame[3], "Jumps A-C-B-D");
            for (int slot = 1; slot < 4; slot++)
            {
                var ahead = party.Members[PartyChainController.ActorAtSlot(slot - 1)].Body.position;
                var follower = party.Members[PartyChainController.ActorAtSlot(slot)].Body.position;
                Require(ahead.x - follower.x > .7f && ahead.x - follower.x < 1.5f, "Chain spacing A-C-B-D");
            }
        }
        finally { Physics.simulationMode = previous; }
        party.ResetParty(); party.EnterChessMode(board); hud.Refresh();
        typeof(BattleHudValidation).GetMethod("Capture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { hud, 1920, 1080, "Enemies" });
        File.WriteAllText("Logs/BattleEnemyValidation.txt", "PASS: A-C-B-D starting positions, following and sequential jumping; stable hero/card identity.\nPASS: Atomic 8-participant placement; four kinematic enemy views and occupancy; repeat entry/pool reuse; pause; exit and reset; placeholder enemy pass.\nNo AI/movement/attack integration enabled.\n");
        Debug.Log("ENEMY_SPAWN_VALIDATED");
    }
}
