using System;

namespace AlbedoGambit.Prototype
{
    public enum BattlePhase { Exploration, Cutscene, Placement, Battle, Paused }
    public enum BattleSide { Player, Enemy }
    public enum BattleResult { Ongoing, Victory, PartyKnockedOut }

    // Logical state has no Unity references. Rendering and physics never own occupancy.
    // This is the baseline for later action simulation, not the optimized rollback system.
    public sealed class BattleSession
    {
        private readonly int[] occupants = new int[64];
        private readonly int[] memberCells = new int[8];
        private readonly int[] health = new int[8];
        private readonly int[] ultimateCharge = new int[8];
        private bool borrowedPending, borrowedActive;
        public bool UsesMiaSkills(int member) => member == 1 || member == 0 && borrowedActive;
        private int nextEnemy;
        private int nextPlayerSlot;
        public bool OrderedEnemyMovement { get; private set; }
        public bool IsTraining { get; private set; }
        private BattlePhase resumePhase;
        private ulong activeCells;
        private ulong blockedCells;
        public BattlePhase Phase { get; private set; }
        public int SelectedMember { get; private set; } = -1;
        public int ActingMember { get; private set; } = -1;
        public BattleSide Side { get; private set; }
        public BattleResult Result { get; private set; }
        public int ParticipantCount { get; private set; }
        public int PlayerTurn { get; private set; }
        public bool MovementSpent { get; private set; }
        public bool ActionSpent { get; private set; }
        public int MaxHealth { get; private set; } = 3;
        public int AttackDamage { get; private set; } = 1;
        public int EnemyAttackDamage { get; private set; } = 1;

        public BattleSession() { Reset(); }

        public bool HasCell(int cell) => cell >= 0 && cell < 64 && (activeCells & (1UL << cell)) != 0;
        public bool IsBlocked(int cell) => !HasCell(cell) || (blockedCells & (1UL << cell)) != 0;
        public int OccupantAt(int cell) => HasCell(cell) ? occupants[cell] : -1;
        public int CellOf(int member) => member >= 0 && member < ParticipantCount ? memberCells[member] : -1;
        public int HealthOf(int member) => member >= 0 && member < ParticipantCount ? health[member] : 0;
        public int UltimateChargeOf(int member) => member >= 0 && member < ParticipantCount ? ultimateCharge[member] : 0;
        public bool IsKnockedOut(int member) => CellOf(member) >= 0 && health[member] == 0;
        public ChessRole RoleOf(int member)
        {
            if (member >= 0 && member < 4) return (ChessRole)member;
            if (!OrderedEnemyMovement) return ChessRole.King;
            // Reverse of the player's board formation: A, B, C, D -> D, C, B, A.
            switch (member)
            {
                case 4: return ChessRole.Knight;
                case 5: return ChessRole.Bishop;
                case 6: return ChessRole.Rook;
                default: return ChessRole.King;
            }
        }
        public bool SameTeam(int a, int b) => (a < 4) == (b < 4);
        public void ConfigurePrototype(int maxHealth, int damage, int enemyDamage = -1)
        {
            if (Phase != BattlePhase.Exploration && Phase != BattlePhase.Cutscene)
                throw new InvalidOperationException("Configure outside an encounter.");
            MaxHealth = Math.Max(1, maxHealth); AttackDamage = Math.Max(1, damage);
            EnemyAttackDamage = enemyDamage < 0 ? AttackDamage : Math.Max(1, enemyDamage);
        }

