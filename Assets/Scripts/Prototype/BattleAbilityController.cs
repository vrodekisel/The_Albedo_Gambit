using UnityEngine;

namespace AlbedoGambit.Prototype
{
    // A scene-opted-in presentation adapter. The session owns targets, health and charge.
    public sealed class BattleAbilityController : MonoBehaviour
    {
        private PartyChainController party;
        private BoardGrid board;
        private GameObject projectile;
        private Vector3 origin, destination;
        private float elapsed;
        private int actor = -1, target = -1;
        private int firingSlot;
        private enum FusionStep { None, Approach, Return }
        private FusionStep fusionStep;
        private Vector3 home, partner;
        private bool partnerChosen;
        public bool ChoosingFusionEnemy => IsAiming && aimingSlot == 2 && partnerChosen;
        public bool ChoosingPartner => IsAiming && aimingSlot == 2 && !partnerChosen;
        public ulong Targets => ChoosingPartner ? 1UL << party.Battle.CellOf(1 - party.SelectedIndex) : party.Battle.AbilityTargets(party.SelectedIndex, aimingSlot);
        private const float FlightSeconds = .45f;
        public bool IsAiming { get; private set; }
        private int aimingSlot = -1;
        public bool IsProjectileFlying => projectile != null || fusionStep != FusionStep.None;

        public void Bind(PartyChainController controller, BoardGrid grid)
        { Cancel(); party = controller; board = grid; }

        public bool BeginAim(int slot)
        {
            if (slot < 0 || slot > 2 || party == null || party.IsBattleMoving) return false;
            var state = party.Battle;
            if (!state.OrderedEnemyMovement || !state.CanAct(party.SelectedIndex) || state.ActionSpent) return false;
            if (IsAiming && aimingSlot == slot) { Cancel(); return true; }
            if ((slot == 2 || slot == 1 && state.UsesMiaSkills(party.SelectedIndex)) && state.AbilityTargets(party.SelectedIndex, slot) == 0) return false;
            partnerChosen = false;
            aimingSlot = slot;
            IsAiming = true;
            return true;
        }

        public bool TryFire(int targetCell)
        {
            if (!IsAiming || party == null || board == null) return false;
            var state = party.Battle;
            if (party.IsBattleMoving) return false;
            actor = party.SelectedIndex;
            if (ChoosingPartner)
            {
                if (targetCell != state.CellOf(1 - actor) || !state.CanFuse(actor)) return false;
                partnerChosen = true;
                return true;
            }
            return Fire(actor, targetCell);
        }

        public bool TryFireEnemy(int participant, int targetCell)
        {
            if (party == null || participant < 4 || party.Battle.Side != BattleSide.Enemy || IsProjectileFlying) return false;
            return Fire(participant, targetCell);
        }

        private bool Fire(int participant, int targetCell)
        {
            var state = party.Battle;
            actor = participant;
            if (targetCell < 0 || targetCell >= 64 ||
                ((actor < 4 ? state.AbilityTargets(actor, aimingSlot) : state.AttackTargets(actor)) & (1UL << targetCell)) == 0) return false;
            target = targetCell;
            IsAiming = false;
            firingSlot = aimingSlot;
            aimingSlot = -1;
            if (actor < 4 && firingSlot == 1 && state.UsesMiaSkills(actor))
            {
                if (!state.TryAbility(actor, firingSlot, target)) return false;
                party.Hud?.ShowFlameStrike(state.OccupantAt(target), state.HealthOf(state.OccupantAt(target)));
                GetComponent<BattleBoardView>()?.FlashStrike(state.CellOf(actor));
                return true;
            }
            if (firingSlot == 2)
            {
                home = party.Members[actor].Body.position;
                partner = party.Members[1 - actor].Body.position;
                fusionStep = FusionStep.Approach;
                elapsed = 0;
                return true;
            }
            Launch();
            return true;
        }

        private void Launch()
        {
            elapsed = 0;
            var source = actor < 4 ? party.Members[actor].transform
                : GetComponent<BattleEnemySpawner>().EnemyAt(actor - 4).transform;
            origin = source.position + Vector3.up * 1.25f;
            destination = board.CellCenter(target % 8, target / 8) + Vector3.up * 1.1f;
            projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "Ability Projectile";
            projectile.transform.SetParent(transform, true);
            projectile.transform.SetPositionAndRotation(origin, Quaternion.identity);
            projectile.transform.localScale = Vector3.one * .55f;
            var collider = projectile.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var heroVisual = source.Find("Capsule Visual");
            projectile.GetComponent<Renderer>().sharedMaterial = heroVisual.GetComponent<Renderer>().sharedMaterial;
            party.Hud?.ShowAbilityFlight();
        }

        public void TickAbility(float dt)
        {
            if (fusionStep != FusionStep.None)
            {
                bool approaching = fusionStep == FusionStep.Approach;
                elapsed += dt;
                float progress = Mathf.Clamp01(elapsed / FlightSeconds);
                var point = Vector3.Lerp(approaching ? home : partner, approaching ? partner : home, progress);
                var motor = party.Members[actor];
                motor.transform.position = motor.Body.position = point;
                if (progress < 1) return;
                fusionStep = FusionStep.None;
                if (approaching) Launch();
                else party.EndPreviewTurn();
                return;
            }
            if (projectile == null || party == null) return;
            elapsed += Mathf.Max(0, dt);
            float t = Mathf.Clamp01(elapsed / FlightSeconds);
            projectile.transform.position = Vector3.Lerp(origin, destination, Mathf.SmoothStep(0, 1, t))
                + Vector3.up * Mathf.Sin(Mathf.PI * t) * .45f;
            if (t < 1) return;
            Destroy(projectile); projectile = null;
            int victim = party.Battle.OccupantAt(target);
            if (actor < 4 ? party.Battle.TryAbility(actor, firingSlot, target) : party.Battle.TryAttack(actor, target))
            {
                if (firingSlot == 2) GetComponent<BattleBoardView>()?.FlashStrike(target, true);
                if (actor < 4) party.Hud?.ShowAbilityHit(victim, party.Battle.HealthOf(victim), party.Battle.UltimateChargeOf(actor));
                else party.Hud?.ShowEnemyHit(victim, party.Battle.HealthOf(victim));
            }
            if (firingSlot == 2) { fusionStep = FusionStep.Return; elapsed = 0; }
            else actor = target = -1;
        }

        public void Cancel()
        {
            IsAiming = false;
            aimingSlot = -1;
            if (projectile != null) Destroy(projectile);
            fusionStep = FusionStep.None;
            projectile = null; actor = target = -1; elapsed = 0;
        }
        private void OnDisable() { Cancel(); }
    }
}
