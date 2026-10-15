using UnityEngine;

namespace AlbedoGambit.Prototype
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class CapsuleMotor : MonoBehaviour
    {
        [SerializeField, Min(1)] private float acceleration = 32f;
        [SerializeField, Min(1)] private float braking = 40f;
        [SerializeField, Min(.1f)] private float jumpHeight = 1.35f;
        [SerializeField, Range(0, 1)] private float airControl = .65f;
        [SerializeField, Range(0, 70)] private float maxGroundAngle = 50f;
        [SerializeField] private LayerMask groundMask = ~0;
        private Rigidbody body;
        private CapsuleCollider capsule;
        private readonly RaycastHit[] groundHits = new RaycastHit[24];
        private float coyoteTime;
        private float jumpBuffer;
        private float groundLock;
        public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();
        public bool IsGrounded { get; private set; }
        public int JumpCount { get; private set; }

        // Called once per physics tick by the party, also usable by other controllers.
        public bool Tick(Vector3 desiredVelocity, bool jumpPressed, float dt)
        {
            if (capsule == null) capsule = GetComponent<CapsuleCollider>();
            groundLock = Mathf.Max(0, groundLock - dt);
            IsGrounded = groundLock <= 0 && Body.linearVelocity.y <= .5f && CheckGround();
            coyoteTime = IsGrounded ? .1f : Mathf.Max(0, coyoteTime - dt);
            jumpBuffer = jumpPressed ? .12f : Mathf.Max(0, jumpBuffer - dt);
            var velocity = Body.linearVelocity;
            var horizontal = new Vector3(velocity.x, 0, velocity.z);
            desiredVelocity.y = 0;
            if (IsGrounded && !jumpPressed && desiredVelocity.sqrMagnitude > .01f)
                TryLowStep(desiredVelocity.normalized);
            float rate = desiredVelocity.sqrMagnitude < .001f ? braking : acceleration;
            horizontal = Vector3.MoveTowards(horizontal, desiredVelocity,
                rate * (IsGrounded ? 1 : airControl) * dt);
            velocity.x = horizontal.x;
            velocity.z = horizontal.z;
            bool jumped = jumpBuffer > 0 && coyoteTime > 0;
            if (jumped)
            {
                velocity.y = Mathf.Sqrt(2 * Mathf.Abs(Physics.gravity.y) * jumpHeight);
                jumpBuffer = coyoteTime = 0;
                groundLock = .15f;
                IsGrounded = false;
                JumpCount++;
            }
            Body.linearVelocity = velocity;
            return jumped;
        }

        private bool CheckGround()
        {
            // The root uses unit scale; visual size is independent of the collider.
            Vector3 bottomSphere = transform.TransformPoint(capsule.center)
                + Vector3.down * (capsule.height * .5f - capsule.radius);
            int count = Physics.SphereCastNonAlloc(bottomSphere + Vector3.up * .05f,
                capsule.radius * .9f, Vector3.down, groundHits,
                .05f + capsule.radius * .1f + .09f, groundMask, QueryTriggerInteraction.Ignore);
            float minimumNormal = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.collider.attachedRigidbody == Body ||
                    hit.collider.GetComponentInParent<PartyMember>() != null) continue;
                if (hit.normal.y >= minimumNormal) return true;
            }
            return false;
        }

        private bool EnvironmentRay(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
        {
            closest = default;
            float nearest = float.MaxValue;
            int count = Physics.RaycastNonAlloc(origin, direction, groundHits, distance, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.collider.attachedRigidbody == Body || hit.collider.GetComponentInParent<PartyMember>() != null) continue;
                if (hit.distance < nearest) { closest = hit; nearest = hit.distance; }
            }
            return nearest < float.MaxValue;
        }

        private void TryLowStep(Vector3 direction)
        {
            // Small board lip only; tall cubes still block movement.
            var feet = Body.position;
            float reach = capsule.radius + .12f;
            if (!EnvironmentRay(feet + Vector3.up * .06f, direction, reach, out _) ||
                EnvironmentRay(feet + Vector3.up * .32f, direction, reach, out _)) return;
            if (EnvironmentRay(feet + direction * reach + Vector3.up * .32f, Vector3.down, .29f, out var top)
                && top.normal.y > .8f)
            {
                float height = top.point.y - feet.y;
                if (height > .03f && height <= .3f) Body.position += Vector3.up * (height + .01f);
            }
        }

        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            Body.position = position;
            Body.rotation = rotation;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            coyoteTime = jumpBuffer = groundLock = 0;
            IsGrounded = false;
        }
    }
}