        // Validate the whole placement before changing any state. No half-placed party on failure.
        public bool TryBeginPlacement(ulong active, ulong blocked, int[] starts, bool orderedEnemyMovement = false, int[] partyHealth = null, bool training = false)
        {
            if (Phase != BattlePhase.Exploration && Phase != BattlePhase.Cutscene) return false;
            if (starts == null || (starts.Length != 4 && starts.Length != 8)) return false;
            if (training && (!orderedEnemyMovement || starts.Length != 8)) return false;
            if (partyHealth != null && partyHealth.Length != 4) return false;
            ulong used = 0;
            for (int i = 0; i < starts.Length; i++)
            {
                int cell = starts[i];
                if (cell < 0 || cell >= 64) return false;
                ulong bit = 1UL << cell;
                if ((active & bit) == 0 || (blocked & bit) != 0 || (used & bit) != 0) return false;
                used |= bit;
            }
            borrowedPending = borrowedActive = false;
            ClearOccupancy();
            OrderedEnemyMovement = orderedEnemyMovement && starts.Length == 8;
            IsTraining = training;
            nextPlayerSlot = 0;
            ParticipantCount = starts.Length;
            activeCells = active;
            blockedCells = blocked;
            for (int i = 0; i < starts.Length; i++)
            {
                memberCells[i] = starts[i];
                occupants[starts[i]] = i;
                health[i] = i < 4 && partyHealth != null ? Math.Max(0, Math.Min(MaxHealth, partyHealth[i])) : MaxHealth;
                ultimateCharge[i] = training && i < 4 ? 3 : 0;
            }
            SelectedMember = 0;
            ActingMember = -1; Side = BattleSide.Player; Result = BattleResult.Ongoing;
            PlayerTurn = 1; nextEnemy = 4; MovementSpent = ActionSpent = false;
            Phase = BattlePhase.Placement;
            return true;
        }

        public bool CompletePlacement()
        {
            if (Phase != BattlePhase.Placement) return false;
            Phase = BattlePhase.Battle;
            if (OrderedEnemyMovement)
            {
                PlayerTurn = 0;
                BeginEnemyTurn();
                if (IsTraining) CompleteEnemyTurn();
            }
            return true;
        }

        public bool TrySelect(int member)
        {
            if (!CanSelect(member)) return false;
            SelectedMember = member;
            return true;
        }

        public bool CanSelect(int member) => Phase == BattlePhase.Battle && Side == BattleSide.Player &&
            Result == BattleResult.Ongoing && member >= 0 && member < 4 && CellOf(member) >= 0 &&
            (!OrderedEnemyMovement || member == SelectedMember) &&
            (ActingMember < 0 || ActingMember == member);

        public bool BeginCutscene()
        {
            if (Phase != BattlePhase.Exploration) return false;
            Phase = BattlePhase.Cutscene;
            return true;
        }

        public bool CancelCutscene()
        {
            if (Phase != BattlePhase.Cutscene) return false;
            Phase = BattlePhase.Exploration;
            return true;
        }

        public bool Pause()
        {
            if (Phase == BattlePhase.Paused) return false;
            resumePhase = Phase;
            Phase = BattlePhase.Paused;
            return true;
        }

        public bool Resume()
        {
            if (Phase != BattlePhase.Paused) return false;
            Phase = resumePhase;
            return true;
        }

        public void Reset()
        {
            Phase = resumePhase = BattlePhase.Exploration;
            SelectedMember = -1;
            ActingMember = -1; ParticipantCount = PlayerTurn = 0;
            Side = BattleSide.Player; Result = BattleResult.Ongoing;
            MovementSpent = ActionSpent = false;
            IsTraining = false; OrderedEnemyMovement = false; nextPlayerSlot = 0;
            activeCells = blockedCells = 0;
            borrowedPending = borrowedActive = false;
            ClearOccupancy();
        }

        private void ClearOccupancy()
        {
            for (int i = 0; i < occupants.Length; i++) occupants[i] = -1;
            for (int i = 0; i < memberCells.Length; i++) { memberCells[i] = -1; health[i] = 0; ultimateCharge[i] = 0; }
        }

        // The first successful action locks both selection and action ownership until the next turn.
        public bool CanAct(int member) => Phase == BattlePhase.Battle && Result == BattleResult.Ongoing &&
            CellOf(member) >= 0 && !IsKnockedOut(member) &&
            (Side == BattleSide.Player ? member < 4 : member >= 4) &&
            (!OrderedEnemyMovement || Side != BattleSide.Player || member == SelectedMember) &&
            (ActingMember < 0 || ActingMember == member);

