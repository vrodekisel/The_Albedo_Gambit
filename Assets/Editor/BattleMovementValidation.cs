using System;
using System.IO;
using System.Linq;
using AlbedoGambit.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BattleMovementValidation
{
    static BattleMovementValidation() { EditorApplication.playModeStateChanged += OnState; }
    public static void Play()
    {
        Rules();
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        SessionState.SetBool("Albedo.MoveTest", true); EditorApplication.EnterPlaymode();
    }
    private static void OnState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Albedo.MoveTest", false)) return;
        SessionState.SetBool("Albedo.MoveTest", false); EditorApplication.update += RunOnce;
    }
    private static void RunOnce()
    {
        EditorApplication.update -= RunOnce;
        try { Scene(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static void Check(bool result, string label) { if (!result) throw new Exception("Movement: " + label); }
    private static ulong Mask(params int[] cells) { ulong result = 0; foreach (int c in cells) result |= 1UL << c; return result; }
    private static BattleSession Fixture(int[] starts, ulong blocked = 0, ulong active = ulong.MaxValue)
    {
        var s = new BattleSession(); Check(s.TryBeginPlacement(active, blocked, starts) && s.CompletePlacement(), "Fixture placement"); return s;
    }
    private static void Rules()
    {
        var s = Fixture(new[] { 27, 0, 7, 63, 16, 54, 55, 56 });
        Check(s.MoveTargets(0) == Mask(18, 19, 20, 26, 28, 34, 35, 36), "Exact king destinations");
        Check(s.MoveTargets(1) == Mask(1, 2, 3, 4, 5, 6, 8), "Exact rook destinations/enemy blocks ray");
        Check(s.MoveTargets(2) == Mask(14, 21, 28, 35, 42, 49), "Exact bishop destinations");
        Check(s.MoveTargets(3) == Mask(46, 53), "Exact knight edge destinations, jumping participants");
        Check(!s.TryMove(0, 63) && !s.TryMove(0, -1) && !s.TryMove(0, 64) && s.ActingMember == -1 && !s.MovementSpent, "Invalid actions do not lock/spend");
        Check(s.TryMove(0, 28) && s.OccupantAt(27) == -1 && s.OccupantAt(28) == 0, "Occupancy transfer");
        Check(!s.TrySelect(1) && s.SelectedMember == 0 && s.MoveTargets(1) == 0 && !s.TryMove(0, 29), "One movement and locked selection");
        Check(s.EndPlayerTurn() && s.CompleteEnemyTurn() && s.TrySelect(1), "Next turn unlocks selection");
        s = Fixture(new[] { 8, 0, 7, 63, 32, 54, 55, 56 });
        Check((s.MoveTargets(1) & Mask(8, 16, 24, 32, 40)) == 0, "Ally blocks rook ray and occupied cells stay unavailable");
        s = Fixture(new[] { 8, 0, 7, 63, 32, 54, 55, 56 }, Mask(16, 53));
        Check((s.MoveTargets(1) & Mask(16, 24)) == 0 && s.MoveTargets(3) == Mask(46), "Blocked cells excluded and stop rays");
        s = Fixture(new[] { 8, 0, 7, 63, 32, 54, 55, 56 }, 0, ulong.MaxValue ^ Mask(16, 46));
        Check((s.MoveTargets(1) & Mask(16, 24)) == 0 && s.MoveTargets(3) == Mask(53), "Missing cells excluded and stop rays");
        s = Fixture(new[] { 27, 0, 7, 63, 28, 54, 55, 56 });
        Check(s.TryAttack(0, 28) && !s.TrySelect(1) && s.TryMove(0, 26), "First action also locks hero; attack-before-move preserved");
        Debug.Log("MOVEMENT_RULES_VALIDATED");
    }
    private static void Finish(PartyChainController p) { for (int i = 0; i < 20; i++) p.Tick(Vector3.zero, false, .02f); }
    private static void Scene()
    {
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        var board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
        var hud = UnityEngine.Object.FindFirstObjectByType<BattleHud>();
        party.Initialize(); hud.Bind(party); party.ResetParty();
        Check(!board.IsBattleVisible && board.GetComponentsInChildren<Renderer>().All(r => !r.enabled) &&
              board.GetComponentsInChildren<Collider>().All(c => !c.enabled), "Board hidden and nonphysical while exploring");
        var start = party.Members.Select(m => m.Body.position).ToArray();
        Check(party.EnterChessMode(board), "Scene battle entry");
        Check(board.IsBattleVisible && board.GetComponentsInChildren<Renderer>().All(r => r.enabled) &&
              board.GetComponentsInChildren<Collider>().All(c => c.enabled && c.isTrigger),
              "Battle board visible and all click colliders are nonphysical triggers");
        var view = party.GetComponent<BattleBoardView>(); view.RefreshHighlights(); hud.Refresh(); Canvas.ForceUpdateCanvases();
        Check(view.HighlightedMoves == party.Battle.MoveTargets(0) && view.HighlightedMoves != 0, "Highlights match model");
        var uiPoint = RectTransformUtility.WorldToScreenPoint(null, hud.heroButtons[0].transform.TransformPoint(new Vector3(35, -35, 0)));
        Check(!view.ClickScreen(uiPoint) && !party.Battle.MovementSpent, "UI click cannot move in world");
        Check(!view.ClickCell(16) && !view.ClickCell(63) && !party.Battle.MovementSpent, "Enemy/illegal cells do nothing");
        var point = Camera.main.WorldToScreenPoint(board.CellCenter(6, 1) + Vector3.up * .01f);
        Physics.Raycast(Camera.main.ScreenPointToRay(point), out var probe, 1000, ~0, QueryTriggerInteraction.Ignore);
        Check(view.ClickScreen(point) && party.Battle.CellOf(0) == 14 && party.IsBattleMoving, "Real camera ray click starts movement; hit=" + (probe.collider == null ? "none" : probe.collider.name) + "; point=" + point + "; UI=" + hud.PointerOverUI(point));
        hud.Refresh(); Check(!hud.heroButtons[1].interactable && !hud.endTurnButton.interactable, "Buttons lock during movement");
        Check(!party.EndPreviewTurn() && !view.ClickCell(50), "No commands during animation");
        party.TogglePause(); var pausedPosition = party.Members[0].Body.position; Finish(party);
        Check(party.Members[0].Body.position == pausedPosition, "Pause freezes animation"); party.TogglePause(); Finish(party);
        Check(!party.IsBattleMoving && Vector3.Distance(party.Members[0].Body.position, board.CellCenter(6, 1) + Vector3.up * .015f) < .001f, "Body and visual reach destination");
        party.SelectMember(1); hud.heroButtons[2].onClick.Invoke(); view.ClickCell(5);
        Check(party.SelectedIndex == 0 && hud.heroName.text == "Eric", "Keyboard, HUD and board cannot change locked hero");
        view.RefreshHighlights(); Check(view.HighlightedMoves == 0, "Spent movement clears targets");
        Check(party.EndPreviewTurn(), "End turn"); hud.Refresh(); Check(hud.heroButtons.All(b => b.interactable), "All cards unlock next turn");
        party.SelectMember(1); view.RefreshHighlights(); hud.Refresh();
        Capture(hud, "Movement-Rook");
        Check(view.ClickCell(38), "Rook command"); Finish(party); Check(party.EndPreviewTurn(), "Rook turn finishes");
        party.SelectMember(2); Check(!party.TryMoveSelected(38) && view.ClickCell(22), "Bishop occupied endpoint rejected, diagonal allowed"); Finish(party); party.EndPreviewTurn();
        party.SelectMember(3); Check(view.ClickCell(30), "Knight L command"); party.Tick(Vector3.zero, false, .1f);
        Check(party.Members[3].Body.position.y > board.CellCenter(6, 3).y + .5f, "Knight animated jump"); Finish(party);
        party.ExitChessMode();
        Check(!board.IsBattleVisible && board.GetComponentsInChildren<Renderer>().All(r => !r.enabled) &&
              board.GetComponentsInChildren<Collider>().All(c => !c.enabled), "Board hides again after battle");
        for (int i = 0; i < 4; i++) Check(Vector3.Distance(start[i], party.Members[i].Body.position) < .001f, "Exploration position restored after movement");
        view.RefreshHighlights(); Check(view.HighlightedMoves == 0, "No stale highlights outside battle");
        party.EnterChessMode(board); view.ClickCell(14); party.ResetParty(); Finish(party);
        Check(!board.IsBattleVisible, "Reset hides battle board");
        Check(!party.IsBattleMoving && party.Battle.Phase == BattlePhase.Exploration, "Reset cancels pending movement");
        party.EnterChessMode(board); party.SelectMember(3); hud.Refresh(); view.RefreshHighlights(); Capture(hud, "Movement-Knight");
        File.WriteAllText("Logs/BattleMovementValidation.txt", "PASS: Exact king/rook/bishop/knight masks; all participant ray blocking; enemies/blocked/missing cells; occupied endpoints; invalid commands.\nPASS: Camera ray click and UI exclusion; animation/pause; model-body sync; all selection paths locked after action; one move; End Turn unlock; enemy pass; exploration restoration and reset cleanup.\n");
        Debug.Log("BATTLE_MOVEMENT_VALIDATED");
    }
    private static void Capture(BattleHud hud, string label) => typeof(BattleHudValidation).GetMethod("Capture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { hud, 1920, 1080, label });
}
