using System;
using System.IO;
using System.Linq;
using AlbedoGambit.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BattleAbilityValidation
{
    static BattleAbilityValidation() { EditorApplication.playModeStateChanged += OnState; }
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception("Ability block: " + message); }
    public static void Play()
    {
        Rules();
        EditorSceneManager.OpenScene("Assets/Scenes/ChessRoomBlockout.unity");
        SessionState.SetBool("Albedo.AbilityTest", true); EditorApplication.EnterPlaymode();
    }
    private static BattleSession Fixture()
    {
        var state = new BattleSession();
        state.ConfigurePrototype(100, 30);
        Check(state.TryBeginPlacement(ulong.MaxValue, 0, new[] { 27, 0, 7, 63, 28, 54, 55, 56 }, true)
            && state.CompletePlacement() && state.CompleteEnemyTurn(), "Configured encounter");
        return state;
    }
    private static void Rules()
    {
        var state = Fixture();
        Check(state.MaxHealth == 100 && state.AttackDamage == 30 && state.HealthOf(4) == 100,
            "100 maximum health and 30 damage");
        Check((state.AttackTargets(0) & (1UL << 28)) != 0, "King may target adjacent enemy");
        Check(!state.TryAttack(0, 54) && state.UltimateChargeOf(0) == 0 && !state.ActionSpent,
            "Miss cannot spend action or give charge");
        Check(state.TryAttack(0, 28) && state.HealthOf(4) == 70 && state.UltimateChargeOf(0) == 1,
            "Hit spends action, removes 30 and adds one charge");
        Check(!state.TryAttack(0, 28) && state.TryMove(0, 26), "One ability then movement");
        Check(state.EndPlayerTurn() && state.CompleteEnemyTurn() && state.SelectedMember == 1,
            "Next hero receives separate action budget");
        for (int round = 0; round < 3; round++)
        {
            for (int i = 1; i < 4; i++) { state.EndPlayerTurn(); state.CompleteEnemyTurn(); }
            Check(state.SelectedMember == 0, "Hero queue cycles");
            if (round == 0) Check(state.TryMove(0, 27) && state.TryAttack(0, 28), "Movement then ability");
            else Check(state.TryAttack(0, 28), "Later turn allows another ability");
            Check(state.UltimateChargeOf(0) == Math.Min(3, round + 2), "Charge capped at three");
            Check(state.HealthOf(4) == Math.Max(0, 70 - 30 * (round + 1)), "Damage clamps at zero");
            state.EndPlayerTurn(); state.CompleteEnemyTurn();
        }
        state.Reset();
        Check(state.UltimateChargeOf(0) == 0 && state.HealthOf(4) == 0, "Encounter reset clears charge and health");
        Debug.Log("BATTLE_ABILITY_RULES_PASS");
    }
    private static void OnState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Albedo.AbilityTest", false)) return;
        SessionState.SetBool("Albedo.AbilityTest", false); EditorApplication.update += Run;
    }
    private static void Run()
    {
        EditorApplication.update -= Run;
        try { Scene(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static void Tick(PartyChainController party, int frames)
    { for (int i = 0; i < frames; i++) party.Tick(Vector3.zero, false, .02f); }
    private static int First(ulong mask)
    { for (int c = 0; c < 64; c++) if ((mask & (1UL << c)) != 0) return c; return -1; }
    private static int Distance(int a, int b)
    { int x = a % 8 - b % 8, y = a / 8 - b / 8; return x * x + y * y; }
    private static int BestApproach(BattleSession state, int hero)
    {
        ulong moves = state.MoveTargets(hero);
        int best = -1, score = int.MaxValue;
        for (int c = 0; c < 64; c++)
        {
            if ((moves & (1UL << c)) == 0) continue;
            int nearest = int.MaxValue;
            for (int e = 4; e < 8; e++) if (!state.IsKnockedOut(e)) nearest = Math.Min(nearest, Distance(c, state.CellOf(e)));
            if (nearest < score) { best = c; score = nearest; }
        }
        return best;
    }
    private static void Scene()
    {
        var party = UnityEngine.Object.FindFirstObjectByType<PartyChainController>();
        var board = UnityEngine.Object.FindFirstObjectByType<BoardGrid>();
        var hud = UnityEngine.Object.FindFirstObjectByType<BattleHud>();
        party.Initialize(); hud.Bind(party); party.ResetParty();
        Check(party.EnterChessMode(board), "Enter room encounter");
        var state = party.Battle;
        Check(state.MaxHealth == 100 && state.AttackDamage == 30, "Scene combat values");
        Tick(party, 80);
        bool hit = false;
        for (int turn = 0; turn < 32 && !hit; turn++)
        {
            Check(state.Side == BattleSide.Player, "Player turn after enemy movement");
            int hero = state.SelectedMember;
            int target = First(state.AttackTargets(hero));
            if (target < 0)
            {
                int move = BestApproach(state, hero);
                if (move >= 0) { Check(party.TryMoveSelected(move), "Approach"); Tick(party, 20); }
                target = First(state.AttackTargets(hero));
            }
            if (target >= 0)
            {
                int victim = state.OccupantAt(target), before = state.HealthOf(victim);
                int charge = state.UltimateChargeOf(hero);
                hud.abilityButtons[0].onClick.Invoke();
                Check(party.IsAimingAbility && (party.AvailableAttackTargets & (1UL << target)) != 0,
                    "First ability enters targeting mode");
                Check(!party.TryUseAbilityOn(state.CellOf(hero)) && state.HealthOf(victim) == before,
                    "Invalid target does not spend action");
                Check(party.GetComponent<BattleBoardView>().ClickCell(target) && party.IsBattleMoving,
                    "Clicking enemy launches projectile and locks turn");
                Check(!party.EndPreviewTurn(), "Cannot end during flight");
                Tick(party, 10);
                var projectile = party.transform.Find("Ability Projectile");
                Check(projectile != null, "Projectile visible in flight");
                typeof(BattleHudValidation).GetMethod("Capture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(null, new object[] { hud, 1920, 1080, "AbilityProjectile" });
                var position = projectile.position;
                party.TogglePause(); Tick(party, 30);
                Check(projectile.position == position && state.HealthOf(victim) == before, "Pause freezes projectile and damage");
                party.TogglePause(); Tick(party, 20);
                Check(state.HealthOf(victim) == before - 30 && state.UltimateChargeOf(hero) == charge + 1 && state.ActionSpent,
                    "Projectile hit applies damage and charge once");
                Check(!party.BeginAbility(1) && !party.GetComponent<BattleBoardView>().ClickCell(target),
                    "Second ability cannot be used in same turn");
                hud.Refresh();
                Check(hud.manaLabel.text.Contains((charge + 1) + " / 3"), "Profile charge is visible");
                hit = true;
            }
            else { party.EndPreviewTurn(); Tick(party, 80); }
        }
        Check(hit, "A reachable enemy can be attacked in room play mode");
        party.ExitChessMode();
        Check(party.EnterChessMode(board) && state.UltimateChargeOf(0) == 0, "New battle resets charge");
        Tick(party, 80);
        // Reach another target and verify the second button produces the same hit.
        bool secondHit = false;
        for (int turn = 0; turn < 32 && !secondHit; turn++)
        {
            int hero = state.SelectedMember;
            int target = First(state.AttackTargets(hero));
            if (target < 0)
            {
                int move = BestApproach(state, hero);
                if (move >= 0) { Check(party.TryMoveSelected(move), "Second ability approach"); Tick(party, 20); }
                target = First(state.AttackTargets(hero));
            }
            if (target >= 0)
            {
                int victim = state.OccupantAt(target), before = state.HealthOf(victim);
                hud.abilityButtons[1].onClick.Invoke();
                Check(party.IsAimingAbility && !state.ActionSpent, "Second button selects targeting mode");
                Check(party.GetComponent<BattleBoardView>().ClickCell(target), "Second ability fires");
                Tick(party, 25);
                Check(state.HealthOf(victim) == before - 30 && state.UltimateChargeOf(hero) == 1,
                    "Second ability has same temporary damage and charge");
                secondHit = true;
            }
            else { party.EndPreviewTurn(); Tick(party, 80); }
        }
        Check(secondHit, "Second ability reaches a target in room");
        party.ExitChessMode();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/BattleAbilityValidation.txt", "PASS: 100 HP, 30 damage, one ability per turn, attack-before-move and move-before-attack, role targeting, real projectile hit, pause, UI charge, reset, and both ability buttons.\nOnly ChessRoomBlockout tested; enemy attacks and ultimate activation remain separate work.\n");
        Debug.Log("BATTLE_ABILITY_SCENE_PASS");
    }
}
