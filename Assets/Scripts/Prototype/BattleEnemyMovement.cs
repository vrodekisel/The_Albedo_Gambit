namespace AlbedoGambit.Prototype
{
    // Deliberately a movement-only baseline. Replace this policy when attacks and
    // abilities exist; adaptation/history and composite-action search are deferred.
    public static class BattleEnemyMovement
    {
        public static int ChooseDestination(BattleSession state, int actor)
        {
            if (state.Side != BattleSide.Enemy || actor != state.ActingMember) return -1;
            ulong legal = state.MoveTargets(actor);
            if (legal == 0) return -1;
            int origin = state.CellOf(actor), target = -1, nearest = int.MaxValue;
            for (int hero = 0; hero < 4; hero++)
            {
                if (state.CellOf(hero) < 0 || state.IsKnockedOut(hero)) continue;
                int distance = DistanceSquared(origin, state.CellOf(hero));
                if (distance < nearest) { nearest = distance; target = state.CellOf(hero); }
            }
            if (target < 0) return -1;
            int best = -1, bestDistance = int.MaxValue, bestTravel = int.MaxValue;
            for (int cell = 0; cell < 64; cell++)
            {
                if ((legal & (1UL << cell)) == 0) continue;
                int distance = DistanceSquared(cell, target), travel = DistanceSquared(origin, cell);
                // Deterministic ties: shorter movement, then lower cell index.
                if (distance < bestDistance || distance == bestDistance && travel < bestTravel)
                { best = cell; bestDistance = distance; bestTravel = travel; }
            }
            return best;
        }
        private static int DistanceSquared(int a, int b)
        {
            int x = a % 8 - b % 8, y = a / 8 - b / 8;
            return x * x + y * y;
        }
    }
}
