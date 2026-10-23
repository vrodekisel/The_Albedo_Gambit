using System;
using System.IO;
using AlbedoGambit.Prototype;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
public static class BattleHudValidation
{
    static BattleHudValidation() { EditorApplication.playModeStateChanged += OnPlayState; }
    public static void BuildAndPlay()
    {
        BattleHudSetup.Build();
        SessionState.SetBool("Albedo.HudValidation", true);
        EditorApplication.EnterPlaymode();
    }
    private static void OnPlayState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Albedo.HudValidation", false)) return;
        SessionState.SetBool("Albedo.HudValidation", false);
        EditorApplication.update += RunOnce;
    }
    private static void RunOnce()
    {
        EditorApplication.update -= RunOnce;
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void Run()
    {
        if (!Application.isPlaying) EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        var hud = UnityEngine.Object.FindFirstObjectByType<BattleHud>();
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        var board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
        Require(Application.isPlaying && EventSystem.current != null, "Run via BuildAndPlay for the live EventSystem");
        Require(hud != null && hud.party == party, "Serialized scene binding");
        Require(UnityEngine.Object.FindObjectsByType<BattlePrototypeController>(FindObjectsSortMode.None).Length == 0, "No enemy controller introduced");
        party.Initialize(); hud.Bind(party); hud.Bind(party);
        Require(!hud.content.activeSelf, "HUD hidden in exploration");
        Require(party.EnterChessMode(board), "Battle entry"); hud.Refresh();
        Require(hud.content.activeSelf, "HUD shown in battle");
        foreach (var hero in hud.heroes)
        {
            Require(hero.avatar != null && hero.portrait != null, "Portrait assets");
            foreach (var icon in hero.icons) Require(icon != null, "Ability assets");
        }
        for (int i = 0; i < 4; i++)
        {
            ExecuteEvents.Execute(hud.heroButtons[i].gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            Require(party.SelectedIndex == i && party.Battle.SelectedMember == i && hud.heroName.text == hud.heroes[i].name, "Correct hero mapping");
            Require(party.Battle.ActingMember == -1, "Inspection does not spend action");
            for (int j = 0; j < 3; j++) hud.abilityButtons[j].onClick.Invoke();
            Require(!party.Battle.ActionSpent && !party.Battle.MovementSpent, "Unavailable abilities do not mutate budget");
            hud.heroButtons[i].onClick.Invoke();
            Capture(hud, 1920, 1080, hud.heroes[i].name);
        }
        int turn = party.Battle.PlayerTurn;
        hud.endTurnButton.onClick.Invoke();
        Require(party.Battle.PlayerTurn == turn + 1, "End Turn wired once after repeated Bind");
        float saved = Time.timeScale;
        hud.menuButton.onClick.Invoke();
        Require(party.Battle.Phase == BattlePhase.Paused && Time.timeScale == 0 && hud.pauseOverlay.activeSelf, "Menu pauses");
        hud.endTurnButton.onClick.Invoke(); Require(party.Battle.PlayerTurn == turn + 1, "Pause rejects End Turn");
        Capture(hud, 1280, 720, "Pause");
        hud.resumeButton.onClick.Invoke();
        Require(party.Battle.Phase == BattlePhase.Battle && Time.timeScale == saved && !hud.pauseOverlay.activeSelf, "Resume restores simulation");
        party.SelectMember(0); hud.Refresh(); Capture(hud, 1280, 720, "Small"); Capture(hud, 2560, 1080, "Wide");
        party.ExitChessMode(); hud.Refresh(); Require(!hud.content.activeSelf, "Exit hides HUD");
        File.WriteAllText("Logs/BattleHudValidation.txt", "PASS: Serialized HUD and assets; hero mappings; inspection without actions; unavailable ability safety; End Turn and duplicate binding; pause/resume; exit.\nPASS: UI raycasts block panels and preserve the world centre at 1920x1080, 1280x720 and 2560x1080.\nNo enemies or movement/attack integration added.\n");
        Debug.Log("BATTLE_HUD_VALIDATED");
    }
    private static void Capture(BattleHud hud, int w, int h, string label)
    {
        var camera = Camera.main; var canvas = hud.GetComponent<Canvas>(); var scaler = hud.GetComponent<CanvasScaler>();
        var target = new RenderTexture(w, h, 24); var image = new Texture2D(w, h, TextureFormat.RGB24, false);
        var oldActive = RenderTexture.active; float aspect = camera.aspect;
        try
        {
            camera.targetTexture = target; camera.aspect = w / (float)h;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            // Camera target dimensions in batchmode do not change Screen.width/height.
            scaler.enabled = false; canvas.scaleFactor = Mathf.Sqrt(w / 2560f * h / 1440f);
            hud.Refresh(); Canvas.ForceUpdateCanvases(); camera.Render(); Canvas.ForceUpdateCanvases();
            camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, w, h), 0, 0); image.Apply();
            File.WriteAllBytes("Logs/BattleHud-" + label + ".png", image.EncodeToPNG());
            if (!hud.pauseOverlay.activeSelf)
            {
                var corner = hud.heroButtons[0].transform.TransformPoint(new Vector3(20, -20, 0));
                var point = RectTransformUtility.WorldToScreenPoint(camera, corner);
                Require(hud.PointerOverUI(point), "UI raycast blocks avatar at " + point + "; EventSystem=" + (EventSystem.current != null));
                Require(!hud.PointerOverUI(new Vector2(w / 2f, h / 2f)), "World centre remains clickable");
            }
            camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, w, h), 0, 0); image.Apply();
            File.WriteAllBytes("Logs/BattleHud-" + label + ".png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; camera.aspect = aspect; RenderTexture.active = oldActive;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; scaler.enabled = true;
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception("HUD validation: " + message); }
}
