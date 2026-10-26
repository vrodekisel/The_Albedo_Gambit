using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AlbedoGambit.Prototype;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run in a validation copy: opening a scene here intentionally discards its unsaved in-memory edits.
public static class BattleFoundationValidation
{
    public static void Run()
    {
        var session = new BattleSession();
        Require(!session.TryBeginPlacement(ulong.MaxValue, 0, new[] { 2, 2, 4, 5 }), "Duplicate placement rejected");
        Require(!session.TryBeginPlacement(ulong.MaxValue ^ (1UL << 2), 0, new[] { 2, 3, 4, 5 }), "Hole rejected");
        Require(!session.TryBeginPlacement(ulong.MaxValue, 1UL << 2, new[] { 2, 3, 4, 5 }), "Obstacle rejected");
        Require(!session.TryBeginPlacement(ulong.MaxValue, 0, new[] { -1, 3, 4, 5 }), "Negative cell rejected");
        Require(!session.TryBeginPlacement(ulong.MaxValue, 0, new[] { 64, 3, 4, 5 }), "Out-of-range bit rejected");
        Require(session.Phase == BattlePhase.Exploration && session.CellOf(0) == -1, "Failures are atomic");
        var starts = new[] { 2, 3, 4, 63 };
        Require(session.TryBeginPlacement(ulong.MaxValue, 0, starts), "Valid placement");
        starts[0] = 10;
        Require(session.CellOf(0) == 2 && session.OccupantAt(63) == 3, "Inputs not aliased; H8 supported");
        Require(!session.TrySelect(1), "Selection blocked during placement");
        Require(session.CompletePlacement() && session.TrySelect(3), "Battle selection");
        Require(session.Pause() && !session.TrySelect(0) && !session.CompletePlacement(), "Paused commands blocked");
        Require(session.Resume() && session.SelectedMember == 3, "Pause preserves selection");
        Require(!session.TryBeginPlacement(ulong.MaxValue, 0, new[] { 0, 1, 2, 3 }) && session.CellOf(0) == 2,
            "Cannot reinitialize an active encounter");
        session.Reset();
        Require(session.CellOf(3) == -1 && session.OccupantAt(63) == -1 && session.SelectedMember == -1, "Reset clears occupancy");
        Require(session.BeginCutscene() && session.CancelCutscene(), "Cutscene cancellation");

        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        var board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
        party.Initialize();
        var savedTimeScale = Time.timeScale;
        try
        {
            party.ToggleGroup(); party.SelectMember(2);
            var positions = party.Members.Select(m => m.Body.position).ToArray();
            var cells = Enumerable.Range(0, 64).Select(board.CellTransform).ToArray();
            var missing = (Transform[])cells.Clone(); missing[23] = null; board.Configure(missing);
            Require(!party.EnterChessMode(board), "Scene rejects missing H3");
            for (int i = 0; i < 4; i++) Require(party.Members[i].Body.position == positions[i], "Failed placement does not teleport");
            board.Configure(cells);
            Require(party.EnterChessMode(board), "Scene enters battle");
            for (int i = 0; i < 4; i++)
                Require(Vector3.Distance(party.Members[i].transform.position, party.Members[i].Body.position) < .001f,
                    "Rendered transform matches physics placement");
            var view = party.GetComponent<BattleBoardView>();
            Require(view.SelectCell(47) && party.SelectedIndex == 3 && party.Battle.SelectedMember == 3, "Cell click selects D");
            Require(!view.SelectCell(12) && party.SelectedIndex == 3, "Empty click does not move or deselect");
            party.SelectMember(1);
            Require(party.Battle.SelectedMember == 1, "Keyboard selection synchronized");
            Time.timeScale = .75f;
            party.TogglePause(); party.SelectMember(0); party.ToggleGroup(); party.ExitChessMode();
            Require(Time.timeScale == 0 && party.SelectedIndex == 1 && party.Mode == PartyMode.Chess && !view.SelectCell(47), "Pause locks all battle input");
            party.TogglePause();
            Require(Time.timeScale == .75f && party.Battle.Phase == BattlePhase.Battle, "Restores previous time scale");
            party.ExitChessMode();
            Require(party.Mode == PartyMode.Separated && party.SelectedIndex == 2 && party.Battle.CellOf(0) == -1, "Exit restores exploration and clears model");
            for (int i = 0; i < 4; i++) Require(party.Members[i].Body.position == positions[i], "Exit restores positions");
            Require(party.BeginCutscene(), "Story hook begins cutscene");
            party.Tick(Vector3.right, true, .02f); party.ToggleGroup(); party.SelectMember(0);
            Require(party.SelectedIndex == 2 && party.Members.All(m => m.Body.isKinematic), "Cutscene locks party");
            party.CancelCutscene();
            Require(party.Members.All(m => !m.Body.isKinematic), "Cancelled cutscene restores physics");
            Require(party.BeginCutscene() && party.EnterChessMode(board), "Cutscene can enter battle via same API");
            party.TogglePause(); party.ResetParty();
            Require(Time.timeScale == .75f && party.Battle.Phase == BattlePhase.Exploration && party.Members.All(m => !m.Body.isKinematic), "Reset while paused restores world");
            party.TogglePause(); party.enabled = false;
            // Edit-mode disabling does not guarantee lifecycle callbacks, so call the lifecycle method explicitly.
            InvokeLifecycle(party, "OnDisable");
            Require(Time.timeScale == .75f, "Disabling controller releases pause");
        }
        finally { Time.timeScale = savedTimeScale; }

        JumpFollowRegression.Run();
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
        foreach (var member in party.Members) member.Body.interpolation = RigidbodyInterpolation.None;
        party.Initialize(); party.EnterChessMode(board); party.SelectMember(3);
        var simulation = Physics.simulationMode;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            Physics.Simulate(.02f);
        }
        finally { Physics.simulationMode = simulation; }
        InvokeLifecycle(party.GetComponent<BattleBoardView>(), "LateUpdate");
        Capture();
        File.WriteAllText("Logs/BattleFoundationValidation.txt",
            "PASS: Atomic placement, holes, blocked/invalid cells, H8, input ownership.\n" +
            "PASS: Placement/battle/pause/cutscene transitions and selection guards.\n" +
            "PASS: Cell selection, no movement on empty click, scene/model consistency.\n" +
            "PASS: Exploration restore, cutscene cancellation, pause/reset/disable time scale cleanup.\n" +
            "PASS: Existing jump and sandbox physics regression suites.\n" +
            "PREVIEW: Logs/BattleFoundationPreview.png\n");
        Debug.Log("BATTLE_FOUNDATION_VALIDATED");
    }

    private static void Capture()
    {
        var camera = Camera.main;
        var target = new RenderTexture(1280, 720, 24);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes("Logs/BattleFoundationPreview.png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void Require(bool condition, string label)
    { if (!condition) throw new Exception("Battle foundation: " + label); }

    private static void InvokeLifecycle(object instance, string method)
    { instance.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, null); }
}
