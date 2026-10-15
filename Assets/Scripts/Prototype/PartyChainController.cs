using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AI;

namespace AlbedoGambit.Prototype
{
    public enum PartyMode { Chained, Separated, Regrouping, Chess }

    public sealed class PartyChainController : MonoBehaviour
    {
        [Tooltip("Order is A, B, C, D. Only A reads player input.")]
        [SerializeField] private CapsuleMotor[] members;
        [SerializeField, Min(.1f)] private float moveSpeed = 3.5f;
        [SerializeField, Min(.7f)] private float spacing = 1.1f;
        [SerializeField] private Transform movementCamera;
        [SerializeField] private float respawnBelowY = -12;
        private readonly List<PathPoint> path = new List<PathPoint>();
        private readonly List<JumpPoint> jumps = new List<JumpPoint>();
        private Vector3[] starts;
        private Quaternion[] rotations;
        private float[] progress;
        private Vector2 input;
        private bool jumpInput;
        private bool resetInput;
        private float elapsed;
        private bool initialized;
        private PartyMode mode;
        private int selected;
        private PrototypeNavigation navigation;
        private readonly NavMeshPath[] routes = new NavMeshPath[4];
        private readonly Vector3[][] corners = new Vector3[4][];
        private readonly float[] nextRouteTime = new float[4];
        private readonly Vector3[] routeTargets = new Vector3[4];
        private readonly Vector3[] regroupTargets = new Vector3[4];
        private readonly Vector3[] explorationPositions = new Vector3[4];
        private readonly Quaternion[] explorationRotations = new Quaternion[4];
        private PartyMode explorationMode;
        private int explorationSelection;
        private Vector3 lastDirection = Vector3.right;
        private readonly Transform[] selectionRings = new Transform[4];
        private string statusMessage = "";
        public PartyMode Mode => mode;
        public int SelectedIndex => selected;
        private struct PathPoint { public Vector3 position; public float distance; }
        private sealed class JumpPoint
        {
            public float distance, time;
            public bool stationary;
            public int consumedMask = 1;
        }
        public CapsuleMotor[] Members => members;
        public float Spacing => spacing;
        public float MoveSpeed => moveSpeed;
        public int StoredPathPoints => path.Count;

        public void Configure(CapsuleMotor[] orderedMembers) { members = orderedMembers; }

        private void Awake() { Initialize(); }

