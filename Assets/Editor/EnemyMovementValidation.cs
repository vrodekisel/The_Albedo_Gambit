using System;
using System.IO;
using System.Linq;
using AlbedoGambit.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class EnemyMovementValidation
{
    static EnemyMovementValidation() { EditorApplication.playModeStateChanged += OnState; }
    public static void Play()
    {
        Rules();
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        SessionState.SetBool("Albedo.EnemyMovementTest", true);
        EditorApplication.EnterPlaymode();
    }
    private static void OnState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Albedo.EnemyMovementTest", false)) return;
        SessionState.SetBool("Albedo.EnemyMovementTest", false);
        EditorApplication.update += Run;
    }
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Enemy movement: " + message); }
    private static ulong Mask(params int[] cells)
    { ulong mask = 0; foreach (int c in cells) mask |= 1UL << c; return mask; }
    private static BattleSession Fixture(ulong blocked = 0, ulong active = ulong.MaxValue)
    {
        var state = new BattleSession();
        Check(state.TryBeginPlacement(active, blocked, new[] { 8, 16, 24, 32, 27, 7, 0, 63 }, true), "Placement");
        Check(state.CompletePlacement(), "Placement completed"); return state;
    }
    private static void NextEnemy(BattleSession state) { state.CompleteEnemyTurn(); state.EndPlayerTurn(); }
    private static void Rules()
    {
        var state = Fixture();
        Check(state.Side == BattleSide.Enemy && state.ActingMember == 4, "Enemy starts");
        Check(state.MoveTargets(4) == Mask(10, 12, 17, 21, 33, 37, 42, 44), "Knight exact legal cells");
        Check(!state.TryMove(0, 9) && !state.TrySelect(0) && !state.EndPlayerTurn(), "Player commands rejected during enemy turn");
        NextEnemy(state);
        Check(state.MoveTargets(5) == Mask(14, 21, 28, 35, 42, 49, 56), "Bishop exact legal cells");
        NextEnemy(state);
        Check(state.MoveTargets(6) == Mask(1, 2, 3, 4, 5, 6), "Rook stopped by both teams");
        NextEnemy(state);
        Check(state.MoveTargets(7) == Mask(54, 55, 62), "King exact legal cells");
        state = Fixture(Mask(10, 19, 20), ulong.MaxValue ^ Mask(12));
        Check(state.MoveTargets(4) == Mask(17, 21, 33, 37, 42, 44), "Knight jumps intermediate blocked cells; rejects blocked/missing landing");
        state = Fixture(Mask(3, 21)); NextEnemy(state);
        Check(state.MoveTargets(5) == Mask(14), "Obstacle stops bishop ray"); NextEnemy(state);
        Check(state.MoveTargets(6) == Mask(1, 2), "Obstacle stops rook ray");
        state = Fixture(0, ulong.MaxValue ^ Mask(3, 21)); NextEnemy(state);
        Check(state.MoveTargets(5) == Mask(14), "Missing cell stops bishop ray"); NextEnemy(state);
        Check(state.MoveTargets(6) == Mask(1, 2), "Missing cell stops rook ray");
        state = Fixture(Mask(10, 12, 17, 21, 33, 37, 42, 44));
        Check(BattleEnemyMovement.ChooseDestination(state, 4) == -1, "Surrounded knight passes");
        state = Fixture();
        int target = BattleEnemyMovement.ChooseDestination(state, 4);
        Check(target == 17, "Move closes distance to nearest hero with deterministic tie break");
        Check(state.TryMove(4, target) && !state.TryMove(4, 27), "Only one movement");
        Check(state.OccupantAt(27) == -1 && state.OccupantAt(target) == 4, "Atomic occupancy transfer");
        int[] order = { 0, 1, 2, 3 };
        for (int i = 0; i < 12; i++)
        {
            Check(state.ActingMember == 4 + i % 4, "Enemy rotation");
            state.CompleteEnemyTurn();
            Check(state.SelectedMember == order[i % 4], "Hero rotation");
            Check(!state.TrySelect((state.SelectedMember + 1) % 4), "Cannot change queued hero before acting");
            Check(!state.TryMove((state.SelectedMember + 1) % 4, 9), "Cannot bypass queue with command");
            state.EndPlayerTurn();
        }
        var legacy = new BattleSession();
        legacy.TryBeginPlacement(ulong.MaxValue, 0, new[] { 23, 39, 31, 47, 16, 24, 32, 40 }); legacy.CompletePlacement();
        Check(legacy.Side == BattleSide.Player && legacy.TrySelect(2) && legacy.RoleOf(4) == ChessRole.King,
            "Non-opted-in encounters retain old behavior");
        Debug.Log("ENEMY_MOVEMENT_RULES_PASS");
    }
    private static void Tick(PartyChainController party, int frames)
    { for (int i = 0; i < frames; i++) party.Tick(Vector3.zero, false, .02f); }
    private static void Run()
    {
        EditorApplication.update -= Run;
        try { Scene(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static void Scene()
    {
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        var board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
        var hud = UnityEngine.Object.FindFirstObjectByType<BattleHud>();
        var spawner = party.GetComponent<BattleEnemySpawner>();
        party.Initialize(); hud.Bind(party); party.ResetParty();
        Check(spawner.MovementEnabled && party.EnterChessMode(board), "Scene opted in and starts");
        var state = party.Battle;
        hud.Refresh();
        Check(state.CellOf(1) == 31 && state.CellOf(2) == 39, "Mia is before Joseph on the board");
        Check(party.CurrentFormationSlot(1) == 1 && party.CurrentFormationSlot(2) == 2, "Party follows A-B-C-D");
        Check(hud.heroButtons[1].transform.position.x < hud.heroButtons[2].transform.position.x,
            "Mia card is before Joseph card");
        Check(state.Side == BattleSide.Enemy && !hud.endTurnButton.interactable && hud.heroButtons.All(b => !b.interactable), "Enemy starts with blocked player UI");
        for (int i = 0; i < 4; i++)
        {
            var cap = spawner.EnemyAt(i).transform.Find("Role Cap");
            var source = party.Members[(int)state.RoleOf(i + 4)].transform.Find("Capsule Visual");
            Check(cap != null && cap.GetComponent<Renderer>().sharedMaterial == source.GetComponent<Renderer>().sharedMaterial, "Cap matches hero material");
            var mesh = cap.GetComponent<MeshFilter>().sharedMesh;
            Check(mesh.triangles.Length > 0 && mesh.triangles.All(v => mesh.vertices[v].y >= .499f), "Only top hemisphere colored");
        }
        Capture(hud, "EnemyMovement-Start");
        Tick(party, 28); // Thinking ends; logical movement is now committed.
        Tick(party, 7); // Halfway through the knight jump.
        var jumping = spawner.EnemyAt(0);
        Check(jumping.transform.position.y > board.CellCenter(0, 2).y + 1, "Knight visibly jumps");
        var position = jumping.transform.position; int cell = state.CellOf(4);
        party.TogglePause(); Tick(party, 50);
        Check(jumping.transform.position == position && state.CellOf(4) == cell, "Pause freezes animation and logical state");
        party.TogglePause(); Tick(party, 50);
        Check(state.Side == BattleSide.Player && party.SelectedIndex == 0 && state.PlayerTurn == 1, "First hero after initial enemy move");
        int[] order = { 0, 1, 2, 3 };
        for (int turn = 0; turn < 16; turn++)
        {
            Check(party.SelectedIndex == order[turn % 4], "Scene hero queue");
            if (turn < 4 && party.AvailableMoves != 0)
            {
                int destination = Enumerable.Range(0, 64).First(c => (party.AvailableMoves & (1UL << c)) != 0);
                Check(party.TryMoveSelected(destination) && !party.EndPreviewTurn(), "Queued hero moves; cannot end mid-animation");
                Tick(party, 20);
                Check(state.Side == BattleSide.Player && !party.TryMoveSelected(destination), "Player retains turn after one movement");
            }
            int[] before = Enumerable.Range(0, 8).Select(state.CellOf).ToArray();
            hud.endTurnButton.onClick.Invoke();
            int actor = state.ActingMember;
            Check(actor == 4 + (turn + 1) % 4 && !party.EndPreviewTurn(), "One enemy and no duplicate End Turn");
            ulong legal = state.MoveTargets(actor);
            Tick(party, 80);
            Check(state.Side == BattleSide.Player, "Enemy turn finishes");
            Check(legal == 0 ? state.CellOf(actor) == before[actor] : (legal & (1UL << state.CellOf(actor))) != 0, "Move belongs to pre-move legal set");
            for (int i = 0; i < 8; i++)
            {
                Check(state.OccupantAt(state.CellOf(i)) == i && state.HealthOf(i) == state.MaxHealth, "Occupancy consistent and no attacks");
                if (i != actor) Check(state.CellOf(i) == before[i], "Other participants stay still");
            }
            for (int i = 0; i < 4; i++)
            {
                int c = state.CellOf(i + 4);
                Check(Vector3.Distance(spawner.EnemyAt(i).transform.position, board.CellCenter(c % 8, c / 8) + Vector3.up * .015f) < .001f, "Model and visual match");
            }
        }
        Capture(hud, "EnemyMovement-After");
        // Exit during animation, re-enter, reset during thinking, and reuse the pool.
        party.EndPreviewTurn(); Tick(party, 30); party.ExitChessMode();
        Check(spawner.VisibleCount == 0 && state.ParticipantCount == 0, "Exit cancels enemy playback");
        Check(party.EnterChessMode(board), "Re-enter"); party.ResetParty(); Tick(party, 70);
        Check(spawner.VisibleCount == 0 && state.Phase == BattlePhase.Exploration, "Reset cancels pending turn");
        Check(party.EnterChessMode(board), "Enter after reset");
        Check(state.ActingMember == 4 && spawner.GetComponentsInChildren<BattleEnemyView>(true).Length == 4, "Queue resets and pool reuses views");
        party.ExitChessMode();
        // A board consisting only of the eight start cells guarantees no legal knight move.
        var cells = Enumerable.Range(0, 64).Select(board.CellTransform).ToArray();
        int[] starts = { 23, 31, 39, 47, 16, 24, 32, 40 };
        board.Configure(cells.Select((c, i) => starts.Contains(i) ? c : null).ToArray());
        Check(party.EnterChessMode(board), "Blocked fixture enters"); Tick(party, 80);
        Check(state.Side == BattleSide.Player && state.CellOf(4) == 16, "No legal move automatically passes in scene");
        party.ExitChessMode(); board.Configure(cells);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/EnemyMovementValidation.txt", "PASS: exact movement geometry, blockers and holes, occupancy, deterministic approach, both queues, enemy first, command/UI locks, 16 enemy turns without damage, knight animation, pause, exit/reset/re-entry, cap geometry/materials, blocked turn pass, opt-out compatibility.\nOnly ChessRoomBlockout tested; other scenes not migrated.\n");
        Debug.Log("ENEMY_MOVEMENT_SCENE_PASS");
    }
    private static void Capture(BattleHud hud, string name)
    {
        typeof(BattleHudValidation).GetMethod("Capture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { hud, 1920, 1080, name });
    }
}
