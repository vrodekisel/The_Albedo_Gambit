using UnityEngine;
using UnityEngine.AI;

namespace AlbedoGambit.Prototype
{
    // Small visibility graph around the other three capsules. Static geometry is still
    // handled by NavMesh; all actual movement and contact resolution stay in Rigidbody.
    internal sealed class PartyLocalNavigation
    {
        private const int RingPoints = 12;
        private readonly Vector3[] nodes = new Vector3[40];
        private readonly float[] costs = new float[40];
        private readonly int[] parents = new int[40];
        private readonly bool[] visited = new bool[40];
        private readonly Vector3[] centers = new Vector3[3];
        private readonly float[] radii = new float[3];
        private int obstacles;

        public Vector3 Direction(int actor, Vector3 from, Vector3 goal, Vector3 previousDirection,
            CapsuleMotor[] members, PrototypeNavigation navigation)
        {
            if (!navigation.TryProjectGround(from, out from, 2)) return Vector3.zero;
            obstacles = 0;
            float ownRadius = members[actor].GetComponent<CapsuleCollider>().radius;
            for (int i = 0; i < members.Length; i++)
            {
                if (i == actor) continue;
                centers[obstacles] = members[i].Body.position;
                radii[obstacles++] = ownRadius + members[i].GetComponent<CapsuleCollider>().radius + .06f;
            }
            // A slot can temporarily be occupied during a turn. Wait beside it, then
            // recompute as its occupant moves; never aim inside another capsule.
            for (int pass = 0; pass < 4; pass++)
                for (int i = 0; i < obstacles; i++)
                {
                    var away = Flat(goal - centers[i]);
                    if (away.magnitude >= radii[i] + .03f) continue;
                    if (away.sqrMagnitude < .001f) away = Flat(from - centers[i]);
                    if (away.sqrMagnitude < .001f) away = Vector3.right;
                    goal = centers[i] + away.normalized * (radii[i] + .04f);
                }
            if (!navigation.TryProjectGround(goal, out goal, .2f)) return Vector3.zero;
            if (Clear(from, goal)) return Flat(goal - from).normalized;
            nodes[0] = from; nodes[1] = goal;
            int count = 2;
            for (int i = 0; i < obstacles; i++)
                for (int p = 0; p < RingPoints; p++)
                {
                    float angle = p * Mathf.PI * 2 / RingPoints;
                    var point = centers[i] + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (radii[i] + .2f);
                    if (!navigation.TryProjectGround(point, out point, .12f) || !Outside(point)) continue;
                    nodes[count++] = point;
                }
            for (int i = 0; i < count; i++) { costs[i] = float.PositiveInfinity; visited[i] = false; parents[i] = -1; }
            costs[0] = 0;
            for (int step = 0; step < count; step++)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                    if (!visited[i] && !float.IsPositiveInfinity(costs[i]) && (best < 0 || costs[i] < costs[best])) best = i;
                if (best < 0) break;
                if (best == 1)
                {
                    int waypoint = 1;
                    while (parents[waypoint] > 0) waypoint = parents[waypoint];
                    return Flat(nodes[waypoint] - from).normalized;
                }
                visited[best] = true;
                for (int i = 1; i < count; i++)
                {
                    if (visited[i]) continue;
                    Vector3 delta = Flat(nodes[i] - nodes[best]);
                    float cost = costs[best] + delta.magnitude;
                    // Prefer continuing an established detour instead of oscillating sides.
                    if (best == 0) cost += .25f * (1 - Vector3.Dot(previousDirection, delta.normalized));
                    if (cost >= costs[i] || !Clear(nodes[best], nodes[i])) continue;
                    costs[i] = cost; parents[i] = best;
                }
            }
            return Vector3.zero;
        }

        private bool Outside(Vector3 point)
        {
            for (int i = 0; i < obstacles; i++)
                if (Flat(point - centers[i]).sqrMagnitude < radii[i] * radii[i]) return false;
            return true;
        }
        private bool Clear(Vector3 start, Vector3 end)
        {
            Vector3 delta = Flat(end - start);
            for (int i = 0; i < obstacles; i++)
            {
                Vector3 offset = Flat(start - centers[i]);
                // A physical contact may start inside the planning margin; allow escape.
                if (offset.sqrMagnitude < radii[i] * radii[i] && Vector3.Dot(offset, delta) >= 0) continue;
                float t = delta.sqrMagnitude < .0001f ? 0 : Mathf.Clamp01(-Vector3.Dot(offset, delta) / delta.sqrMagnitude);
                if ((offset + delta * t).sqrMagnitude < radii[i] * radii[i]) return false;
            }
            return !NavMesh.Raycast(start, end, out _, NavMesh.AllAreas);
        }
        private static Vector3 Flat(Vector3 value) { value.y = 0; return value; }
    }
}