        public ulong MoveTargets(int member)
        {
            if (!CanAct(member) || MovementSpent) return 0;
            ulong mask = 0;
            for (int cell = 0; cell < 64; cell++)
                if (OccupantAt(cell) < 0 && Reachable(member, cell, false)) mask |= 1UL << cell;
            return mask;
        }
        public ulong AttackTargets(int member)
        {
            if (!CanAct(member) || ActionSpent) return 0;
            ulong mask = 0;
            for (int target = 0; target < ParticipantCount; target++)
                if (!SameTeam(member, target) && !IsKnockedOut(target) && Reachable(member, CellOf(target), true))
                    mask |= 1UL << CellOf(target);
            return mask;
        }
        public bool CanFuse(int member)
        {
            return OrderedEnemyMovement && (member == 0 || member == 1) && CanAct(member)
                && !MovementSpent && !ActionSpent && ultimateCharge[member] == 3
                && !IsKnockedOut(1 - member) && Reachable(member, CellOf(1 - member), false);
        }

        private static int Distance(int a, int b) =>
            Math.Max(Math.Abs(a % 8 - b % 8), Math.Abs(a / 8 - b / 8));

        public ulong AbilityTargets(int member, int slot)
        {
            if (!CanAct(member) || ActionSpent || slot < 0 || slot > 2) return 0;
            bool fusion = slot == 2;
            if (fusion && !CanFuse(member)) return 0;
            if (!fusion && !UsesMiaSkills(member)) return AttackTargets(member);
            int source = CellOf(fusion ? 1 - member : member);
            ulong result = 0;
            for (int enemy = 4; enemy < ParticipantCount; enemy++)
            {
                int cell = CellOf(enemy);
                if (IsKnockedOut(enemy)) continue;
                if (slot == 1 ? Distance(source, cell) == 1 : FireLine(source, cell))
                    result |= 1UL << cell;
            }
            return result;
        }

        private bool FireLine(int source, int cell)
        {
            int dx = cell % 8 - source % 8, dy = cell / 8 - source / 8;
            if (dx != 0 && dy != 0 || source == cell) return false;
            int step = Math.Sign(dx) + 8 * Math.Sign(dy);
            for (int i = source + step; i != cell; i += step)
                if (IsBlocked(i)) return false;
            return true;
        }

        public bool TryAbility(int member, int slot, int target)
        {
            if (!Contains(AbilityTargets(member, slot), target)) return false;
            if (slot != 2 && !UsesMiaSkills(member)) return TryAttack(member, target);
            bool fusion = slot == 2;
            int center = slot == 1 ? CellOf(member) : target;
            for (int i = 0; i < ParticipantCount; i++)
            {
                // During fusion both participants stand on the receiving partner's cell.
                int cell = fusion && i == member ? CellOf(1 - member) : CellOf(i);
                if (slot == 0 ? cell != target : Distance(center, cell) > 1) continue;
                if (slot == 1 && i == member) continue;
                health[i] = Math.Max(0, health[i] - (i < 4 ? AttackDamage / 2 : AttackDamage));
            }
            ActingMember = member;
            ActionSpent = true;
            ultimateCharge[member] = fusion ? 0 : Math.Min(3, ultimateCharge[member] + 1);
            if (fusion) { MovementSpent = true; borrowedPending = true; }
            UpdateResult();
            return true;
        }

