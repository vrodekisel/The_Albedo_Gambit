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
        private readonly PartyLocalNavigation localNavigation = new PartyLocalNavigation();
        private readonly Vector3[] steering = new Vector3[4];
        private Vector2 input;
        private bool jumpInput;
        private bool resetInput;
        private float elapsed;
        private bool initialized;
        private PartyMode mode;
        private int selected;
        private PrototypeNavigation navigation;
        private readonly NavMeshPath[] routes = new NavMeshPath[4];
        private NavMeshPath leaderGroundRoute;
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
        private readonly BattleSession battle = new BattleSession();
        private BattleBoardView battleView;
        private BoardGrid battleBoard;
        private int movingMember = -1;
        private Vector3 moveFrom, moveTo;
        private float moveElapsed;
        private const float BattleMoveDuration = .3f;
        public bool IsBattleMoving => movingMember >= 0;
        public ulong AvailableMoves => IsBattleMoving ? 0 : battle.MoveTargets(selected);
        private float timeScaleBeforePause = 1;
        private bool ownsPause;
        private readonly bool[] cutsceneKinematic = new bool[4];
        public BattleSession Battle => battle;
        public BattleHud Hud { get; set; }
        // Reserved adapter reference; enemy integration is a separate step.
        public BattlePrototypeController Combat { get; set; }
        public PartyMode Mode => mode;
        public int SelectedIndex => selected;
        private struct PathPoint { public Vector3 position; public float distance; }
        private sealed class JumpPoint
        {
            public float time;
            public int consumedMask = 1;
        }
        public CapsuleMotor[] Members => members;
        public float Spacing => spacing;
        public float MoveSpeed => moveSpeed;
        // Identity remains A/B/C/D; formation slots are A/C/B/D.
        public static int FormationSlot(int actor) => actor == 1 ? 2 : actor == 2 ? 1 : actor;
        public static int ActorAtSlot(int slot) => FormationSlot(slot);
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
            starts = new Vector3[4]; rotations = new Quaternion[4];
            for (int i = 0; i < 4; i++)
            {
                if (members[i] == null) { enabled = false; return; }
                starts[i] = members[i].Body.position;
                routes[i] = new NavMeshPath();
                rotations[i] = members[i].Body.rotation;
                selectionRings[i] = members[i].transform.Find("Selection Ring");
                for (int j = 0; j < i; j++)
                    Physics.IgnoreCollision(members[i].GetComponent<Collider>(), members[j].GetComponent<Collider>(), false);
            }
            initialized = true;
            leaderGroundRoute = new NavMeshPath();
            navigation = FindFirstObjectByType<PrototypeNavigation>();
            RebuildTrail();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) { input = Vector2.zero; return; }
            if (keyboard.escapeKey.wasPressedThisFrame) TogglePause();
            if (battle.Phase == BattlePhase.Paused || battle.Phase == BattlePhase.Cutscene)
            { input = Vector2.zero; jumpInput = resetInput = false; return; }
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
            if (battle.Phase == BattlePhase.Paused || battle.Phase == BattlePhase.Cutscene) return;
            elapsed += dt;
            foreach (var member in members)
                if (member.Body.position.y < respawnBelowY) { ResetParty(); return; }
            movement.y = 0;
            movement = Vector3.ClampMagnitude(movement, 1);
            UpdateSelection();
            if (mode == PartyMode.Chess) { TickBattleMovement(dt); return; }
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
            if (movement.sqrMagnitude > .01f)
            {
                if (Vector3.Dot(lastDirection, movement.normalized) < -.5f) TurnTrail(movement.normalized);
                lastDirection = movement.normalized;
            }
            RecordLeader(false);
            if (members[0].Tick(movement * moveSpeed, jump, dt))
            {
                RecordLeader(true);
                jumps.Add(new JumpPoint { time = elapsed });
            }
            float total = path[path.Count - 1].distance;
            for (int i = 1; i < 4; i++)
            {
                float limit = Mathf.Max(path[0].distance, total - spacing * FormationSlot(i));
                Vector3 position = members[i].Body.position;
                // Rejoin the current formation slot. Requiring every old breadcrumb
                // deadlocks after a collision or when A reverses through the chain.
                Vector3 target = Sample(limit);
                target = YieldToLeader(i, target, movement);
                Vector3 delta = target - position;
                delta.y = 0;
                float speed = Mathf.Min(moveSpeed * 1.3f, delta.magnitude * 9f);
                Vector3 velocity = delta.magnitude < .035f ? Vector3.zero : Navigate(i, target, speed);
                JumpPoint pending = null;
                foreach (var marker in jumps)
                {
                    if ((marker.consumedMask & (1 << i)) != 0) continue;
                    bool ready = elapsed >= marker.time + .18f * FormationSlot(i);
                    if (ready) { pending = marker; break; }
                }
                if (members[i].Tick(velocity, pending != null, dt) && pending != null)
                    pending.consumedMask |= 1 << i;
            }
            float earliest = total - spacing * 3;
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
            if (!navigation.TryProjectGround(target, out target)) return Vector3.zero;
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
            // Static corners and moving party members are separate navigation concerns.
            if (distance < .04f) return Vector3.zero;
            steering[index] = localNavigation.Direction(index, position, aim, steering[index], members, navigation);
            return steering[index] * (approachingCorner ? Mathf.Min(speed, distance * 12) : speed);
        }

        private Vector3 YieldToLeader(int index, Vector3 target, Vector3 movement)
        {
            if (movement.sqrMagnitude < .01f || navigation == null) return target;
            var direction = movement.normalized;
            var offset = members[index].Body.position - members[0].Body.position;
            offset.y = 0;
            float ahead = Vector3.Dot(offset, direction);
            var side = Vector3.Cross(Vector3.up, direction);
            float lateral = Vector3.Dot(offset, side);
            if (ahead < -.15f || ahead > 1.6f || Mathf.Abs(lateral) > .95f) return target;
            // Give the manually controlled leader a clear corridor on reversals.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float sign = attempt == 0 ? 1 : -1;
                var candidate = members[index].Body.position + side * (sign * 1.05f - lateral);
                if (navigation.TryProjectGround(candidate, out var projected, .15f)) return projected;
            }
            return target;
        }

        private void TurnTrail(Vector3 heading)
        {
            // A reversal folds the old trail over itself. Seed distinct destinations
            // behind the new heading, without changing any character's physical pose.
            Vector3 anchor = members[0].Body.position;
            if (navigation != null && !navigation.TryProjectGround(anchor, out anchor)) return;
            var slots = new Vector3[4];
            slots[0] = anchor;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = ((attempt + 1) / 2) * 22.5f * (attempt % 2 == 0 ? -1 : 1);
                Vector3 behind = Quaternion.Euler(0, angle, 0) * -heading;
                bool free = true;
                for (int slot = 1; slot < 4; slot++)
                {
                    var point = anchor + behind * spacing * slot;
                    if (navigation != null && (!navigation.TryProjectGround(point, out point, .2f)
                        || NavMesh.Raycast(anchor, point, out _, NavMesh.AllAreas))) { free = false; break; }
                    slots[slot] = point;
                }
                if (!free) continue;
                path.Clear();
                for (int slot = 3; slot >= 0; slot--)
                    path.Add(new PathPoint { position = slots[slot], distance = (3 - slot) * spacing });
                ClearRoutes();
                return;
            }
        }

        private static Vector3 FlatDirection(Vector3 delta) { delta.y = 0; return delta.normalized; }
        private void ClearRoutes()
        {
            for (int i = 0; i < 4; i++) { corners[i] = null; nextRouteTime[i] = 0; steering[i] = Vector3.zero; }
        }

        public void SelectMember(int index)
        {
            if (IsBattleMoving) return;
            if (battle.Phase == BattlePhase.Paused || battle.Phase == BattlePhase.Cutscene) return;
            if (index < 0 || index > 3 || mode == PartyMode.Regrouping) return;
            if (mode == PartyMode.Chained && index != 0) return;
            if (mode == PartyMode.Chess && !battle.TrySelect(index)) return;
            selected = index; jumpInput = false; UpdateSelection();
        }

        public bool TryMoveSelected(int cell)
        {
            if (mode != PartyMode.Chess || battleBoard == null || IsBattleMoving) return false;
            int actor = selected;
            if (!battle.TryMove(actor, cell)) return false;
            movingMember = actor; moveElapsed = 0;
            moveFrom = members[actor].Body.position;
            moveTo = battleBoard.CellCenter(cell % 8, cell / 8) + Vector3.up * .015f;
            return true;
        }

        private void TickBattleMovement(float dt)
        {
            if (!IsBattleMoving) return;
            moveElapsed += Mathf.Max(0, dt);
            float t = Mathf.Clamp01(moveElapsed / BattleMoveDuration);
            Vector3 position = Vector3.Lerp(moveFrom, moveTo, Mathf.SmoothStep(0, 1, t));
            if (battle.RoleOf(movingMember) == ChessRole.Knight) position += Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.8f;
            var motor = members[movingMember];
            motor.transform.position = position; motor.Body.position = position;
            if (t >= 1) movingMember = -1;
            Physics.SyncTransforms();
        }

        public void ToggleGroup()
        {
            Initialize();
            if (battle.Phase != BattlePhase.Exploration) return;
            if (mode == PartyMode.Chess) return;
            if (mode == PartyMode.Chained || mode == PartyMode.Regrouping)
            { mode = PartyMode.Separated; jumps.Clear(); ClearRoutes(); statusMessage = "Выбери персонажа клавишами 1–4"; }
            else BeginRegroup();
            jumpInput = false;
        }

        public void BeginRegroup()
        {
            Initialize();
            if (battle.Phase != BattlePhase.Exploration) return;
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
                    var slot = anchor + direction * spacing * FormationSlot(i);
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
            if (!initialized || board == null || mode == PartyMode.Chess) return false;
            // Opposite board edges are an explicit debug fixture, not the future placement solver.
            var enemies = GetComponent<BattleEnemySpawner>();
            if (enemies != null && !enemies.IsConfigured) { statusMessage = "Не задан префаб противника."; return false; }
            int[] starts = enemies != null ? new[] { 23, 39, 31, 47, 16, 24, 32, 40 } : new[] { 23, 39, 31, 47 };
            if (!battle.TryBeginPlacement(board.ActiveMask, board.BlockedMask, starts))
            {
                statusMessage = "Не удалось начать расстановку: проверь клетки H3–H6, A3–A6 и текущий режим.";
                return false;
            }
            explorationMode = mode == PartyMode.Regrouping ? PartyMode.Separated : mode;
            battleBoard = board; movingMember = -1;
            explorationSelection = selected;
            for (int i = 0; i < 4; i++)
            {
                explorationPositions[i] = members[i].Body.position;
                explorationRotations[i] = members[i].Body.rotation;
                members[i].Body.isKinematic = false;
                members[i].ResetTo(board.CellCenter(7, 2 + FormationSlot(i)) + Vector3.up * .015f, Quaternion.identity);
                members[i].Body.isKinematic = true;
            }
            Physics.SyncTransforms();
            mode = PartyMode.Chess; selected = 0; jumps.Clear(); jumpInput = false;
            battle.CompletePlacement();
            if (enemies != null) enemies.Show(battle, board);
            if (battleView == null) battleView = GetComponent<BattleBoardView>();
            if (battleView == null) battleView = gameObject.AddComponent<BattleBoardView>();
            battleView.Bind(this, board);
            statusMessage = "ЛКМ по герою — выбор; по подсвеченной клетке — ход. End Turn — следующий ход.";
            UpdateSelection();
            return true;
        }

        public void ExitChessMode()
        {
            if (mode != PartyMode.Chess || battle.Phase != BattlePhase.Battle) return;
            movingMember = -1; battleBoard = null;
            for (int i = 0; i < 4; i++)
            {
                members[i].Body.isKinematic = false;
                members[i].ResetTo(explorationPositions[i], explorationRotations[i]);
            }
            mode = explorationMode; selected = explorationSelection;
            GetComponent<BattleEnemySpawner>()?.Hide();
            battle.Reset();
            if (battleView != null) battleView.Clear();
            Physics.SyncTransforms(); RebuildTrail(); ClearRoutes(); jumpInput = false; statusMessage = "";
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < 4; i++)
                if (selectionRings[i] != null) selectionRings[i].gameObject.SetActive(i == selected);
        }

        private void OnGUI()
        {
            if (Hud != null && Hud.IsShowingBattle) return;
            if (battle.Phase == BattlePhase.Paused)
            {
                GUI.Box(new Rect(Screen.width / 2f - 140, Screen.height / 2f - 40, 280, 80), "Пауза");
                GUI.Label(new Rect(Screen.width / 2f - 115, Screen.height / 2f, 250, 30), "Esc — продолжить");
                return;
            }
            string label = mode == PartyMode.Chained ? "Цепочка" : mode == PartyMode.Separated ? "Отряд разделён" :
                mode == PartyMode.Regrouping ? "Сбор к A" : "Шахматный режим (тест)";
            GUI.Box(new Rect(12, 12, 670, 98), "");
            var style = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            GUI.Label(new Rect(24, 18, 645, 88), $"{label}  |  Выбран: {(char)('A' + selected)}\n" +
                "WASD / стрелки — идти   Пробел — прыжок   G — разделить / собрать\n" +
                "1–4 — персонаж   T — тест режима   R — на старт   Esc — пауза\n" +
                statusMessage, style);
        }

        private void RecordLeader(bool force)
        {
            var last = path[path.Count - 1];
            var position = members[0].Body.position;
            if (navigation != null)
            {
                if (!navigation.TryProjectGround(position, out position)) return;
                float travel = FlatDistance(last.position, position);
                if (travel < .04f && !(force && travel > .001f)) return;
                if (!navigation.FindPath(last.position, position, leaderGroundRoute)) return;
                // If A jumps past a blocked footprint, store a walkable detour instead
                // of a straight airborne segment through the obstacle. No actor is warped.
                var points = leaderGroundRoute.corners;
                for (int i = 1; i < points.Length; i++) AppendGroundPoint(points[i]);
                return;
            }
            float distance = FlatDistance(last.position, position);
            if (distance >= .04f || force && distance > .001f)
                path.Add(new PathPoint { position = position, distance = last.distance + distance });
        }

        private void AppendGroundPoint(Vector3 position)
        {
            var last = path[path.Count - 1];
            float distance = FlatDistance(last.position, position);
            if (distance > .001f)
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
            for (int slot = 3; slot >= 0; slot--)
            {
                int i = ActorAtSlot(slot);
                if (slot < 3) distance += FlatDistance(members[ActorAtSlot(slot + 1)].Body.position, members[i].Body.position);
                path.Add(new PathPoint { position = members[i].Body.position, distance = distance });
            }
            Vector3 heading = FlatDirection(members[0].Body.position - members[ActorAtSlot(1)].Body.position);
            if (heading.sqrMagnitude > .01f) lastDirection = heading;
        }

        public void ResetParty()
        {
            if (!initialized) return;
            movingMember = -1; battleBoard = null;
            RestoreTimeScale();
            GetComponent<BattleEnemySpawner>()?.Hide();
            battle.Reset();
            if (battleView != null) battleView.Clear();
            for (int i = 0; i < 4; i++)
            { members[i].Body.isKinematic = false; members[i].ResetTo(starts[i], rotations[i]); }
            mode = PartyMode.Chained; selected = 0; statusMessage = ""; ClearRoutes();
            Physics.SyncTransforms();
            RebuildTrail();
            jumpInput = false;
        }

        // Spawn-only milestone: the enemy side explicitly passes, without movement or attacks.
        public bool EndPreviewTurn()
        {
            if (IsBattleMoving) return false;
            if (!battle.EndPlayerTurn()) return false;
            if (GetComponent<BattleEnemySpawner>() != null && Combat == null && battle.Side == BattleSide.Enemy)
                battle.CompleteEnemyTurn();
            return true;
        }

        public void TogglePause()
        {
            if (battle.Phase == BattlePhase.Paused)
            {
                RestoreTimeScale();
                battle.Resume();
            }
            else if (battle.Pause())
            {
                timeScaleBeforePause = Time.timeScale;
                ownsPause = true;
                Time.timeScale = 0;
            }
            input = Vector2.zero; jumpInput = resetInput = false;
        }

        // Story integration can freeze the party, then call EnterChessMode or CancelCutscene.
        public bool BeginCutscene()
        {
            Initialize();
            if (!initialized || !battle.BeginCutscene()) return false;
            for (int i = 0; i < members.Length; i++)
            {
                cutsceneKinematic[i] = members[i].Body.isKinematic;
                if (!cutsceneKinematic[i])
                { members[i].Body.linearVelocity = Vector3.zero; members[i].Body.angularVelocity = Vector3.zero; }
                members[i].Body.isKinematic = true;
            }
            input = Vector2.zero; jumpInput = resetInput = false;
            return true;
        }

        public void CancelCutscene()
        {
            if (!battle.CancelCutscene()) return;
            for (int i = 0; i < members.Length; i++) members[i].Body.isKinematic = cutsceneKinematic[i];
            RebuildTrail(); ClearRoutes();
        }

        private void RestoreTimeScale()
        {
            if (!ownsPause) return;
            Time.timeScale = timeScaleBeforePause;
            ownsPause = false;
        }

        private void OnDisable()
        {
            RestoreTimeScale();
            battle.Resume();
        }
    }
}
