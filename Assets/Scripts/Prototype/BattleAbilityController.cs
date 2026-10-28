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
        private const float FlightSeconds = .45f;
        public bool IsAiming { get; private set; }
        public bool IsProjectileFlying => projectile != null;

        public void Bind(PartyChainController controller, BoardGrid grid)
        { Cancel(); party = controller; board = grid; }

        public bool BeginAim(int slot)
        {
            if (slot < 0 || slot > 1 || party == null || party.IsBattleMoving) return false;
            var state = party.Battle;
            if (!state.OrderedEnemyMovement || !state.CanAct(party.SelectedIndex) || state.ActionSpent) return false;
            IsAiming = true;
            return true;
        }

        public bool TryFire(int targetCell)
        {
            if (!IsAiming || party == null || board == null) return false;
            var state = party.Battle;
            actor = party.SelectedIndex;
            if (targetCell < 0 || targetCell >= 64 ||
                (state.AttackTargets(actor) & (1UL << targetCell)) == 0) return false;
            target = targetCell;
            IsAiming = false;
            elapsed = 0;
            origin = party.Members[actor].transform.position + Vector3.up * 1.25f;
            destination = board.CellCenter(targetCell % 8, targetCell / 8) + Vector3.up * 1.1f;
            projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "Ability Projectile";
            projectile.transform.SetParent(transform, true);
            projectile.transform.SetPositionAndRotation(origin, Quaternion.identity);
            projectile.transform.localScale = Vector3.one * .55f;
            var collider = projectile.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var heroVisual = party.Members[actor].transform.Find("Capsule Visual");
            projectile.GetComponent<Renderer>().sharedMaterial = heroVisual.GetComponent<Renderer>().sharedMaterial;
            party.Hud?.ShowAbilityFlight();
            return true;
        }

        public void TickAbility(float dt)
        {
            if (projectile == null || party == null) return;
            elapsed += Mathf.Max(0, dt);
            float t = Mathf.Clamp01(elapsed / FlightSeconds);
            projectile.transform.position = Vector3.Lerp(origin, destination, Mathf.SmoothStep(0, 1, t))
                + Vector3.up * Mathf.Sin(Mathf.PI * t) * .45f;
            if (t < 1) return;
            Destroy(projectile); projectile = null;
            int victim = party.Battle.OccupantAt(target);
            if (party.Battle.TryAttack(actor, target))
                party.Hud?.ShowAbilityHit(victim, party.Battle.HealthOf(victim), party.Battle.UltimateChargeOf(actor));
            actor = target = -1;
        }

        public void Cancel()
        {
            IsAiming = false;
            if (projectile != null) Destroy(projectile);
            projectile = null; actor = target = -1; elapsed = 0;
        }
        private void OnDisable() { Cancel(); }
    }
}
