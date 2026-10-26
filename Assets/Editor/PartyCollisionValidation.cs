using System;
using System.IO;
using AlbedoGambit.Prototype;
using UnityEditor.SceneManagement;
using UnityEngine;

// Deterministic physics regression, run only in the validation copy.
public static class PartyCollisionValidation
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        var prior = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            party.Initialize();
            foreach (var m in party.Members) m.Body.interpolation = RigidbodyInterpolation.None;
            Sim(party, Vector3.zero, 50);
            var start = party.Members[0].Body.position;
            Sim(party, Vector3.left, 120);
            Sim(party, Vector3.zero, 400);
            Check(start.x - party.Members[0].Body.position.x > 6, "A can reverse through the formation without being trapped");
            Chain(party, Vector3.left, "Reverse from rest");
            party.ResetParty(); Sim(party, Vector3.zero, 50);
            Sim(party, Vector3.left, 12);
            Sim(party, Vector3.zero, 400);
            Chain(party, Vector3.left, "Short reverse followed by a stop");
            party.ResetParty(); Sim(party, Vector3.zero, 50);
            Sim(party, Vector3.right, 90, true);
            Sim(party, Vector3.left, 120, true);
            Sim(party, Vector3.right, 120, true);
            Sim(party, Vector3.zero, 400);
            Chain(party, Vector3.right, "Repeated reversals and jumps");
            party.ResetParty(); Sim(party, Vector3.zero, 50);
            party.ToggleGroup();
            // Swap followers across one another's regroup slots, with A fixed.
            Vector3 anchor = party.Members[0].Body.position;
            party.Members[1].ResetTo(anchor + Vector3.left * 3.3f, Quaternion.identity);
            party.Members[2].ResetTo(anchor + Vector3.left * 2.2f, Quaternion.identity);
            party.Members[3].ResetTo(anchor + Vector3.left * 1.1f, Quaternion.identity);
            Physics.SyncTransforms(); party.BeginRegroup();
            Sim(party, Vector3.zero, 800);
            Check(party.Mode == PartyMode.Chained, "Crossed followers can regroup without manual reset");
            Chain(party, Vector3.right, "Crossed regroup");
            File.WriteAllText("Logs/PartyCollisionValidation.txt", "PASS: Reverse from rest; repeated reversals with jumps; crossed regroup; physical separation during every simulation step.\n");
            Debug.Log("PARTY_COLLISIONS_VALIDATED");
        }
        finally { Physics.simulationMode = prior; }
    }
    private static void Sim(PartyChainController party, Vector3 move, int frames, bool jump = false)
    {
        for (int f = 0; f < frames; f++)
        {
            party.Tick(move, jump && f == 15, .02f); Physics.Simulate(.02f);
            for (int i = 0; i < 4; i++) for (int j = 0; j < i; j++)
            {
                var a = party.Members[i].GetComponent<CapsuleCollider>();
                var b = party.Members[j].GetComponent<CapsuleCollider>();
                Check(!Physics.GetIgnoreCollision(a, b), "Party collisions remain enabled");
                if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                    b, b.transform.position, b.transform.rotation, out _, out float depth))
                    Check(depth < .05f, "Capsules must not overlap: " + i + "/" + j + " depth=" + depth);
            }
        }
    }
    private static void Chain(PartyChainController party, Vector3 direction, string label)
    {
        for (int slot = 1; slot < 4; slot++)
        {
            var delta = party.Members[PartyChainController.ActorAtSlot(slot - 1)].Body.position
                - party.Members[PartyChainController.ActorAtSlot(slot)].Body.position;
            delta.y = 0;
            Check(Vector3.Dot(delta, direction) > .7f && delta.magnitude < 1.5f,
                label + ": member " + slot + " is not back in formation, delta=" + delta);
        }
    }
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
}
