using System;
using System.IO;
using System.Linq;
using AlbedoGambit.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class Level02SceneValidation
{
    static Level02SceneValidation() { EditorApplication.playModeStateChanged += OnState; }
    public static void Play()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Level02Prototype.unity");
        Require(PrefabUtility.IsPartOfPrefabInstance(UnityEngine.Object.FindFirstObjectByType<PartyChainController>().gameObject) &&
            PrefabUtility.IsPartOfPrefabInstance(UnityEngine.Object.FindFirstObjectByType<BoardGrid>().gameObject) &&
            PrefabUtility.IsPartOfPrefabInstance(UnityEngine.Object.FindFirstObjectByType<BattleHud>().gameObject),
            "Party, board and HUD remain prefab instances in the saved scene");
        SessionState.SetBool("Albedo.Level02Test", true);
        EditorApplication.EnterPlaymode();
    }
    private static void OnState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Albedo.Level02Test", false)) return;
        SessionState.SetBool("Albedo.Level02Test", false);
        EditorApplication.update += RunOnce;
    }
    private static void RunOnce()
    {
        EditorApplication.update -= RunOnce;
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception("Level 02: " + message); }

    private static void Run()
    {
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        Camera.main.aspect = 16f / 9f;
        var board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
        var hud = UnityEngine.Object.FindFirstObjectByType<BattleHud>();
        var nav = UnityEngine.Object.FindFirstObjectByType<PrototypeNavigation>();
        var art = GameObject.Find("Level 02 Art");
        var proxies = GameObject.Find("Level 02 Collision Proxies");
        Require(party && board && hud && nav && art && proxies, "All reusable gameplay parts and imported art exist");
        int visualCount = art.GetComponentsInChildren<MeshRenderer>(true).Length;
        int proxyCount = proxies.GetComponentsInChildren<BoxCollider>(true).Length;
        Require(visualCount >= 40 && proxyCount > 20 && GameObject.Find("Floor Perimeter - Invisible Walls").GetComponentsInChildren<BoxCollider>().Length >= 90,
            "Visible model and obstacle proxies imported; visual=" + visualCount + " proxies=" + proxyCount);
        var ground = art.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name == "Ground");
        Require(ground.sharedMaterial != null && ground.sharedMaterial.GetTexture("_BaseMap") != null,
            "Painted ground texture assigned");
        Require(nav.EnsureReady(), "Navigation builds across the actual floor mesh");
        var follow = Camera.main.GetComponent<BoundedFollowCamera>();
        Require(follow && Vector3.Dot(Camera.main.transform.forward, new Vector3(1, -1, -1).normalized) > .99999f,
            "Camera angle matches the Blender camera, including the correct side of the map");
        Require(Mathf.Abs(Camera.main.orthographicSize - 8.4375f) < .0001f, "Blender gameplay framing at 16:9");
        int offRoadPoints = 0;
        for (int x = -40; x <= 40; x += 5)
            for (int z = 25; z <= 55; z += 5)
                if (nav.TryProjectGround(new Vector3(x, 0, z), out _, .5f)) offRoadPoints++;
        Require(offRoadPoints >= 30, "Large walkable grass area beyond the former road strip; samples=" + offRoadPoints);
        ValidateEdges(party, ground.GetComponent<MeshCollider>());

        var before = party.Members[0].Body.position;
        var prior = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            foreach (var motor in party.Members) motor.Body.interpolation = RigidbodyInterpolation.None;
            for (int i = 0; i < 60; i++) { party.Tick(Vector3.right, false, .02f); Physics.Simulate(.02f); }
            Require(party.Members[0].Body.position.x > before.x + 2 && party.Mode == PartyMode.Chained,
                "Free movement works on imported ground");
        }
        finally { Physics.simulationMode = prior; }
        party.ResetParty(); hud.Bind(party);
        Require(party.EnterChessMode(board), "Existing prefab command starts battle on second scene");
        Require(party.GetComponent<BattleEnemySpawner>().VisibleCount == 4 && hud.IsShowingBattle,
            "Four enemies and shared HUD appear");
        Require(party.Battle.CellOf(0) == 23 && party.Battle.CellOf(4) == 16,
            "Board coordinates remain consistent");
        follow.Follow(.02f, true);
        var hero = Camera.main.WorldToViewportPoint(board.CellCenter(7, 3));
        var enemy = Camera.main.WorldToViewportPoint(board.CellCenter(0, 3));
        Require(hero.x > enemy.x && hero.y < enemy.y, "Heroes lower right and enemies upper left on screen");
        Require(party.GetComponent<BattleBoardView>().ClickCell(14), "A moves by the existing board command");
        for (int i = 0; i < 20; i++) party.Tick(Vector3.zero, false, .02f);
        Require(party.Battle.CellOf(0) == 14 && !party.IsBattleMoving, "Battle movement finishes");
        Capture("Level02Preview");
        party.ExitChessMode();
        Require(party.Mode == PartyMode.Chained && party.GetComponent<BattleEnemySpawner>().VisibleCount == 0,
            "Leaving battle restores exploration");
        var start = party.Members[0].Body.position;
        foreach (var point in new[] { new Vector3(-100,0,-100),new Vector3(100,0,-100),new Vector3(-100,0,100),new Vector3(100,0,100) })
        {
            party.Members[0].ResetTo(point, Quaternion.identity);
            follow.Follow(.02f, true);
            var offset = follow.FrameOffset; var limits = follow.FrameLimits;
            Require(Mathf.Abs(offset.x) <= limits.x + .001f && Mathf.Abs(offset.y) <= limits.y + .001f, "Camera stops at artwork bounds");
            Capture("Level02Bounds_" + point.x + "_" + point.z);
        }
        party.Members[0].ResetTo(start, Quaternion.identity);
        follow.Follow(.02f, true);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/Level02SceneValidation.txt",
            "PASS: Original party, board and HUD prefabs work on Blender Level 02.\n" +
            "PASS: Whole-floor navigation including " + offRoadPoints + " off-road samples and physical perimeter jump tests.\n" +
            "PASS: Exact Blender camera angle/scale, constrained follow, lower-right heroes / upper-left enemies.\n" +
            "PASS: Free movement, enemy spawn, HUD, board click and battle exit.\n");
        Debug.Log("LEVEL02_SCENE_VALIDATED");
    }
    private static void ValidateEdges(PartyChainController party, MeshCollider ground)
    {
        var prior = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        var motor = party.Members[0];
        int checkedEdges = 0;
        try
        {
            var walls = GameObject.Find("Floor Perimeter - Invisible Walls").GetComponentsInChildren<BoxCollider>();
            foreach (var wall in walls)
            {
                if (wall.size.z < 2) continue;
                Vector3 middle = wall.transform.position; middle.y = .1f;
                Vector3 inward = wall.transform.right;
                if (!ground.Raycast(new Ray(middle + inward * 1.5f + Vector3.up * 5, Vector3.down), out _, 10)) inward = -inward;
                Vector3 start = middle + inward * 1.5f;
                if (!ground.Raycast(new Ray(start + Vector3.up * 5, Vector3.down), out _, 10)) continue;
                motor.ResetTo(start, Quaternion.identity);
                Physics.SyncTransforms();
                for (int i = 0; i < 120; i++) { motor.Tick(-inward * 6, i == 15, .02f); Physics.Simulate(.02f); }
                Require(motor.Body.position.y > -.3f && ground.Raycast(new Ray(motor.Body.position + Vector3.up * 5, Vector3.down), out _, 10),
                    "Walking/jumping cannot escape floor at " + wall.name);
                if (++checkedEdges == 12) break;
            }
            Require(checkedEdges >= 8, "Representative perimeter segments checked physically");
        }
        finally { Physics.simulationMode = prior; party.ResetParty(); }
    }
    private static void Capture(string name)
    {
        var camera = Camera.main;
        var target = new RenderTexture(1600, 900, 24);
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes("Logs/" + name + ".png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
