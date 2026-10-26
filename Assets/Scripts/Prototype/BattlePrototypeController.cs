using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Collections.Generic;

namespace AlbedoGambit.Prototype
{
    public enum BattleCommandMode { Move, Attack }

    // Adapter: all UI, keyboard and board input goes through these command methods.
    public sealed class BattlePrototypeController : MonoBehaviour
    {
        [SerializeField] private PartyChainController party;
        [SerializeField] private BoardGrid board;
        [SerializeField] private BattleEnemyView enemyPrefab;
        [SerializeField] private BattleHud hudPrefab;
        [SerializeField, Min(1)] private int prototypeHealth = 3;
        [SerializeField, Min(1)] private int prototypeDamage = 1;
        [SerializeField, Min(.1f)] private float enemyThinkSeconds = .55f;
        [SerializeField, Min(.1f)] private float moveSeconds = .28f;
        private readonly BattleEnemyView[] enemies = new BattleEnemyView[4];
        private readonly Vector3[] visualLocalPositions = new Vector3[4];
        private readonly Quaternion[] visualLocalRotations = new Quaternion[4];
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private BattleHud hud;
        private bool initialized;
        private bool encounterVisible;
        private float enemyWait;
        private int enemyStep;
        private int moving = -1;
        private float moveTime;
        private Vector3 moveFrom, moveTo;
        public BattleSession Session => party.Battle;
        public PartyChainController Party => party;
        public BoardGrid Board => board;
        public BattleHud Hud => hud;
        public BattleCommandMode CommandMode { get; private set; }
        public string Message { get; private set; } = "";
        public bool IsAnimating => moving >= 0;
        public int VisibleEnemyCount
        {
            get { int count = 0; foreach (var enemy in enemies) if (enemy != null && enemy.gameObject.activeSelf) count++; return count; }
        }
        public void Configure(PartyChainController group, BoardGrid grid, BattleEnemyView enemy, BattleHud ui)
        { party = group; board = grid; enemyPrefab = enemy; hudPrefab = ui; }

        private void Awake() { Initialize(); }
        public void Initialize()
        {
            if (initialized || party == null || board == null) return;
            party.Initialize(); party.Combat = this;
            if (Session.Phase == BattlePhase.Exploration) Session.ConfigurePrototype(prototypeHealth, prototypeDamage);
            for (int i = 0; i < 4; i++)
            {
                var visual = party.Members[i].transform.Find("Capsule Visual");
                visualLocalPositions[i] = visual.localPosition; visualLocalRotations[i] = visual.localRotation;
            }
            if (hudPrefab != null) { hud = Instantiate(hudPrefab, transform); hud.name = "Battle HUD"; hud.Bind(this); }
            initialized = true;
        }

        public int[] DebugStarts => new[] { 23, 39, 31, 47, 16, 24, 32, 40 };
        public bool BeginBattle()
        {
            Initialize();
            if (!initialized) return false;
            return party.EnterChessMode(board);
        }
        public void OnBattleStarted()
        {
            Initialize();
            CommandMode = BattleCommandMode.Move; Message = "Select a hero, then a highlighted cell.";
            encounterVisible = true; enemyStep = 0; moving = -1;
            for (int i = 0; i < 4; i++)
            {
                if (enemies[i] == null)
                {
                    enemies[i] = Instantiate(enemyPrefab, transform);
                    enemies[i].name = "Enemy " + (i + 1); enemies[i].Bind(4 + i);
                }
                enemies[i].gameObject.SetActive(true);
            }
            SynchronizeViews();
            if (hud != null) hud.Refresh();
        }
        public void OnBattleEnded()
        {
            moving = -1; enemyStep = 0; encounterVisible = false; Message = "";
            foreach (var enemy in enemies) if (enemy != null) enemy.gameObject.SetActive(false);
            for (int i = 0; i < 4; i++)
            {
                var visual = party.Members[i].transform.Find("Capsule Visual");
                visual.localPosition = visualLocalPositions[i]; visual.localRotation = visualLocalRotations[i];
            }
            if (hud != null) hud.Refresh();
        }
        public void InspectHero(int member) { party.SelectMember(member); Message = ""; }
        public void ChooseCommand(BattleCommandMode command)
        {
            if (Session.Phase != BattlePhase.Battle) return;
            CommandMode = command; Message = "";
        }
        public void PreviewAbility(int slot)
        {
            if (Session.Phase != BattlePhase.Battle) return;
            Message = slot == 2 ? "Fusion is not implemented yet." : "This ability is not implemented yet.";
        }
        public bool ExecuteCell(int cell)
        {
            if (Session.Phase != BattlePhase.Battle || IsAnimating) return false;
            int occupant = Session.OccupantAt(cell);
            if (occupant >= 0 && occupant < 4) { InspectHero(occupant); return true; }
            int actor = Session.SelectedMember;
            if (Session.Side != BattleSide.Player || !Session.CanAct(actor))
            {
                Message = Session.Side == BattleSide.Enemy ? "Enemy turn." : Session.IsKnockedOut(actor) ? "This hero is knocked out." :
                    Session.Result != BattleResult.Ongoing ? "Encounter finished." : $"{HeroName(Session.ActingMember)} is acting. Other cards are preview only.";
                return false;
            }
            if (CommandMode == BattleCommandMode.Attack)
            {
                if (!Session.TryAttack(actor, cell)) { Message = "Choose a highlighted enemy."; return false; }
                Message = "Attack landed."; SynchronizeViews(); return true;
            }
            int from = Session.CellOf(actor);
            if (!Session.TryMove(actor, cell)) { Message = "Choose a highlighted empty cell."; return false; }
            BeginMovement(actor, from, cell); Message = ""; return true;
        }
        public bool EndTurn()
        {
            if (IsAnimating || !Session.EndPlayerTurn()) return false;
            enemyWait = enemyThinkSeconds; enemyStep = 0; Message = ""; return true;
        }
        public void ToggleMenu() { party.TogglePause(); }
        public void RestartEncounter()
        {
            if (Session.Phase == BattlePhase.Paused) party.TogglePause();
            if (party.Mode == PartyMode.Chess) party.ExitChessMode();
            BeginBattle();
        }
        public void LeaveBattle()
        {
            if (Session.Phase == BattlePhase.Paused) party.TogglePause();
            party.ExitChessMode();
        }
        public bool PointerOverUI(Vector2 position)
        {
            if (EventSystem.current == null) return false;
            var data = new PointerEventData(EventSystem.current) { position = position };
            uiHits.Clear(); EventSystem.current.RaycastAll(data, uiHits);
            foreach (var hit in uiHits) if (hit.module is GraphicRaycaster) return true;
            return false;
        }
        public ulong HighlightTargets => IsAnimating || Session.Side != BattleSide.Player ? 0 :
            CommandMode == BattleCommandMode.Move ? Session.MoveTargets(Session.SelectedMember) : Session.AttackTargets(Session.SelectedMember);

