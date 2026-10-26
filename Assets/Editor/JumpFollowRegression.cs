using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AlbedoGambit.Prototype;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JumpFollowRegression
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        var prior = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        var report = new System.Text.StringBuilder();
        try
        {
            foreach (var m in party.Members) m.Body.interpolation = RigidbodyInterpolation.None;
            Physics.SyncTransforms(); party.Initialize();
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < i; j++)
                    if (Physics.GetIgnoreCollision(party.Members[i].GetComponent<Collider>(), party.Members[j].GetComponent<Collider>()))
                        throw new Exception("Party colliders must interact outside battle");
            Sim(party, Vector3.zero, 50);
            party.ToggleGroup(); party.SelectMember(2);
            for (int frame = 0; frame < 100; frame++)
            {
                Sim(party, Vector3.right, 1);
                var a = party.Members[0].Body.position;
                var c = party.Members[2].Body.position;
                if (Vector3.Distance(a, c) < .55f)
                    throw new Exception("Separated party members overlap while crossing");
            }
            party.ResetParty(); Sim(party, Vector3.zero, 50);
            report.AppendLine("PASS: Party colliders interact; selected member cannot pass through another capsule.");
            Sim(party, Vector3.right, 8);
            party.Tick(Vector3.right, true, .02f); Physics.Simulate(.02f);
            Sim(party, Vector3.right, 90); Sim(party, Vector3.zero, 180);
            CheckChain(party, "moving jump on flat ground");
            report.AppendLine("PASS: Moving jump on flat ground; followers recover spacing without G.");
            party.ResetParty(); Sim(party, Vector3.zero, 50);
            for (int frame = 0; frame < 150; frame++)
            {
                party.Tick(Vector3.right, frame == 5 || frame == 55 || frame == 105, .02f);
                Physics.Simulate(.02f);
            }
            Sim(party, Vector3.zero, 180);
            CheckChain(party, "three moving jumps");
            report.AppendLine("PASS: Three successive moving jumps keep the chain functional.");
            party.ResetParty(); Sim(party, Vector3.zero, 50);
            Sim(party, Vector3.back, 35);
            party.Tick(Vector3.back, true, .02f); Physics.Simulate(.02f);
            Sim(party, Vector3.back, 10);
            Sim(party, Vector3.right, 110); Sim(party, Vector3.zero, 300);
            CheckChain(party, "jump beside front cube, then leave");
            report.AppendLine("PASS: Jump beside front cube then move away; all followers catch up without regrouping.");
        }
        finally { Physics.simulationMode = prior; }
        // Existing suite verifies separation, regrouping, nav detours, chess transitions and jump order.
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        typeof(PartySandboxSetup).GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        report.AppendLine("PASS: Existing sandbox physics regression suite.");
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/JumpFollowValidation.txt", report.ToString());
        Debug.Log("JUMP_FOLLOW_VALIDATED");
    }

    private static void CheckChain(PartyChainController p, string label)
    {
        Debug.Log(label + ": " + string.Join(" / ", p.Members.Select(m => m.Body.position.ToString("F3"))));
        if (p.Mode != PartyMode.Chained) throw new Exception(label + ": chain mode changed");
        for (int i = 1; i < 4; i++)
        {
            Vector3 delta = p.Members[PartyChainController.ActorAtSlot(i - 1)].Body.position - p.Members[PartyChainController.ActorAtSlot(i)].Body.position;
            if (delta.x < .7f || delta.x > 1.5f || Mathf.Abs(delta.z) > .25f || !p.Members[i].IsGrounded)
                throw new Exception(label + ": follower " + i + " stuck/out of order, delta=" + delta);
        }
    }
    private static void Sim(PartyChainController p, Vector3 move, int frames)
    { for (int i = 0; i < frames; i++) { p.Tick(move, false, .02f); Physics.Simulate(.02f); } }
}
