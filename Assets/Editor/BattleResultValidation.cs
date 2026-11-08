// Pure state tests; invoke Run from Unity batch mode or a standalone C# harness.
public static class BattleResultValidation
{
    private static void Check(bool value, string message)
    { if (!value) throw new System.Exception(message); }

    private static AlbedoGambit.Prototype.BattleSession Fixture(int[] starts)
    {
        var state = new AlbedoGambit.Prototype.BattleSession();
        state.ConfigurePrototype(100, 40, 20);
        Check(state.TryBeginPlacement(ulong.MaxValue, 0, starts, true), "Placement failed");
        state.CompletePlacement();
        return state;
    }

    private static int[] Health(AlbedoGambit.Prototype.BattleSession state)
    {
        // Arrange terminal-position fixtures without exposing health mutation to gameplay.
        return (int[])typeof(AlbedoGambit.Prototype.BattleSession).GetField("health",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(state);
    }

    public static void Run()
    {
        var state = Fixture(new[] { 27, 0, 1, 2, 28, 60, 61, 62 });
        state.CompleteEnemyTurn();
        Check(state.TryAttack(0, 28) && state.HealthOf(4) == 60, "Hero damage must be 40");
        Check(state.Result == AlbedoGambit.Prototype.BattleResult.Ongoing, "Premature victory");
        state.EndPlayerTurn(); state.CompleteEnemyTurn();

        state = Fixture(new[] { 27, 0, 1, 2, 28, 60, 61, 62 });
        state.CompleteEnemyTurn();
        var hp = Health(state);
        hp[4] = 30; hp[5] = hp[6] = hp[7] = 0;
        Check(state.TryAttack(0, 28) && state.IsKnockedOut(4), "Final enemy hit failed");
        Check(state.Result == AlbedoGambit.Prototype.BattleResult.Victory, "Victory missing");
        Check(!state.EndPlayerTurn() && !state.TryMove(0, 26) && !state.TryAttack(0, 28), "Actions after victory");
        Check(state.UltimateChargeOf(0) == 1, "Final hit charge missing");

        state = Fixture(new[] { 10, 0, 1, 2, 27, 60, 61, 62 });
        Check(state.TryAttack(4, 10) && state.HealthOf(0) == 80, "Enemy damage must be 20");
        Check(state.Result == AlbedoGambit.Prototype.BattleResult.Ongoing, "Premature defeat");
        state = Fixture(new[] { 10, 0, 1, 2, 27, 60, 61, 62 });
        hp = Health(state); hp[0] = 20; hp[1] = hp[2] = hp[3] = 0;
        Check(state.TryAttack(4, 10), "Final hero hit failed");
        Check(state.Result == AlbedoGambit.Prototype.BattleResult.PartyKnockedOut, "Defeat missing");
        Check(!state.CompleteEnemyTurn() && state.MoveTargets(4) == 0, "Actions after defeat");
        state.Reset();
        Check(state.Result == AlbedoGambit.Prototype.BattleResult.Ongoing, "Reset retained result");
        state.ConfigurePrototype(3, 1);
        Check(state.EnemyAttackDamage == 1, "Legacy damage changed");

        state = Fixture(new[] { 10, 0, 1, 2, 27, 60, 61, 62 });
        hp = Health(state); hp[0] = hp[2] = hp[5] = hp[6] = 0;
        Check(state.CompleteEnemyTurn() && state.SelectedMember == 1, "Skip knocked-out first hero");
        Check(state.EndPlayerTurn() && state.ActingMember == 7, "Skip two knocked-out enemies");
        Check(state.CompleteEnemyTurn() && state.SelectedMember == 3, "Skip knocked-out middle hero");
        Check(state.EndPlayerTurn() && state.ActingMember == 4, "Enemy queue wraps");
        Check(state.CompleteEnemyTurn() && state.SelectedMember == 1, "Hero queue wraps past knockout");
    }
}