        private void Update()
        {
            Initialize();
            if (!initialized) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && Session.Phase == BattlePhase.Battle)
            {
                if (keyboard.mKey.wasPressedThisFrame) ChooseCommand(BattleCommandMode.Move);
                if (keyboard.fKey.wasPressedThisFrame) ChooseCommand(BattleCommandMode.Attack);
                if (keyboard.enterKey.wasPressedThisFrame) EndTurn();
            }
            TickBattle(Time.deltaTime);
        }
        public void TickBattle(float dt)
        {
            if (!initialized || Session.Phase != BattlePhase.Battle) return;
            if (!encounterVisible) OnBattleStarted();
            if (IsAnimating)
            {
                moveTime += dt;
                float t = Mathf.Clamp01(moveTime / moveSeconds);
                Vector3 position = Vector3.Lerp(moveFrom, moveTo, Mathf.SmoothStep(0, 1, t));
                if (Session.RoleOf(moving) == ChessRole.Knight) position += Vector3.up * Mathf.Sin(t * Mathf.PI) * .8f;
                SetPosition(moving, position);
                if (t >= 1) { moving = -1; SynchronizeViews(); }
                return;
            }
            if (Session.Side != BattleSide.Enemy || Session.Result != BattleResult.Ongoing) return;
            enemyWait -= dt;
            if (enemyWait > 0) return;
            int actor = Session.ActingMember;
            if (enemyStep == 0)
            {
                // One participant per enemy turn. Simple deterministic baseline, not adaptive AI.
                int target = FirstCell(Session.AttackTargets(actor));
                if (target >= 0) { Session.TryAttack(actor, target); enemyStep = 2; SynchronizeViews(); }
                else
                {
                    int destination = ClosestMove(actor);
                    if (destination >= 0)
                    {
                        int origin = Session.CellOf(actor);
                        if (Session.TryMove(actor, destination)) BeginMovement(actor, origin, destination);
                    }
                    enemyStep = 1;
                }
            }
            else if (enemyStep == 1)
            {
                int target = FirstCell(Session.AttackTargets(actor));
                if (target >= 0) Session.TryAttack(actor, target);
                SynchronizeViews(); enemyStep = 2;
            }
            else { Session.CompleteEnemyTurn(); enemyStep = 0; }
            enemyWait = enemyThinkSeconds;
        }
        private int ClosestMove(int actor)
        {
            ulong targets = Session.MoveTargets(actor);
            int best = -1, bestDistance = int.MaxValue;
            for (int cell = 0; cell < 64; cell++)
            {
                if ((targets & (1UL << cell)) == 0) continue;
                for (int hero = 0; hero < 4; hero++)
                {
                    if (Session.IsKnockedOut(hero)) continue;
                    int target = Session.CellOf(hero);
                    int distance = Mathf.Abs(cell % 8 - target % 8) + Mathf.Abs(cell / 8 - target / 8);
                    if (distance < bestDistance) { bestDistance = distance; best = cell; }
                }
            }
            return best;
        }
        private static int FirstCell(ulong mask)
        { for (int i = 0; i < 64; i++) if ((mask & (1UL << i)) != 0) return i; return -1; }
        private Vector3 CellPosition(int cell) => board.CellCenter(cell % 8, cell / 8) + Vector3.up * .015f;
        private void BeginMovement(int participant, int from, int to)
        { moving = participant; moveTime = 0; moveFrom = CellPosition(from); moveTo = CellPosition(to); }
        private void SetPosition(int participant, Vector3 position)
        {
            if (participant < 4)
            {
                var member = party.Members[participant];
                member.transform.position = position; member.Body.position = position;
            }
            else if (enemies[participant - 4] != null) enemies[participant - 4].transform.position = position;
        }
        public void SynchronizeViews()
        {
            for (int i = 0; i < Session.ParticipantCount; i++)
            {
                if (i != moving) SetPosition(i, CellPosition(Session.CellOf(i)));
                Transform root = i < 4 ? party.Members[i].transform : enemies[i - 4].transform;
                var visual = root.Find("Capsule Visual");
                bool down = Session.IsKnockedOut(i);
                visual.localRotation = down ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;
                visual.localPosition = down ? new Vector3(0, .3f, 0) : new Vector3(0, .8f, 0);
            }
            Physics.SyncTransforms();
        }
        public static string HeroName(int index)
        { switch (index) { case 0: return "Eric"; case 1: return "Mia"; case 2: return "Joseph"; case 3: return "Evelyn"; default: return "Enemy " + (index - 3); } }
    }
}
