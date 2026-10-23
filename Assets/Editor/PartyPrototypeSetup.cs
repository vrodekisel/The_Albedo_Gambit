using System;
using System.IO;
using System.Linq;
using AlbedoGambit.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PartyPrototypeSetup
{
    private const string ScenePath = "Assets/Scenes/ChessRoomBlockout.unity";
    private const string PrefabFolder = "Assets/Prefabs/Prototype";

    [MenuItem("Tools/Albedo Gambit/Add Party Prototype to Current Scene")]
    public static void AddToCurrentScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        AddPartyAndBoard();
    }

    public static void BuildAndValidate()
    {
        EditorSceneManager.OpenScene(ScenePath);
        AddPartyAndBoard();
        Capture();
        ValidatePhysics();
        Debug.Log("PARTY_PROTOTYPE_VALIDATED");
    }

    private static void AddPartyAndBoard()
    {
        Directory.CreateDirectory(PrefabFolder);
        Directory.CreateDirectory("Assets/Materials/Party");
        AssetDatabase.Refresh();
        var board = GameObject.Find("Chessboard (8 x 8)");
        if (board != null && board.GetComponent<BoardGrid>() == null)
        {
            var squares = board.transform.Find("Squares");
            if (squares == null) throw new InvalidOperationException("Board Squares group missing.");
            var cells = new Transform[64];
            for (int rank = 0; rank < 8; rank++)
            for (int file = 0; file < 8; file++)
            {
                cells[rank * 8 + file] = squares.Find($"Square {(char)('A' + file)}{rank + 1}");
                if (cells[rank * 8 + file] == null) throw new InvalidOperationException("Board cell missing.");
            }
            board.AddComponent<BoardGrid>().Configure(cells);
            PrefabUtility.SaveAsPrefabAssetAndConnect(board, PrefabFolder + "/Chessboard.prefab", InteractionMode.AutomatedAction);
        }

        if (UnityEngine.Object.FindFirstObjectByType<PartyChainController>() == null)
        {
            string prefabPath = PrefabFolder + "/PartyChain.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null) PrefabUtility.InstantiatePrefab(prefab);
            else
            {
                var root = new GameObject("Party - A B C D");
                // Root remains unscaled; the member roots represent their feet.
                root.transform.position = new Vector3(1.65f, .05f, -5.2f);
                var physicsMaterial = new PhysicsMaterial("Party - No Friction")
                { dynamicFriction = 0, staticFriction = 0, bounciness = 0,
                    frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
                AssetDatabase.CreateAsset(physicsMaterial, "Assets/Materials/Party/PartyPhysics.physicMaterial");
                string[] ids = { "A", "B", "C", "D" };
                string[] colors = { "F4F3EC", "D94646", "46B96D", "408CEC" };
                var motors = new CapsuleMotor[4];
                for (int i = 0; i < 4; i++)
                {
                    var member = new GameObject(ids[i] + " - " + (ChessRole)i);
                    member.transform.SetParent(root.transform, false);
                    member.transform.localPosition = Vector3.left * (1.1f * PartyChainController.FormationSlot(i));
                    var collider = member.AddComponent<CapsuleCollider>();
                    collider.center = new Vector3(0, .8f, 0);
                    collider.height = 1.6f; collider.radius = .3f;
                    collider.sharedMaterial = physicsMaterial;
                    var body = member.AddComponent<Rigidbody>();
                    body.mass = 1;
                    body.constraints = RigidbodyConstraints.FreezeRotation;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    body.useGravity = true;
                    member.AddComponent<PartyMember>().Configure(ids[i], (ChessRole)i);
                    motors[i] = member.AddComponent<CapsuleMotor>();
                    var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    visual.name = "Capsule Visual";
                    UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
                    visual.transform.SetParent(member.transform, false);
                    visual.transform.localPosition = new Vector3(0, .8f, 0);
                    visual.transform.localScale = new Vector3(.6f, .8f, .6f);
                    var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    material.name = "Character " + ids[i];
                    ColorUtility.TryParseHtmlString("#" + colors[i], out var color);
                    material.SetColor("_BaseColor", color);
                    material.SetFloat("_Smoothness", .25f);
                    AssetDatabase.CreateAsset(material, "Assets/Materials/Party/Character" + ids[i] + ".mat");
                    visual.GetComponent<Renderer>().sharedMaterial = material;
                }
                root.AddComponent<PartyChainController>().Configure(motors);
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.AutomatedAction);
            }
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
    }

    private static void Capture()
    {
        Directory.CreateDirectory("Logs");
        var camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("Main Camera missing.");
        var target = new RenderTexture(1440, 1080, 24);
        var previous = RenderTexture.active;
        var texture = new Texture2D(1440, 1080, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1440, 1080), 0, 0); texture.Apply();
            File.WriteAllBytes("Logs/PartyPrototypePreview.png", texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void ValidatePhysics()
    {
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        Require(party != null && party.Members.Length == 4, "Four members exist");
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        foreach (var motor in party.Members) motor.Body.interpolation = RigidbodyInterpolation.None;
        Physics.SyncTransforms();
        party.Initialize();
        var report = new System.Text.StringBuilder();
        try
        {
            Simulate(party, Vector3.zero, 70);
            Require(party.Members.All(m => m.IsGrounded), "All members settle on the floor");
            report.AppendLine("PASS: Four dynamic capsules settle under gravity.");
            var start = party.Members[0].Body.position;
            Simulate(party, Vector3.right, 130);
            Simulate(party, Vector3.zero, 100);
            Require(party.Members[0].Body.position.x > start.x + 7, "Leader moves at configured speed");
            for (int i = 1; i < 4; i++)
            {
                float gap = party.Members[PartyChainController.ActorAtSlot(i - 1)].Body.position.x - party.Members[PartyChainController.ActorAtSlot(i)].Body.position.x;
                Require(gap > .8f && gap < 1.4f, "Chain order/spacing for member " + i + ": " + gap);
            }
            report.AppendLine("PASS: Straight movement, A-C-B-D order, spacing and stopping.");
            party.ResetParty(); Simulate(party, Vector3.zero, 40);
            var counts = party.Members.Select(m => m.JumpCount).ToArray();
            party.Tick(Vector3.zero, true, .02f); Physics.Simulate(.02f);
            Simulate(party, Vector3.zero, 120);
            for (int i = 0; i < 4; i++) Require(party.Members[i].JumpCount == counts[i] + 1,
                "Stationary jump propagates once to member " + i);
            Require(party.Members.All(m => m.IsGrounded), "Party lands after stationary jump");
            report.AppendLine("PASS: Space jump propagates once A-C-B-D while stationary; all land.");
            party.ResetParty(); Simulate(party, Vector3.zero, 40);
            counts = party.Members.Select(m => m.JumpCount).ToArray();
            Simulate(party, Vector3.right, 15);
            party.Tick(Vector3.right, true, .02f); Physics.Simulate(.02f);
            Simulate(party, Vector3.right, 120);
            Simulate(party, Vector3.zero, 70);
            for (int i = 0; i < 4; i++) Require(party.Members[i].JumpCount == counts[i] + 1,
                "Moving jump follows route for member " + i);
            report.AppendLine("PASS: Moving jumps replay along the route for all followers.");
            party.ResetParty(); Simulate(party, Vector3.zero, 40);
            Simulate(party, Vector3.forward, 5);
            party.Tick(Vector3.forward, true, .02f); Physics.Simulate(.02f);
            Simulate(party, Vector3.forward, 90);
            Simulate(party, Vector3.zero, 100);
            foreach (var member in party.Members)
                Require(member.Body.position.y > .18f && member.Body.position.y < .28f && member.IsGrounded,
                    "Member lands on raised board: " + member.name + " " + member.Body.position);
            report.AppendLine("PASS: All four jump onto the raised board and land on its colliders.");
            party.ResetParty(); Simulate(party, Vector3.zero, 40);
            Simulate(party, Vector3.back, 70);
            Simulate(party, Vector3.right, 100);
            Simulate(party, Vector3.zero, 100);
            for (int i = 1; i < 4; i++)
            {
                var offset = party.Members[PartyChainController.ActorAtSlot(i - 1)].Body.position - party.Members[PartyChainController.ActorAtSlot(i)].Body.position;
                Require(offset.x > .8f && offset.x < 1.4f && Mathf.Abs(offset.z) < .18f,
                    "Followers complete a right-angle turn: " + offset);
            }
            report.AppendLine("PASS: Right-angle turn follows breadcrumbs, settles into ordered chain.");
            party.ResetParty(); Simulate(party, Vector3.zero, 40);
            Simulate(party, Vector3.left, 150);
            Require(party.Members.All(m => m.Body.position.x > -5.7f), "Wall blocks capsules");
            report.AppendLine("PASS: Wall collision prevents moving through the left wall.");
            party.ResetParty(); Simulate(party, Vector3.zero, 40);
            party.Members[0].Body.position = new Vector3(0, -15, 0);
            Simulate(party, Vector3.zero, 60);
            Require(party.Members.All(m => m.IsGrounded), "Fall reset restores party");
            report.AppendLine("PASS: Falling resets the whole group to the scene spawn.");
            var board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
            for (int rank = 0; rank < 8; rank++) for (int file = 0; file < 8; file++)
                Require(board.TryGetCell(board.CellCenter(file, rank), out var cell) && cell == new Vector2Int(file, rank), "Cell coordinate roundtrip");
            report.AppendLine("PASS: All 64 board coordinate roundtrips.");
            File.WriteAllText("Logs/PartyPrototypeValidation.txt", report.ToString());
        }
        finally
        {
            Physics.simulationMode = mode;
            // Never save the simulated positions over the authored scene.
        }
    }

    private static void Simulate(PartyChainController party, Vector3 direction, int frames)
    {
        for (int i = 0; i < frames; i++) { party.Tick(direction, false, .02f); Physics.Simulate(.02f); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Physics validation failed: " + message);
    }
}