        // Baseline geometry, deliberately not the research cache or lazy composite generator.
        // Straight movement and attack rays stop at every participant, including KO bodies.
        // The knight jumps over intermediate cells but cannot land on an occupied cell.
        private bool Reachable(int member, int target, bool attack)
        {
            int origin = CellOf(member);
            if (origin < 0 || target == origin || IsBlocked(target)) return false;
            int dx = target % 8 - origin % 8, dy = target / 8 - origin / 8;
            int ax = Math.Abs(dx), ay = Math.Abs(dy);
            switch (RoleOf(member))
            {
                case ChessRole.King: return Math.Max(ax, ay) == 1;
                case ChessRole.Knight: return ax * ay == 2;
                case ChessRole.Rook: if (dx != 0 && dy != 0) return false; break;
                case ChessRole.Bishop: if (ax != ay) return false; break;
            }
            int sx = Math.Sign(dx), sy = Math.Sign(dy);
            for (int i = 1; i < Math.Max(ax, ay); i++)
            {
                int cell = (origin / 8 + sy * i) * 8 + origin % 8 + sx * i;
                if (IsBlocked(cell)) return false;
                if (OccupantAt(cell) >= 0) return false;
            }
            return true;
        }
        private static bool Contains(ulong mask, int cell) => cell >= 0 && cell < 64 && (mask & (1UL << cell)) != 0;
        public bool TryMove(int member, int target)
        {
            if (!Contains(MoveTargets(member), target)) return false;
            ActingMember = member;
            if (member < 4) SelectedMember = member;
            occupants[memberCells[member]] = -1; occupants[target] = member; memberCells[member] = target;
            MovementSpent = true; return true;
        }
        public bool TryAttack(int member, int targetCell)
        {
            if (!Contains(AttackTargets(member), targetCell)) return false;
            ActingMember = member;
            if (member < 4) SelectedMember = member;
            int target = OccupantAt(targetCell);
            health[target] = Math.Max(0, health[target] - (member < 4 ? AttackDamage : EnemyAttackDamage));
            if (member < 4) ultimateCharge[member] = Math.Min(3, ultimateCharge[member] + 1);
            ActionSpent = true;
            UpdateResult();
            return true;
        }
        private void UpdateResult()
        {
            if (health[0] == 0) borrowedPending = borrowedActive = false;
            bool playerAlive = false, enemyAlive = false;
            for (int i = 0; i < ParticipantCount; i++)
                if (health[i] > 0) { if (i < 4) playerAlive = true; else enemyAlive = true; }
            if (ParticipantCount > 4 && !enemyAlive) Result = BattleResult.Victory;
            else if (!playerAlive) Result = BattleResult.PartyKnockedOut;
        }
        public bool EndPlayerTurn()
        {
            if (Phase != BattlePhase.Battle || Side != BattleSide.Player || Result != BattleResult.Ongoing) return false;
            MovementSpent = ActionSpent = false; ActingMember = -1;
            if (ParticipantCount == 4) { PlayerTurn++; return true; }
            borrowedActive = false;
            BeginEnemyTurn();
            if (IsTraining) CompleteEnemyTurn();
            return true;
        }
        private void BeginEnemyTurn()
        {
            UpdateResult();
            if (Result != BattleResult.Ongoing) return;
            ActingMember = -1;
            Side = BattleSide.Enemy;
            for (int i = 0; i < ParticipantCount - 4; i++)
            {
                int candidate = 4 + (nextEnemy - 4 + i) % (ParticipantCount - 4);
                if (!IsKnockedOut(candidate))
                { ActingMember = candidate; nextEnemy = 4 + (candidate - 3) % (ParticipantCount - 4); break; }
            }
        }
        public bool CompleteEnemyTurn()
        {
            if (Phase != BattlePhase.Battle || Side != BattleSide.Enemy || Result != BattleResult.Ongoing) return false;
            UpdateResult();
            if (Result != BattleResult.Ongoing) return false;
            Side = BattleSide.Player; ActingMember = -1;
            if (OrderedEnemyMovement)
            {
                for (int i = 0; i < 4; i++)
                {
                    int candidate = (nextPlayerSlot + i) % 4;
                    if (IsKnockedOut(candidate)) continue;
                    SelectedMember = candidate;
                    if (candidate == 0) { borrowedActive = borrowedPending; borrowedPending = false; }
                    nextPlayerSlot = (candidate + 1) % 4;
                    break;
                }
            }
            MovementSpent = ActionSpent = false; PlayerTurn++; return true;
        }
    }
}
