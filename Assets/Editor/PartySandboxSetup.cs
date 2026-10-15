using System;
using System.IO;
using System.Linq;
using AlbedoGambit.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Batch authoring/verification runs in a staging copy, never over an open unsaved scene.
public static class PartySandboxSetup
{
    public static void BuildAndValidate()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        var floor = GameObject.Find("Floor (12 x 12 m)");
        if (floor == null) floor = GameObject.Find("Floor - Sandbox");
        floor.name = "Floor - Sandbox";
        floor.transform.localScale = new Vector3(40, .4f, 40);
        var back = GameObject.Find("Wall - Back").transform;
        back.localPosition = new Vector3(0, 15, 11);
        back.localScale = new Vector3(40, 30, .3f);
        var left = GameObject.Find("Wall - Left").transform;
        left.localPosition = new Vector3(-11, 15, 0);
        left.localScale = new Vector3(.3f, 30, 40);
        var room = GameObject.Find("Room");
        if (room.GetComponent<PrototypeNavigation>() == null) room.AddComponent<PrototypeNavigation>();
        var camera = Camera.main;
        camera.orthographicSize = 12.4f;
        camera.transform.position = new Vector3(0, .8f, 0) - camera.transform.forward * 35;

        Directory.CreateDirectory("Assets/Materials/PrototypeSandbox");
        AssetDatabase.Refresh();
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PrototypeSandbox/Obstacles.mat");
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(.40f, .46f, .50f));
            material.SetFloat("_Smoothness", .12f);
            AssetDatabase.CreateAsset(material, "Assets/Materials/PrototypeSandbox/Obstacles.mat");
        }
        if (GameObject.Find("Obstacle Playground") == null)
        {
            var group = new GameObject("Obstacle Playground");
            Box(group.transform, material, "Cube - Left", new Vector3(-7, 0, -3), new Vector3(2, 1.6f, 2));
            Box(group.transform, material, "Cube - Detour Test", new Vector3(7, 0, -2), new Vector3(2, 2.2f, 2));
            Box(group.transform, material, "Cube - Tall Right", new Vector3(8, 0, 4), new Vector3(3, 3, 3));
            Box(group.transform, material, "Cube - Front", new Vector3(0, 0, -9), new Vector3(3, 2, 2));
            Box(group.transform, material, "Cube - Front Left", new Vector3(-6, 0, -8), new Vector3(2, 2.5f, 2));
            Box(group.transform, material, "Cube - Back Left", new Vector3(-3, 0, 8), new Vector3(2, 1.6f, 2));
            Box(group.transform, material, "Cube - Back Right", new Vector3(3, 0, 8), new Vector3(2.5f, 3, 2.5f));
        }

        const string partyPath = "Assets/Prefabs/Prototype/PartyChain.prefab";
        var contents = PrefabUtility.LoadPrefabContents(partyPath);
        var ringMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PrototypeSandbox/Selection.mat");
        if (ringMat == null)
        {
            ringMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            ringMat.SetColor("_BaseColor", new Color(1, .8f, .18f));
            AssetDatabase.CreateAsset(ringMat, "Assets/Materials/PrototypeSandbox/Selection.mat");
        }
        try
        {
            var motors = contents.GetComponent<PartyChainController>().Members;
            for (int i = 0; i < 4; i++)
            {
                if (motors[i].transform.Find("Selection Ring") != null) continue;
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "Selection Ring";
                UnityEngine.Object.DestroyImmediate(ring.GetComponent<Collider>());
                ring.transform.SetParent(motors[i].transform, false);
                ring.transform.localPosition = new Vector3(0, .015f, 0);
                ring.transform.localScale = new Vector3(.8f, .008f, .8f);
                ring.GetComponent<Renderer>().sharedMaterial = ringMat;
                ring.SetActive(i == 0);
            }
            PrefabUtility.SaveAsPrefabAsset(contents, partyPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        // Reload to exercise serialized references and the updated prefab instance.
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        Capture();
        Validate();
        Debug.Log("SANDBOX_VALIDATED");
    }

    private static void Box(Transform parent, Material material, string name, Vector3 feet, Vector3 size)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name; box.transform.SetParent(parent, false);
        box.transform.position = feet + Vector3.up * size.y * .5f;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void Capture()
    {
        Directory.CreateDirectory("Logs");
        var camera = Camera.main;
        var target = new RenderTexture(1600, 1200, 24);
        var prior = RenderTexture.active;
        var texture = new Texture2D(1600, 1200, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0); texture.Apply();
            File.WriteAllBytes("Logs/PartySandboxPreview.png", texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = prior;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void Validate()
    {
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        var nav = UnityEngine.Object.FindFirstObjectByType<PrototypeNavigation>();
        var board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
        var report = new System.Text.StringBuilder();
        var previous = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            foreach (var member in party.Members) member.Body.interpolation = RigidbodyInterpolation.None;
            Physics.SyncTransforms(); party.Initialize();
            Require(nav.EnsureReady(), "Navigation builds from colliders");
            var path = new NavMeshPath();
            Require(nav.FindPath(new Vector3(4, 0, -2), new Vector3(10, 0, -2), path) && path.corners.Length >= 3,
                "Path detours around cube");
            report.AppendLine("PASS: NavMesh builds; route around cube has multiple corners.");
            Sim(party, Vector3.zero, 50);
            party.Members[3].Body.position += Vector3.left * 2;
            Physics.SyncTransforms();
            Sim(party, Vector3.right, 170);
            for (int i = 1; i < 4; i++)
            {
                float gap = party.Members[i - 1].Body.position.x - party.Members[i].Body.position.x;
                Require(gap > .7f && gap < 1.6f, "Catch-up during continuous movement member=" + i + " gap=" + gap);
            }
            report.AppendLine("PASS: Displaced D catches up while A keeps moving; chain spacing remains bounded.");
            party.ResetParty(); Sim(party, Vector3.zero, 40);
            var initial = party.Members.Select(m => m.Body.position).ToArray();
            party.ToggleGroup(); party.SelectMember(2);
            Sim(party, Vector3.back, 55); Sim(party, Vector3.zero, 35);
            Require(party.Mode == PartyMode.Separated && party.SelectedIndex == 2, "G / selection state");
            Require(Flat(party.Members[2].Body.position, initial[2]) > 2, "Selected C moves");
            foreach (int i in new[] { 0, 1, 3 }) Require(Flat(party.Members[i].Body.position, initial[i]) < .1f, "Other members wait");
            report.AppendLine("PASS: Separation and selection move C only; A, B, D remain in place.");
            party.ToggleGroup(); Sim(party, Vector3.zero, 500);
            Require(party.Mode == PartyMode.Chained && party.SelectedIndex == 0, "Regroup restores A leadership");
            report.AppendLine("PASS: G regroups at A and restores chained mode.");
            party.ResetParty(); Sim(party, Vector3.zero, 40);
            // Place B on the far side of a cube, then regroup with a physically blocked direct line.
            party.ToggleGroup();
            party.Members[0].ResetTo(new Vector3(10, .05f, -2), Quaternion.identity);
            party.Members[1].ResetTo(new Vector3(4, .05f, -2), Quaternion.identity);
            party.Members[2].ResetTo(new Vector3(4, .05f, -4), Quaternion.identity);
            party.Members[3].ResetTo(new Vector3(4, .05f, -5.5f), Quaternion.identity);
            Physics.SyncTransforms(); Sim(party, Vector3.zero, 40);
            party.BeginRegroup();
            float detour = 0;
            for (int frame = 0; frame < 800 && party.Mode == PartyMode.Regrouping; frame++)
            {
                Sim(party, Vector3.zero, 1);
                detour = Mathf.Max(detour, Mathf.Abs(party.Members[1].Body.position.z + 2));
            }
            Require(party.Mode == PartyMode.Chained && detour > 1.2f, "Physics detour and rejoin: " + party.Mode + " detour=" + detour);
            report.AppendLine("PASS: Rigidbody follower walks around cube and regroups without teleporting.");
            party.ResetParty(); Sim(party, Vector3.zero, 40);
            int[] counts = party.Members.Select(m => m.JumpCount).ToArray();
            party.Tick(Vector3.zero, true, .02f); Physics.Simulate(.02f);
            int[] first = { 0, -1, -1, -1 };
            for (int frame = 1; frame < 100; frame++)
            {
                Sim(party, Vector3.zero, 1);
                for (int i = 1; i < 4; i++) if (first[i] < 0 && party.Members[i].JumpCount > counts[i]) first[i] = frame;
            }
            Require(first[1] > 0 && first[2] > first[1] && first[3] > first[2], "Timed A B C D jump order");
            report.AppendLine("PASS: Jumps propagate in temporal A-B-C-D order.");
            var positions = party.Members.Select(m => m.Body.position).ToArray();
            Require(party.EnterChessMode(board), "Enter chess mode");
            Sim(party, Vector3.right, 50);
            for (int i = 0; i < 4; i++)
            {
                Require(party.Members[i].Body.isKinematic, "Chess body locked");
                Require(Flat(party.Members[i].Body.position, board.CellCenter(2 + i, 0)) < .01f, "Chess placement and input lock");
            }
            party.ExitChessMode();
            for (int i = 0; i < 4; i++) Require(!party.Members[i].Body.isKinematic && Flat(party.Members[i].Body.position, positions[i]) < .01f, "Restore exploration position");
            report.AppendLine("PASS: T-style API places C1-F1, locks movement, restores prior positions on exit.");
            party.ToggleGroup(); party.SelectMember(3); party.EnterChessMode(board); party.ExitChessMode();
            Require(party.Mode == PartyMode.Separated && party.SelectedIndex == 3, "Restore separated mode and selection");
            report.AppendLine("PASS: Chess exit restores separated state and selected D.");
            party.ResetParty(); Sim(party, Vector3.zero, 40);
            Sim(party, Vector3.forward, 65); Sim(party, Vector3.zero, 150);
            Require(party.Mode == PartyMode.Chained, "Walking onto board does not enter chess");
            Require(party.Members[0].Body.position.y > .18f, "Board lip is walkable");
            report.AppendLine("PASS: Free movement on board, no automatic chess trigger; low step traversed.");
            party.EnterChessMode(board); party.ResetParty(); Sim(party, Vector3.zero, 40);
            Require(party.Mode == PartyMode.Chained && party.Members.All(m => !m.Body.isKinematic && m.IsGrounded), "Reset from chess");
            report.AppendLine("PASS: R reset restores dynamic grounded party from chess mode.");
            File.WriteAllText("Logs/PartySandboxValidation.txt", report.ToString());
        }
        finally { Physics.simulationMode = previous; }
    }
    private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    private static void Sim(PartyChainController p, Vector3 move, int frames)
    { for (int i = 0; i < frames; i++) { p.Tick(move, false, .02f); Physics.Simulate(.02f); } }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("SANDBOX CHECK FAILED: " + message); }
}
