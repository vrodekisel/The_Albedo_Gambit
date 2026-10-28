using UnityEngine;
using UnityEngine.InputSystem;

namespace AlbedoGambit.Prototype
{
    // Input/presentation adapter. BattleSession remains the authority for legal destinations.
    public sealed class BattleBoardView : MonoBehaviour
    {
        private PartyChainController party;
        private BoardGrid board;
        private LineRenderer selectedOutline;
        private LineRenderer hoverOutline;
        private Material outlineMaterial;
        private readonly LineRenderer[] moveOutlines = new LineRenderer[64];
        public ulong HighlightedMoves { get; private set; }
        private int hovered = -1;
        public int HoveredCell => hovered;

        public void Bind(PartyChainController controller, BoardGrid grid)
        {
            party = controller; board = grid; hovered = -1;
            if (outlineMaterial != null) return;
            outlineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            outlineMaterial.SetColor("_BaseColor", Color.white);
            selectedOutline = CreateOutline("Selected Cell", new Color(1, .72f, .08f));
            hoverOutline = CreateOutline("Hovered Cell", new Color(.1f, .8f, 1));
            for (int i = 0; i < 64; i++)
            {
                moveOutlines[i] = CreateOutline("Move Target " + i, new Color(.15f, .85f, .55f));
                moveOutlines[i].widthMultiplier = .035f;
            }
        }

        private LineRenderer CreateOutline(string label, Color color)
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(transform, false);
            var line = obj.AddComponent<LineRenderer>();
            line.sharedMaterial = outlineMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            line.SetPropertyBlock(properties);
            line.useWorldSpace = true; line.loop = true; line.positionCount = 4;
            line.widthMultiplier = .045f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false; line.enabled = false;
            return line;
        }

        private void LateUpdate()
        {
            RefreshHighlights();
            if (party == null || board == null || party.Battle.Phase != BattlePhase.Battle)
            { Clear(); return; }
            hovered = -1;
            var mouse = Mouse.current;
            var camera = Camera.main;
            if (mouse != null && camera != null)
            {
                Vector2 screen = mouse.position.ReadValue();
                bool overHud = party.Hud != null ? party.Hud.PointerOverUI(screen) : screen.x < 700 && screen.y > Screen.height - 155;
                if (!overHud)
                {
                    hovered = CellUnderPointer(screen);
                    if (hovered >= 0 && mouse.leftButton.wasPressedThisFrame) { ClickCell(hovered); RefreshHighlights(); }
                }
            }
            DrawCell(hoverOutline, hovered);
        }

        private int CellUnderPointer(Vector2 screen)
        {
            var camera = Camera.main;
            if (camera == null || board == null || !Physics.Raycast(camera.ScreenPointToRay(screen), out var hit, 1000, ~0, QueryTriggerInteraction.Collide)) return -1;
            var member = hit.collider.GetComponentInParent<PartyMember>();
            if (member != null)
                for (int i = 0; i < party.Members.Length; i++)
                    if (party.Members[i].GetComponent<PartyMember>() == member) return party.Battle.CellOf(i);
            var enemy = hit.collider.GetComponentInParent<BattleEnemyView>();
            if (enemy != null) return party.Battle.CellOf(enemy.Participant);
            if (hit.collider.GetComponentInParent<BoardGrid>() == board && board.TryGetCell(hit.point, out var cell) &&
                Mathf.Abs(hit.point.y - board.CellCenter(cell.x, cell.y).y) < .1f) return cell.y * 8 + cell.x;
            return -1;
        }

        public bool ClickScreen(Vector2 screen)
        {
            if (party == null || party.Hud != null && party.Hud.PointerOverUI(screen)) return false;
            return ClickCell(CellUnderPointer(screen));
        }

        public bool ClickCell(int cell)
        {
            if (party == null || party.IsBattleMoving || party.Battle.Phase != BattlePhase.Battle || !party.Battle.HasCell(cell)) return false;
            int occupant = party.Battle.OccupantAt(cell);
            return occupant >= 0 ? SelectCell(cell) : party.TryMoveSelected(cell);
        }

        public void RefreshHighlights()
        {
            if (party == null || board == null || party.Battle.Phase != BattlePhase.Battle) { Clear(); return; }
            HighlightedMoves = party.AvailableMoves;
            DrawCell(selectedOutline, party.Battle.CellOf(party.SelectedIndex));
            for (int i = 0; i < 64; i++) DrawCell(moveOutlines[i], (HighlightedMoves & (1UL << i)) != 0 ? i : -1);
        }

        public bool SelectCell(int cell)
        {
            if (party == null || party.Battle.Phase != BattlePhase.Battle) return false;
            int occupant = party.Battle.OccupantAt(cell);
            if (occupant < 0) return false;
            party.SelectMember(occupant);
            return party.SelectedIndex == occupant;
        }

        private void DrawCell(LineRenderer line, int index)
        {
            if (line == null) return;
            var cell = board.CellTransform(index);
            line.enabled = cell != null;
            if (cell == null) return;
            for (int i = 0; i < 4; i++)
            {
                float x = i == 0 || i == 3 ? -.45f : .45f;
                float z = i < 2 ? -.45f : .45f;
                line.SetPosition(i, cell.TransformPoint(new Vector3(x, .5f, z)) + Vector3.up * .04f);
            }
        }

        private void OnGUI()
        {
            if (party != null && party.Hud != null && party.Hud.IsShowingBattle) return;
            if (party == null || board == null || party.Battle.Phase != BattlePhase.Battle) return;
            int cell = party.Battle.CellOf(party.Battle.SelectedMember);
            string text = $"Выбран {(char)('A' + party.Battle.SelectedMember)}: {CellName(cell)}";
            if (hovered >= 0)
            {
                int occupant = party.Battle.OccupantAt(hovered);
                text += $"   |   {CellName(hovered)}: " + (party.Battle.IsBlocked(hovered) ? "препятствие" :
                    occupant < 0 ? "свободно" : $"персонаж {(char)('A' + occupant)}");
            }
            GUI.Box(new Rect(12, 114, 670, 32), text);
        }

        private static string CellName(int cell) => cell < 0 ? "—" : $"{(char)('A' + cell % 8)}{cell / 8 + 1}";

        public void Clear()
        {
            hovered = -1;
            if (selectedOutline != null) selectedOutline.enabled = false;
            if (hoverOutline != null) hoverOutline.enabled = false;
            HighlightedMoves = 0;
            foreach (var line in moveOutlines) if (line != null) line.enabled = false;
        }

        private void OnDisable() { Clear(); }
        private void OnDestroy()
        {
            if (outlineMaterial != null) Destroy(outlineMaterial);
        }
    }
}
