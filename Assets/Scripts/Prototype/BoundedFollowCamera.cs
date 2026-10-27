using UnityEngine;

namespace AlbedoGambit.Prototype
{
    // Bounds are expressed in the original Blender camera plane, not world X/Z.
    [RequireComponent(typeof(Camera))]
    public sealed class BoundedFollowCamera : MonoBehaviour
    {
        [SerializeField] private PartyChainController party;
        [SerializeField] private BoardGrid board;
        [SerializeField] private Vector3 referencePosition;
        [SerializeField] private Vector2 artworkHalfSize;
        [SerializeField] private float followTime = .3f;
        [SerializeField] private float referenceHalfHeight = 8.4375f;
        private Vector2 velocity;
        private Camera view;

        public void Configure(PartyChainController group, BoardGrid grid, Vector3 origin, Vector2 halfSize)
        {
            party = group; board = grid; referencePosition = origin; artworkHalfSize = halfSize;
            referenceHalfHeight = GetComponent<Camera>().orthographicSize;
        }

        public Vector2 FrameOffset => new Vector2(Vector3.Dot(transform.position - referencePosition, transform.right),
            Vector3.Dot(transform.position - referencePosition, transform.up));

        public Vector2 FrameLimits
        {
            get
            {
                if (!view) view = GetComponent<Camera>();
                return new Vector2(Mathf.Max(0, artworkHalfSize.x - view.orthographicSize * view.aspect),
                    Mathf.Max(0, artworkHalfSize.y - view.orthographicSize));
            }
        }

        public void Follow(float dt, bool snap = false)
        {
            if (!snap && dt <= 0) return;
            if (!party || party.Members == null || party.Members.Length == 0) return;
            if (!view) view = GetComponent<Camera>();
            // Extremely wide windows must not reveal space outside the artwork.
            view.orthographicSize = Mathf.Min(referenceHalfHeight, artworkHalfSize.y, artworkHalfSize.x / view.aspect);
            // Jump height must not pull the camera up. Battle frames the complete board.
            Vector3 target = party.Mode == PartyMode.Chess && board
                ? board.transform.position : party.Members[party.SelectedIndex].transform.position;
            target.y = 0;
            Vector3 delta = target - referencePosition;
            Vector2 limit = FrameLimits;
            Vector2 desired = new Vector2(Mathf.Clamp(Vector3.Dot(delta, transform.right), -limit.x, limit.x),
                Mathf.Clamp(Vector3.Dot(delta, transform.up), -limit.y, limit.y));
            Vector2 offset = snap ? desired : Vector2.SmoothDamp(FrameOffset, desired, ref velocity, followTime, Mathf.Infinity, dt);
            offset.x = Mathf.Clamp(offset.x, -limit.x, limit.x);
            offset.y = Mathf.Clamp(offset.y, -limit.y, limit.y);
            Vector3 horizontalUp = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            transform.position = referencePosition + transform.right * offset.x + horizontalUp * (offset.y / horizontalUp.sqrMagnitude);
        }

        private void LateUpdate() { Follow(Time.deltaTime); }
    }
}