        public void Initialize()
        {
            if (initialized) return;
            if (members == null || members.Length != 4)
            {
                Debug.LogError("Party requires exactly four motors ordered A, B, C, D.", this);
                enabled = false;
                return;
            }
            starts = new Vector3[4]; rotations = new Quaternion[4]; progress = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (members[i] == null) { enabled = false; return; }
                starts[i] = members[i].Body.position;
                routes[i] = new NavMeshPath();
                rotations[i] = members[i].Body.rotation;
                selectionRings[i] = members[i].transform.Find("Selection Ring");
                for (int j = 0; j < i; j++)
                    Physics.IgnoreCollision(members[i].GetComponent<Collider>(), members[j].GetComponent<Collider>());
            }
            initialized = true;
            navigation = FindFirstObjectByType<PrototypeNavigation>();
            RebuildTrail();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) { input = Vector2.zero; return; }
            input = new Vector2(
                (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) -
                (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) -
                (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
            input = Vector2.ClampMagnitude(input, 1);
            jumpInput |= keyboard.spaceKey.wasPressedThisFrame;
            resetInput |= keyboard.rKey.wasPressedThisFrame;
            if (keyboard.gKey.wasPressedThisFrame) ToggleGroup();
            if (keyboard.digit1Key.wasPressedThisFrame) SelectMember(0);
            if (keyboard.digit2Key.wasPressedThisFrame) SelectMember(1);
            if (keyboard.digit3Key.wasPressedThisFrame) SelectMember(2);
            if (keyboard.digit4Key.wasPressedThisFrame) SelectMember(3);
            if (keyboard.tKey.wasPressedThisFrame)
            {
                if (mode == PartyMode.Chess) ExitChessMode();
                else EnterChessMode(FindFirstObjectByType<BoardGrid>());
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { input = Vector2.zero; jumpInput = resetInput = false; }
        }

        private void FixedUpdate()
        {
            if (resetInput) ResetParty();
            resetInput = false;
            if (movementCamera == null && Camera.main != null) movementCamera = Camera.main.transform;
            Vector3 forward = movementCamera == null ? Vector3.forward : movementCamera.forward;
            forward.y = 0; forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Tick(forward * input.y + right * input.x, jumpInput, Time.fixedDeltaTime);
            jumpInput = false;
        }

        // World-space command is intentionally separate from keyboard/camera input for reuse and physics checks.
        public void Tick(Vector3 movement, bool jump, float dt)
        {
            Initialize();
            if (!initialized) return;
            elapsed += dt;
            foreach (var member in members)
                if (member.Body.position.y < respawnBelowY) { ResetParty(); return; }
            movement.y = 0;
            movement = Vector3.ClampMagnitude(movement, 1);
            UpdateSelection();
            if (mode == PartyMode.Chess) return;
            if (mode == PartyMode.Separated)
            {
                for (int i = 0; i < 4; i++)
                    members[i].Tick(i == selected ? movement * moveSpeed : Vector3.zero, i == selected && jump, dt);
                return;
            }
            if (mode == PartyMode.Regrouping)
            {
                bool arrived = true;
                members[0].Tick(Vector3.zero, false, dt);
                for (int i = 1; i < 4; i++)
                {
                    float distance = FlatDistance(members[i].Body.position, regroupTargets[i]);
                    arrived &= distance < .23f;
                    members[i].Tick(Navigate(i, regroupTargets[i], Mathf.Min(moveSpeed * 1.5f, distance * 5)), false, dt);
                }
                if (arrived)
                {
                    mode = PartyMode.Chained; selected = 0; RebuildTrail(); ClearRoutes();
                }
                return;
            }
            if (movement.sqrMagnitude > .01f) lastDirection = movement.normalized;
            RecordLeader(false);
            if (members[0].Tick(movement * moveSpeed, jump, dt))
            {
                RecordLeader(true);
                jumps.Add(new JumpPoint { distance = path[path.Count - 1].distance,
                    time = elapsed, stationary = movement.sqrMagnitude < .01f });
            }
            float total = path[path.Count - 1].distance;
            for (int i = 1; i < 4; i++)
            {
                float limit = Mathf.Max(path[0].distance, total - spacing * i);
                Vector3 position = members[i].Body.position;
                // A short look-ahead follows corners without aiming directly at the leader.
                for (int n = 0; n < 6; n++)
                {
                    float next = Mathf.Min(limit, progress[i] + .12f);
                    if (next <= progress[i] || FlatDistance(position, Sample(next)) > .16f) break;
                    progress[i] = next;
                }
                float aimDistance = Mathf.Min(limit, progress[i] + .16f);
                Vector3 delta = Sample(aimDistance) - position;
                delta.y = 0;
                // Slow only at the stopping point, not at each breadcrumb.
                float remaining = Mathf.Max(0, limit - progress[i]);
                float speed = Mathf.Min(moveSpeed * 1.3f, (remaining + delta.magnitude) * 9f);
                Vector3 velocity = delta.magnitude < .035f ? Vector3.zero : Navigate(i, Sample(aimDistance), speed);
                JumpPoint pending = null;
                foreach (var marker in jumps)
                {
                    if ((marker.consumedMask & (1 << i)) != 0) continue;
                    bool ready = elapsed >= marker.time + .18f * i;
                    if (ready) { pending = marker; break; }
                }
                if (members[i].Tick(velocity, pending != null, dt) && pending != null)
                    pending.consumedMask |= 1 << i;
            }
            float earliest = Mathf.Min(progress[1], Mathf.Min(progress[2], progress[3]));
            int remove = 0;
            while (remove + 1 < path.Count - 1 && path[remove + 1].distance < earliest - 2) remove++;
            if (remove > 0) path.RemoveRange(0, remove);
            jumps.RemoveAll(p => p.consumedMask == 15);
            // A blocked companion can lag behind without accumulating an unlimited route.
            if (path.Count > 12000) { BeginRegroup(); statusMessage = "Длинный маршрут: сбор к A"; }
        }

        private Vector3 Navigate(int index, Vector3 target, float speed)
        {
            Vector3 position = members[index].Body.position;
            if (navigation == null) navigation = FindFirstObjectByType<PrototypeNavigation>();
            if (navigation == null) return FlatDirection(target - position) * speed;
            if (elapsed >= nextRouteTime[index] || FlatDistance(target, routeTargets[index]) > .6f)
            {
                corners[index] = navigation.FindPath(position, target, routes[index]) ? routes[index].corners : null;
                routeTargets[index] = target;
                nextRouteTime[index] = elapsed + .25f;
            }
            var points = corners[index];
            if (points == null || points.Length == 0) return Vector3.zero;
            Vector3 aim = target;
            bool approachingCorner = false;
            // NavMesh corner zero is the projected start; do not steer back towards it.
            for (int c = 1; c < points.Length - 1; c++)
                if (FlatDistance(position, points[c]) > .13f) { aim = points[c]; approachingCorner = true; break; }
            float distance = FlatDistance(position, aim);
            // Trail targets intentionally sit a short distance ahead. Braking for every
            // breadcrumb would make followers permanently slower than the leader.
            return distance < .04f ? Vector3.zero : FlatDirection(aim - position) *
                (approachingCorner ? Mathf.Min(speed, distance * 12) : speed);
        }

        private static Vector3 FlatDirection(Vector3 delta) { delta.y = 0; return delta.normalized; }
        private void ClearRoutes()
        {
            for (int i = 0; i < 4; i++) { corners[i] = null; nextRouteTime[i] = 0; }
        }

        public void SelectMember(int index)
        {
            if (index < 0 || index > 3 || mode == PartyMode.Regrouping) return;
            if (mode == PartyMode.Chained && index != 0) return;
            selected = index; jumpInput = false; UpdateSelection();
        }

        public void ToggleGroup()
        {
            Initialize();
            if (mode == PartyMode.Chess) return;
            if (mode == PartyMode.Chained || mode == PartyMode.Regrouping)
            { mode = PartyMode.Separated; jumps.Clear(); ClearRoutes(); statusMessage = "Выбери персонажа клавишами 1–4"; }
            else BeginRegroup();
            jumpInput = false;
        }

        public void BeginRegroup()
        {
            Initialize();
            if (mode == PartyMode.Chess) return;
            mode = PartyMode.Regrouping; selected = 0; jumps.Clear(); ClearRoutes();
            var anchor = members[0].Body.position;
            // Choose a complete free line behind A, rotating it if a wall/cube blocks the slots.
            bool found = false;
            for (int attempt = 0; attempt < 16 && !found; attempt++)
            {
                Vector3 direction = Quaternion.Euler(0, attempt * 22.5f, 0) * -lastDirection;
                found = true;
                for (int i = 1; i < 4; i++)
                {
                    var slot = anchor + direction * spacing * i;
                    if (navigation != null && navigation.EnsureReady())
                    {
                        if (!NavMesh.SamplePosition(slot, out var hit, .25f, NavMesh.AllAreas) ||
                            !navigation.FindPath(anchor, slot, routes[i])) { found = false; break; }
                        slot = hit.position;
                    }
                    regroupTargets[i] = slot;
                }
            }
            if (!found)
            {
                mode = PartyMode.Separated;
                statusMessage = "Недостаточно места у A. Отойди на свободное место и нажми G.";
            }
            else statusMessage = "Сбор к A — можно отменить клавишей G";
        }

        // Future cutscenes call this method; T is only the prototype/debug trigger.
        public bool EnterChessMode(BoardGrid board)
        {
            Initialize();
            if (board == null || mode == PartyMode.Chess) return false;
            explorationMode = mode == PartyMode.Regrouping ? PartyMode.Separated : mode;
            explorationSelection = selected;
            for (int i = 0; i < 4; i++)
            {
                explorationPositions[i] = members[i].Body.position;
                explorationRotations[i] = members[i].Body.rotation;
                members[i].ResetTo(board.CellCenter(2 + i, 0) + Vector3.up * .015f, Quaternion.identity);
                members[i].Body.isKinematic = true;
            }
            Physics.SyncTransforms();
            mode = PartyMode.Chess; selected = 0; jumps.Clear(); jumpInput = false;
            statusMessage = "Тестовая расстановка C1–F1. Ходы и магия пока выключены.";
            UpdateSelection();
            return true;
        }

        public void ExitChessMode()
        {
            if (mode != PartyMode.Chess) return;
            for (int i = 0; i < 4; i++)
            {
                members[i].Body.isKinematic = false;
                members[i].ResetTo(explorationPositions[i], explorationRotations[i]);
            }
            mode = explorationMode; selected = explorationSelection;
            Physics.SyncTransforms(); RebuildTrail(); ClearRoutes(); jumpInput = false; statusMessage = "";
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < 4; i++)
                if (selectionRings[i] != null) selectionRings[i].gameObject.SetActive(i == selected);
        }

        private void OnGUI()
        {
            string label = mode == PartyMode.Chained ? "Цепочка" : mode == PartyMode.Separated ? "Отряд разделён" :
                mode == PartyMode.Regrouping ? "Сбор к A" : "Шахматный режим (тест)";
            GUI.Box(new Rect(12, 12, 670, 98), "");
            var style = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            GUI.Label(new Rect(24, 18, 645, 88), $"{label}  |  Выбран: {(char)('A' + selected)}\n" +
                "WASD / стрелки — идти   Пробел — прыжок   G — разделить / собрать\n" +
                "1–4 — персонаж   T — тест шахматного режима   R — вернуть на старт\n" +
                (mode == PartyMode.Regrouping || mode == PartyMode.Chess || mode == PartyMode.Separated ? statusMessage : ""), style);
        }

        private void RecordLeader(bool force)
        {
            var last = path[path.Count - 1];
            var position = members[0].Body.position;
            float distance = FlatDistance(last.position, position);
            if (distance >= .04f || force && distance > .001f)
                path.Add(new PathPoint { position = position, distance = last.distance + distance });
        }

        private Vector3 Sample(float distance)
        {
            int lo = 0, hi = path.Count - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) / 2;
                if (path[mid].distance <= distance) lo = mid; else hi = mid;
            }
            var a = path[lo]; var b = path[hi];
            return Vector3.Lerp(a.position, b.position, Mathf.InverseLerp(a.distance, b.distance, distance));
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0;
            return Vector3.Distance(a, b);
        }

        private void RebuildTrail()
        {
            path.Clear(); jumps.Clear(); elapsed = 0;
            float distance = 0;
            for (int i = 3; i >= 0; i--)
            {
                if (i < 3) distance += FlatDistance(members[i + 1].Body.position, members[i].Body.position);
                path.Add(new PathPoint { position = members[i].Body.position, distance = distance });
                progress[i] = distance;
            }
        }

        public void ResetParty()
        {
            if (!initialized) return;
            for (int i = 0; i < 4; i++)
            { members[i].Body.isKinematic = false; members[i].ResetTo(starts[i], rotations[i]); }
            mode = PartyMode.Chained; selected = 0; statusMessage = ""; ClearRoutes();
            Physics.SyncTransforms();
            RebuildTrail();
            jumpInput = false;
        }
    }
}
