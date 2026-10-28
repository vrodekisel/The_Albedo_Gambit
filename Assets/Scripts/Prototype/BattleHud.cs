using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlbedoGambit.Prototype
{
    // Presentation only. Buttons delegate to existing commands; they never edit health or occupancy.
    public sealed class BattleHud : MonoBehaviour
    {
        [Serializable] public sealed class HeroArt
        {
            public string name;
            public Sprite avatar, portrait;
            public Color color;
            public Sprite[] icons;
            public string[] abilities;
            public Vector4 portraitCrop;
        }
        public PartyChainController party;
        public GameObject content, pauseOverlay;
        public Button[] heroButtons, abilityButtons;
        public Button menuButton, endTurnButton, resumeButton;
        public Image portrait, portraitBackground, healthFill;
        public Image[] abilityIcons, abilityBackgrounds;
        public Text heroName, healthLabel, manaLabel, hint, turnLabel;
        public Text[] abilityLabels;
        public HeroArt[] heroes;
        private BattlePrototypeController combat;
        private bool wired;
        private string notice = "";
        private float noticeUntil;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private static readonly int[] DisplayOrder = { 0, 2, 1, 3 };
        private static readonly int[] OrderedDisplayOrder = { 0, 1, 2, 3 };
        public bool IsShowingBattle => party != null && party.Mode == PartyMode.Chess;

        private void Awake() { if (party != null) Bind(party); else content.SetActive(false); }
        public void Bind(BattlePrototypeController controller) { combat = controller; Bind(controller.Party); }
        public void Bind(PartyChainController controller)
        {
            if (party != null && party.Hud == this) party.Hud = null;
            party = controller;
            if (party != null) party.Hud = this;
            if (!wired)
            {
                for (int i = 0; i < 4; i++) { int actor = i; heroButtons[i].onClick.AddListener(() => Inspect(actor)); }
                for (int i = 0; i < 3; i++) { int slot = i; abilityButtons[i].onClick.AddListener(() => PreviewAbility(slot)); }
                menuButton.onClick.AddListener(ToggleMenu);
                resumeButton.onClick.AddListener(ToggleMenu);
                endTurnButton.onClick.AddListener(EndTurn);
                wired = true;
            }
            Refresh();
        }
        private void LateUpdate() { Refresh(); }
        private void Inspect(int actor)
        {
            if (party == null) return;
            party.SelectMember(actor); notice = ""; Refresh();
        }
        private void ToggleMenu() { if (party != null) party.TogglePause(); Refresh(); }
        private void PreviewAbility(int slot)
        {
            if (party == null || party.Battle.Phase != BattlePhase.Battle) return;
            if (slot < 2 && party.Battle.OrderedEnemyMovement)
            {
                if (party.BeginAbility(slot)) Notice("Choose a highlighted enemy");
                return;
            }
            Notice(heroes[party.SelectedIndex].abilities[slot] + " — not implemented yet");
        }
        public void ShowAbilityHit(int enemy, int health, int charge)
        { Notice("Enemy " + (enemy - 3) + ": " + health + " / 100 HP  ·  Ultimate " + charge + " / 3"); }
        public void ShowAbilityFlight() { Notice("Ability in flight"); }
        private void EndTurn()
        {
            if (party == null) return;
            bool ended = combat != null ? combat.EndTurn() : party.EndPreviewTurn();
            if (ended) Notice(party.Battle.OrderedEnemyMovement ? "" : combat == null ? "Next turn — enemy actions are not connected yet" : "Turn ended");
            Refresh();
        }
        private void Notice(string message) { notice = message; noticeUntil = Time.unscaledTime + 4; Refresh(); }
        public void Refresh()
        {
            bool visible = IsShowingBattle;
            content.SetActive(visible);
            if (!visible) return;
            var state = party.Battle;
            bool paused = state.Phase == BattlePhase.Paused;
            pauseOverlay.SetActive(paused);
            int selected = Mathf.Clamp(party.SelectedIndex, 0, 3);
            var art = heroes[selected];
            heroName.text = art.name;
            portrait.sprite = art.portrait; portraitBackground.color = art.color;
            // Crops supplied by the four Figma states, relative to the portrait frame.
            var crop = art.portraitCrop;
            float innerY = selected == 3 ? .28656f : 36.524f;
            float innerH = selected == 3 ? 369.713f : 333.476f;
            var pr = portrait.rectTransform;
            pr.anchoredPosition = new Vector2(382 * crop.z, -(innerY + innerH * crop.w));
            pr.sizeDelta = new Vector2(382 * crop.x, innerH * crop.y);
            healthLabel.text = "Health   " + state.HealthOf(selected) + " / " + state.MaxHealth;
            float healthRatio = state.HealthOf(selected) / (float)state.MaxHealth;
            healthFill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 382 * healthRatio);
            healthFill.enabled = healthRatio > 0;
            manaLabel.text = state.OrderedEnemyMovement
                ? "Ultimate Mana   " + state.UltimateChargeOf(selected) + " / 3"
                : "Ultimate Mana   — / 3";
            hint.text = state.OrderedEnemyMovement
                ? party.IsBattleMoving ? "Action in progress"
                : party.IsAimingAbility ? "Click a highlighted enemy" : "Choose an ability or a move"
                : "Abilities not implemented yet";
            turnLabel.text = state.Side == BattleSide.Enemy
                ? "Enemy turn  ·  " + state.RoleOf(state.ActingMember)
                : state.OrderedEnemyMovement && !string.IsNullOrEmpty(notice) && Time.unscaledTime < noticeUntil
                ? notice
                : state.OrderedEnemyMovement
                ? "Turn " + state.PlayerTurn + "  ·  " + heroes[state.SelectedMember].name + " — your turn"
                : state.ActingMember >= 0 && state.Side == BattleSide.Player
                ? "Turn " + state.PlayerTurn + "  ·  " + heroes[state.ActingMember].name + " — hero locked"
                : !string.IsNullOrEmpty(notice) && Time.unscaledTime < noticeUntil ? notice : "Turn " + state.PlayerTurn + "  ·  click a highlighted cell";
            endTurnButton.interactable = !party.IsBattleMoving && state.Phase == BattlePhase.Battle && state.Side == BattleSide.Player && state.Result == BattleResult.Ongoing;
            float x = 24;
            foreach (int actor in state.OrderedEnemyMovement ? OrderedDisplayOrder : DisplayOrder)
            {
                var rect = (RectTransform)heroButtons[actor].transform;
                float size = actor == selected ? 197 : 127;
                rect.anchoredPosition = new Vector2(x, -24); rect.sizeDelta = new Vector2(size, size);
                heroButtons[actor].interactable = !party.IsBattleMoving && state.CanSelect(actor);
                x += size + 30;
            }
            for (int i = 0; i < 3; i++)
            {
                abilityIcons[i].sprite = art.icons[i];
                abilityLabels[i].text = state.OrderedEnemyMovement && i < 2 ? "Orb " + (i + 1) : art.abilities[i];
                abilityBackgrounds[i].color = art.color;
                abilityButtons[i].interactable = !party.IsBattleMoving && !paused && state.Phase == BattlePhase.Battle &&
                    state.Side == BattleSide.Player && (!state.OrderedEnemyMovement && i == 2 || i < 2 && !state.ActionSpent);
            }
        }
        public bool PointerOverUI(Vector2 position)
        {
            if (EventSystem.current == null || !IsShowingBattle) return false;
            hits.Clear(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            foreach (var hit in hits) if (hit.module is GraphicRaycaster) return true;
            return false;
        }
        private void OnDestroy() { if (party != null && party.Hud == this) party.Hud = null; }
    }
}
