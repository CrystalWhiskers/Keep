using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Strategy
{
    public class HudBadge
    {
        public RectTransform Rect;
        public Image Fill, Stroke, Dot;
        public Text Label;
        /// <summary>Red "!" shown when an enemy could strike this squad next turn.</summary>
        public RectTransform Warn;
        public void Destroy() => Object.Destroy(Rect.gameObject);
    }

    /// <summary>
    /// The command HUD, laid out on a 1600×900 reference canvas using the mockup's coordinates.
    /// Numbered zones match the design notes: 1 objective, 2 mana bar, 3 selection, 4 roster, 7 minimap,
    /// 8 end turn, 9 build, 10 ready status.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        GameManager gm;
        Canvas canvas;
        RectTransform root, badgeLayer, popupLayer;
        public float Scale => canvas != null ? canvas.scaleFactor : 1f;

        class Btn
        {
            public Panel P;
            public UIButton B;
            public Text Icon, Name, Cost;
            public Image Ring;
            public Panel Pill;
            public int Index;
        }

        // 2 · mana bar
        readonly List<Btn> spellBtns = new List<Btn>();
        Text manaText;
        Panel hintTop, hintPlace;
        Text hintTopText, hintPlaceText;

        // 3 · selection
        Panel selPanel, statBox, abilityBtn;
        Text selName, selStatus, selExtra, stat1, stat2, abilityText;
        Image selDot, statDivider;
        UIButton abilityB;

        // research panel, shown beside the roster when a Forge or Academy is selected
        Panel researchPanel;
        Text researchTitle, researchSub;
        readonly List<(Btn row, Text desc, Text status)> researchRows = new List<(Btn, Text, Text)>();
        BuildingType researchFor;

        // 4 / 9 · roster
        Panel recruitPanel, buildPanel;
        Text recruitChevron, buildChevron, goldText, buildCount, researchPoints;
        Image goldRing, researchRing;
        readonly List<Btn> recruitRows = new List<Btn>(), buildRows = new List<Btn>();

        // card
        Panel card;
        RectTransform cardArrow;
        Text cardTitle, cardRole, passiveLabel, passiveName, passiveText, activeLabel, activeName, activeText, costText, goldPreview, problemText;
        Panel activeChip;
        Text activeChipText;
        readonly List<(Panel p, Text value, Text label)> cardStats = new List<(Panel, Text, Text)>();
        Panel confirmBtn, cancelBtn;
        Text confirmText, sizeLabel;
        readonly List<(Panel p, Text t, UIButton b, int n)> sizeBtns = new List<(Panel, Text, UIButton, int)>();
        UIButton confirmB;

        // 7 / 8 / 10 · bottom
        Panel readyChip, endTurn, nextBtn, menuBtn, muteBtn, undoBtn;
        Text muteText, undoText;
        UIButton undoB;
        Image muteSlash;
        Text readyText, endText, nextText;
        Image readyDot;
        UIButton endB, nextB;
        Minimap minimap;

        // overlay
        RectTransform overlay;
        Text overlayTitle, overlaySub;
        Panel overlayPrimary, overlaySecondary, overlayTertiary;
        Text overlayPrimaryText, overlaySecondaryText, overlayTertiaryText;
        UIButton overlayPrimaryB, overlaySecondaryB, overlayTertiaryB;

        class PopupItem
        {
            public RectTransform R;
            public Text T;
            public Vector3 World;
            public float Born;
            public Color C;
        }
        readonly List<PopupItem> popups = new List<PopupItem>();

        // attack forecast tags, above the target and above the attacker
        Panel fcTarget, fcSelf;
        Text fcTargetText, fcSelfText;

        UnitType[] roster;

        // 1 · objective, plus the campaign's dialogue banner
        Text objTitle, objText;
        Panel dialogue;
        Text dialogueSpeaker, dialogueText;
        float dialogueUntil;
        readonly Queue<(string, string)> dialogueQueue = new Queue<(string, string)>();

        public static Hud Create(GameManager gm)
        {
            var go = new GameObject("HUD", typeof(RectTransform));
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            var hud = go.AddComponent<Hud>();
            hud.gm = gm;
            hud.canvas = c;
            hud.root = (RectTransform)go.transform;
            hud.Build();
            return hud;
        }

        static RectTransform Group(Transform parent, string name, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            return rt;
        }

        // ================================================================ build

        void Build()
        {
            badgeLayer = UIKit.Fill(root, "Badges");
            var tl = Group(root, "TopLeft", new Vector2(0, 1), new Vector2(700, 900));
            var tc = Group(root, "TopCenter", new Vector2(0.5f, 1), new Vector2(1600, 260));
            var tr = Group(root, "TopRight", new Vector2(1, 1), new Vector2(400, 300));
            var bc = Group(root, "BottomCenter", new Vector2(0.5f, 0), new Vector2(1600, 300));
            var br = Group(root, "BottomRight", new Vector2(1, 0), new Vector2(400, 320));
            popupLayer = UIKit.Fill(root, "Popups");
            roster = gm.Roster;
            (fcTarget, fcTargetText) = MakeTag("ForecastTarget", Palette.White);
            (fcSelf, fcSelfText) = MakeTag("ForecastSelf", Palette.Red);

            BuildObjective(tl);
            BuildRoster(tl);
            BuildCard(tl);
            BuildResearch(tl);
            BuildManaBar(tc);
            BuildSelection(tr);
            BuildBottom(bc, br);
            BuildDialogue(tc);
            BuildOverlay();
            Refresh();
        }

        void BuildObjective(RectTransform tl)
        {
            var p = UIKit.Panel(tl, "1 Objective", 30, 30, 300, 100, Palette.Panel, Palette.PanelStroke);
            var ch = gm.Chapter;
            UIKit.Label(p.Rect, ch != null ? $"CHAPTER {ch.Numeral} · OBJECTIVE" : "OBJECTIVE", 21, 14, 270, 20, 13, Palette.Muted, FontStyle.Bold);
            objTitle = UIKit.Label(p.Rect, ch?.ObjectiveTitle ?? "Win", 21, 32, 260, 38, 30, Palette.Text, FontStyle.Bold);
            objTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            objText = UIKit.Label(p.Rect, ch?.ObjectiveText ?? "Capture the enemy Keep", 21, 68, 270, 22, 15, Palette.Muted);
        }

        void RefreshObjective()
        {
            var ch = gm.Chapter;
            if (ch == null || ch.Goal != Objective.Survive) return;
            int left = Mathf.Max(0, ch.SurviveTurns - gm.Model.Turn);
            objTitle.text = left == 0 ? "Survive" : $"Survive · {left} {(left == 1 ? "turn" : "turns")}";
        }

        // ================================================================ dialogue

        /// <summary>Story line in a banner under the hint. Click to dismiss; it also fades on its own.</summary>
        public void ShowDialogue(string speaker, string text)
        {
            dialogueQueue.Enqueue((speaker, text));
            if (!dialogue.Rect.gameObject.activeSelf) NextDialogue();
        }

        void NextDialogue()
        {
            if (dialogueQueue.Count == 0)
            {
                dialogue.Show(false);
                return;
            }
            var (speaker, text) = dialogueQueue.Dequeue();
            dialogueSpeaker.text = speaker.ToUpperInvariant();
            dialogueText.text = text;
            float h = Mathf.Max(22, dialogueText.preferredHeight);
            dialogueText.rectTransform.sizeDelta = new Vector2(480, h);
            dialogue.Rect.sizeDelta = new Vector2(520, h + 54);
            dialogue.Show(true);
            dialogueUntil = Time.time + 9f;
        }

        void BuildDialogue(RectTransform tc)
        {
            // Right of the recruit card (which ends at x 650) and left of the selection panel.
            dialogue = UIKit.Panel(tc, "Dialogue", 668, 204, 520, 80, UIKit.A(Palette.Panel, 0.97f), Palette.White, 12);
            UIKit.Button(dialogue).OnClick = NextDialogue;
            dialogueSpeaker = UIKit.Label(dialogue.Rect, "", 20, 12, 300, 20, 13, Palette.White, FontStyle.Bold);
            UIKit.Label(dialogue.Rect, "click to continue", 340, 12, 160, 20, 12, Palette.Dim, FontStyle.Normal, TextAnchor.MiddleRight);
            dialogueText = UIKit.Label(dialogue.Rect, "", 20, 36, 480, 22, 16, Palette.Text, FontStyle.Normal, TextAnchor.UpperLeft);
            dialogue.Show(false);
        }

        void BuildRoster(RectTransform tl)
        {
            recruitPanel = UIKit.Panel(tl, "4 Recruit", 30, 150, 300, RecruitHeight, Palette.Panel, Palette.PanelStroke);
            var rh = UIKit.TL(recruitPanel.Rect, "Header", 0, 0, 300, 58);
            var rhb = rh.gameObject.AddComponent<UIButton>();
            UIKit.Img(rh, null, new Color(0, 0, 0, 0), true);
            rhb.OnClick = () => gm.ShowRoster(true);
            recruitChevron = UIKit.Label(rh, "▾", 18, 0, 20, 58, 16, Palette.Muted, FontStyle.Normal, TextAnchor.MiddleLeft, true);
            UIKit.Label(rh, "RECRUIT", 42, 0, 110, 58, 15, Palette.Muted, FontStyle.Bold);
            // Research points sit beside the gold, in jade.
            researchRing = UIKit.Img(UIKit.TL(rh, "ResearchRing", 160, 21, 16, 16), UIKit.Circle(16, 3f), Palette.Jade);
            researchPoints = UIKit.Label(rh, "2", 182, 0, 40, 58, 19, Palette.Jade, FontStyle.Bold);
            goldRing = UIKit.Img(UIKit.TL(rh, "GoldRing", 236, 21, 16, 16), UIKit.Circle(16, 3f), Palette.Gold);
            goldText = UIKit.Label(rh, "14", 258, 0, 40, 58, 19, Palette.Gold, FontStyle.Bold);

            for (int i = 0; i < roster.Length; i++)
            {
                var def = Defs.Unit(roster[i]);
                var row = MakeRow(recruitPanel.Rect, i, def.Icon, def.Name, def.Cost, def.Type == UnitType.Minions);
                var type = def.Type;
                row.B.OnClick = () => gm.OnRosterClick(type);
                recruitRows.Add(row);
            }

            buildPanel = UIKit.Panel(tl, "9 Build", 30, 150 + RecruitHeight + 13, 300, 60, Palette.Panel, Palette.PanelStroke);
            var bh = UIKit.TL(buildPanel.Rect, "Header", 0, 0, 300, 58);
            var bhb = bh.gameObject.AddComponent<UIButton>();
            UIKit.Img(bh, null, new Color(0, 0, 0, 0), true);
            bhb.OnClick = () => gm.ShowRoster(false);
            buildChevron = UIKit.Label(bh, "▸", 18, 0, 20, 58, 16, Palette.Muted, FontStyle.Normal, TextAnchor.MiddleLeft, true);
            UIKit.Label(bh, "BUILD", 42, 0, 150, 58, 15, Palette.Muted, FontStyle.Bold);
            buildCount = UIKit.Label(bh, $"{Defs.Buildable.Length} buildings", 150, 0, 133, 58, 15, Palette.Muted,
                FontStyle.Normal, TextAnchor.MiddleRight);

            for (int i = 0; i < Defs.Buildable.Length; i++)
            {
                var def = Defs.Building(Defs.Buildable[i]);
                var row = MakeRow(buildPanel.Rect, i, def.Icon, def.Name, def.Cost, false);
                var type = def.Type;
                row.B.OnClick = () => gm.OnBuildClick(type);
                buildRows.Add(row);
            }
        }

        /// <summary>The open Recruit panel fits the loadout: 8 rows is the mockup's 582.</summary>
        float RecruitHeight => 56 + roster.Length * 65 + 6;

        Btn MakeRow(RectTransform parent, int i, string icon, string name, int cost, bool pill)
        {
            var b = new Btn { Index = i };
            b.P = UIKit.Panel(parent, name, 11, 56 + i * 65, 278, 60, Palette.Row, Palette.RowStroke, 8);
            b.B = UIKit.Button(b.P);
            b.B.OnHover = _ => Refresh();
            var box = UIKit.Panel(b.P.Rect, "Icon", 10, 10, 40, 40, Palette.IconBox, Palette.RowStroke, 6, false);
            b.Icon = UIKit.Label(UIKit.Fill(box.Rect, "Glyph"), icon, 20, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter, true);
            b.Name = UIKit.Label(b.P.Rect, name, 64, 0, 150, 60, 19, Palette.Text);
            if (pill)
            {
                b.Pill = UIKit.Panel(b.P.Rect, "Pill", 212, 14, 54, 32, new Color(0, 0, 0, 0), Palette.Cyan, 16, false);
                b.Cost = UIKit.Label(UIKit.Fill(b.Pill.Rect, "Count"), "×10", 16, Palette.Cyan, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            else
            {
                var ring = UIKit.TL(b.P.Rect, "Cost", 232, 13, 34, 34);
                b.Ring = UIKit.Img(ring, UIKit.Circle(34, 2f), Palette.Gold);
                b.Cost = UIKit.Label(UIKit.Fill(ring, "Value"), cost.ToString(), 17, Palette.Gold, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            return b;
        }

        void BuildCard(RectTransform tl)
        {
            var arrow = UIKit.TL(tl, "CardArrow", 0, 0, 16, 16);
            arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.localRotation = Quaternion.Euler(0, 0, 45);
            UIKit.Img(arrow, UIKit.Round(2), Palette.Panel);
            UIKit.Img(UIKit.Fill(arrow, "Stroke"), UIKit.Round(2, 1.5f), Palette.White);
            cardArrow = arrow;

            card = UIKit.Panel(tl, "Card", 350, 150, 300, 370, Palette.Panel, UIKit.A(Palette.White, 0.95f), 12);
            var r = card.Rect;
            cardTitle = UIKit.Label(r, "", 21, 18, 260, 36, 27, Palette.Text, FontStyle.Bold);
            cardRole = UIKit.Label(r, "", 21, 54, 260, 20, 14, Palette.Muted);
            for (int i = 0; i < 5; i++)
            {
                var p = UIKit.Panel(r, "Stat", 0, 0, 50, 62, new Color(0, 0, 0, 0), Palette.RowStroke, 7, false);
                var v = UIKit.Label(p.Rect, "", 0, 6, 50, 32, 24, Palette.Text, FontStyle.Bold, TextAnchor.MiddleCenter);
                var l = UIKit.Label(p.Rect, "", 0, 36, 50, 20, 12, Palette.Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
                cardStats.Add((p, v, l));
            }
            passiveLabel = UIKit.Label(r, "PASSIVE", 21, 0, 200, 18, 13, Palette.Muted, FontStyle.Bold);
            passiveName = UIKit.Label(r, "", 21, 0, 258, 24, 18, Palette.Text, FontStyle.Bold);
            passiveText = UIKit.Label(r, "", 21, 0, 258, 20, 14, Palette.Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            activeLabel = UIKit.Label(r, "ACTIVE", 21, 0, 200, 18, 13, Palette.Muted, FontStyle.Bold);
            activeChip = UIKit.Panel(r, "ManaChip", 0, 0, 70, 24, UIKit.A(Palette.Lilac, 0.15f), UIKit.A(Palette.Lilac, 0.5f), 6, false);
            activeChipText = UIKit.Label(UIKit.Fill(activeChip.Rect, "T"), "", 12, Palette.Lilac, FontStyle.Bold, TextAnchor.MiddleCenter);
            activeName = UIKit.Label(r, "", 21, 0, 200, 24, 18, Palette.Text, FontStyle.Bold);
            activeText = UIKit.Label(r, "", 21, 0, 258, 20, 14, Palette.Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            costText = UIKit.Label(r, "", 21, 0, 130, 24, 16, Palette.Muted);
            goldPreview = UIKit.Label(r, "", 120, 0, 159, 24, 17, Palette.Gold, FontStyle.Bold, TextAnchor.MiddleRight);
            problemText = UIKit.Label(r, "", 21, 0, 258, 20, 13, Palette.Red);

            sizeLabel = UIKit.Label(r, "SQUAD SIZE", 21, 0, 200, 18, 13, Palette.Muted, FontStyle.Bold);
            foreach (var n in new[] { 1, Defs.MinionSquad })
            {
                var p = UIKit.Panel(r, "Size" + n, 21, 0, 125, 40, Palette.Row, Palette.RowStroke, 8);
                var b = UIKit.Button(p);
                int count = n;
                b.OnClick = () => gm.SetMinionCount(count);
                b.OnHover = _ => Refresh();
                var t = UIKit.Label(UIKit.Fill(p.Rect, "T"), n == 1 ? "×1  single" : $"×{n}  squad", 16, Palette.Text, FontStyle.Bold, TextAnchor.MiddleCenter);
                sizeBtns.Add((p, t, b, n));
            }

            confirmBtn = UIKit.Panel(r, "Confirm", 21, 0, 147, 50, Palette.White, Palette.White, 8);
            confirmB = UIKit.Button(confirmBtn);
            confirmB.OnClick = () => gm.ConfirmPlacement();
            confirmB.OnHover = _ => Refresh();
            confirmText = UIKit.Label(UIKit.Fill(confirmBtn.Rect, "T"), "RECRUIT", 18, Palette.Background, FontStyle.Bold, TextAnchor.MiddleCenter);
            cancelBtn = UIKit.Panel(r, "Cancel", 178, 0, 101, 50, new Color(0, 0, 0, 0), Palette.RowStroke, 8);
            var cb = UIKit.Button(cancelBtn);
            cb.OnClick = () => gm.Cancel();
            cb.OnHover = _ => Refresh();
            UIKit.Label(UIKit.Fill(cancelBtn.Rect, "T"), "Cancel", 17, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        void BuildManaBar(RectTransform tc)
        {
            var bar = UIKit.Panel(tc, "2 Mana", 536, 30, 528, 108, Palette.Panel, Palette.PanelStroke);
            var xs = new[] { 20, 120, 320, 419 };
            for (int i = 0; i < gm.Spells.Count; i++)
            {
                var def = Defs.Spell(gm.Spells[i]);
                var b = new Btn { Index = i };
                b.P = UIKit.Panel(bar.Rect, def.Name, xs[i], 10, 88, 88, Palette.Row, Palette.RowStroke, 8);
                b.B = UIKit.Button(b.P);
                var type = def.Type;
                b.B.OnClick = () => gm.OnSpellClick(type);
                b.B.OnHover = h =>
                {
                    gm.HoverHint = h ? def.Text : null;
                    Refresh();
                };
                b.Icon = UIKit.Label(b.P.Rect, def.Icon, 0, 14, 88, 40, 28, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter, true);
                b.Name = UIKit.Label(b.P.Rect, def.Name.ToUpperInvariant(), 0, 56, 88, 22, 14, Palette.Text, FontStyle.Bold, TextAnchor.MiddleCenter);
                var badge = UIKit.Panel(b.P.Rect, "Cost", 6, 6, 22, 22, Palette.IconBox, Palette.RowStroke, 5, false);
                b.Cost = UIKit.Label(UIKit.Fill(badge.Rect, "V"), def.Cost.ToString(), 14, Palette.Lilac, FontStyle.Bold, TextAnchor.MiddleCenter);
                spellBtns.Add(b);
            }
            var orb = UIKit.TL(bar.Rect, "Orb", 223, 13, 82, 82);
            UIKit.Img(orb, UIKit.Circle(82), Palette.OrbFill);
            UIKit.Img(UIKit.Fill(orb, "Ring"), UIKit.Circle(82, 3f), Palette.Lilac);
            manaText = UIKit.Label(orb, "6", 0, 10, 82, 40, 32, Palette.White, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIKit.Label(orb, "MANA", 0, 46, 82, 18, 12, Palette.Lilac, FontStyle.Bold, TextAnchor.MiddleCenter);

            hintTop = UIKit.Panel(tc, "Hint", 605, 150, 390, 40, UIKit.A(Palette.Background, 0.92f), UIKit.A(Palette.Text, 0.85f), 20, false);
            hintTopText = UIKit.Label(UIKit.Fill(hintTop.Rect, "T"), "", 17, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        void BuildSelection(RectTransform tr)
        {
            selPanel = UIKit.Panel(tr, "3 Selection", 20, 30, 350, 140, Palette.Panel, Palette.PanelStroke);
            var r = selPanel.Rect;
            UIKit.Label(r, "SELECTING", 21, 14, 200, 20, 13, Palette.Muted, FontStyle.Bold);
            selName = UIKit.Label(r, "", 21, 32, 200, 42, 30, Palette.Text, FontStyle.Bold);
            selName.horizontalOverflow = HorizontalWrapMode.Overflow;
            selDot = UIKit.Img(UIKit.TL(r, "Dot", 21, 85, 10, 10), UIKit.Circle(10), Palette.Cyan);
            selStatus = UIKit.Label(r, "", 37, 78, 180, 24, 15, Palette.Cyan);
            selExtra = UIKit.Label(r, "", 21, 104, 200, 22, 13, Palette.Muted);
            statBox = UIKit.Panel(r, "Stats", 220, 16, 112, 108, new Color(0, 0, 0, 0), Palette.RowStroke, 8, false);
            statDivider = UIKit.Img(UIKit.TL(statBox.Rect, "Divider", 0, 53.5f, 112, 1.5f), null, Palette.RowStroke);
            stat1 = UIKit.Label(statBox.Rect, "", 0, 0, 112, 54, 15, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter);
            stat2 = UIKit.Label(statBox.Rect, "", 0, 54, 112, 54, 15, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter);

            abilityBtn = UIKit.Panel(tr, "Ability", 20, 182, 350, 44, UIKit.A(Palette.Lilac, 0.08f), Palette.Lilac, 10);
            abilityB = UIKit.Button(abilityBtn);
            abilityB.OnClick = () => gm.OnAbilityClick();
            abilityB.OnHover = h =>
            {
                var a = gm.SelUnit != null ? Defs.AbilityOf(gm.SelUnit.Type) : null;
                gm.HoverHint = h && a != null ? $"{a.Name}: {a.Text}" : null;
                Refresh();
            };
            abilityText = UIKit.Label(UIKit.Fill(abilityBtn.Rect, "T"), "", 16, Palette.Lilac, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        void BuildBottom(RectTransform bc, RectTransform br)
        {
            hintPlace = UIKit.Panel(bc, "PlaceHint", 605, 30, 390, 40, UIKit.A(Palette.Background, 0.92f), Palette.Cyan, 20, false);
            hintPlaceText = UIKit.Label(UIKit.Fill(hintPlace.Rect, "T"), "", 17, Palette.Cyan, FontStyle.Normal, TextAnchor.MiddleCenter);

            readyChip = UIKit.Panel(bc, "10 Ready", 695, 166, 210, 34, UIKit.A(Palette.Background, 0.9f), Palette.Cyan, 17, false);
            readyDot = UIKit.Img(UIKit.TL(readyChip.Rect, "Dot", 24, 12, 10, 10), UIKit.Circle(10), Palette.Cyan);
            readyText = UIKit.Label(readyChip.Rect, "", 40, 0, 165, 34, 15, Palette.Cyan, FontStyle.Bold);

            endTurn = UIKit.Panel(bc, "8 EndTurn", 695, 210, 210, 60, Palette.White, Palette.White, 10);
            endB = UIKit.Button(endTurn);
            endB.OnClick = () => gm.EndTurn();
            endB.OnHover = _ => Refresh();
            endText = UIKit.Label(UIKit.Fill(endTurn.Rect, "T"), "", 21, Palette.Background, FontStyle.Bold, TextAnchor.MiddleCenter);

            nextBtn = UIKit.Panel(bc, "NextUnit", 925, 210, 60, 60, new Color(0, 0, 0, 0), Palette.Cyan, 10);
            nextB = UIKit.Button(nextBtn);
            nextB.OnClick = () => gm.NextUnit();
            nextB.OnHover = _ => Refresh();
            nextText = UIKit.Label(UIKit.Fill(nextBtn.Rect, "T"), "▸|", 22, Palette.Cyan, FontStyle.Bold, TextAnchor.MiddleCenter, true);

            // Undo the last move (also Z / Backspace), mirroring ▸| on the other side of End Turn.
            undoBtn = UIKit.Panel(bc, "Undo", 615, 210, 60, 60, new Color(0, 0, 0, 0), Palette.Text, 10);
            undoB = UIKit.Button(undoBtn);
            undoB.OnClick = () => gm.Undo();
            undoB.OnHover = h =>
            {
                gm.HoverHint = h ? "Undo last move (Z)" : null;
                Refresh();
            };
            undoText = UIKit.Label(UIKit.Fill(undoBtn.Rect, "T"), "↶", 26, Palette.Text, FontStyle.Bold, TextAnchor.MiddleCenter, true);

            menuBtn = UIKit.Panel(br, "Menu", 55, 240, 50, 50, Palette.Panel, Palette.PanelStroke, 25);
            var mb = UIKit.Button(menuBtn);
            mb.OnClick = () => gm.ToggleMenu();
            UIKit.Label(UIKit.Fill(menuBtn.Rect, "T"), "≡", 26, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter, true);

            // Music toggle (also M). A slash across the note means muted.
            muteBtn = UIKit.Panel(br, "Mute", 55, 182, 50, 50, Palette.Panel, Palette.PanelStroke, 25);
            UIKit.Button(muteBtn).OnClick = () => gm.ToggleMusic();
            muteText = UIKit.Label(UIKit.Fill(muteBtn.Rect, "T"), "♪", 26, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter, true);
            muteSlash = UIKit.Img(UIKit.TL(muteBtn.Rect, "Slash", 0, 0, 4, 30), UIKit.Round(2), Palette.Muted);
            var slash = muteSlash.rectTransform;
            slash.pivot = new Vector2(0.5f, 0.5f);
            slash.anchoredPosition = new Vector2(25, -25);
            slash.localRotation = Quaternion.Euler(0, 0, 45);

            var area = UIKit.TL(br, "7 Minimap", 120, 40, 250, 250);
            minimap = new Minimap(area, gm);
            UIKit.Img(UIKit.TL(br, "MinimapRing", 119, 39, 252, 252), UIKit.Circle(252, 2f), Palette.PanelStroke);
        }

        void BuildOverlay()
        {
            overlay = UIKit.Fill(root, "Overlay");
            UIKit.Img(overlay, null, new Color(0.02f, 0.03f, 0.04f, 0.72f), true);
            var box = UIKit.Panel(overlay, "Box", 0, 0, 440, 330, Palette.Panel, Palette.PanelStroke, 14);
            box.Rect.anchorMin = box.Rect.anchorMax = box.Rect.pivot = new Vector2(0.5f, 0.5f);
            box.Rect.anchoredPosition = Vector2.zero;
            overlayTitle = UIKit.Label(box.Rect, "", 30, 26, 380, 44, 34, Palette.Text, FontStyle.Bold, TextAnchor.MiddleCenter);
            overlaySub = UIKit.Label(box.Rect, "", 30, 74, 380, 80, 15, Palette.Muted, FontStyle.Normal, TextAnchor.UpperCenter);

            (Panel, Text, UIButton) Make(float y, bool primary)
            {
                var p = UIKit.Panel(box.Rect, "Btn", 70, y, 300, 46, primary ? Palette.White : new Color(0, 0, 0, 0),
                    primary ? Palette.White : Palette.RowStroke, 8);
                var b = UIKit.Button(p);
                var t = UIKit.Label(UIKit.Fill(p.Rect, "T"), "", 17, primary ? Palette.Background : Palette.Text,
                    FontStyle.Bold, TextAnchor.MiddleCenter);
                return (p, t, b);
            }
            (overlayPrimary, overlayPrimaryText, overlayPrimaryB) = Make(150, true);
            (overlaySecondary, overlaySecondaryText, overlaySecondaryB) = Make(206, false);
            (overlayTertiary, overlayTertiaryText, overlayTertiaryB) = Make(262, false);
            overlay.gameObject.SetActive(false);
        }

        // ================================================================ badges and popups

        public HudBadge CreateBadge()
        {
            var rt = UIKit.TL(badgeLayer, "Badge", 0, 0, 34, 26);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            var b = new HudBadge { Rect = rt };
            b.Fill = UIKit.Img(rt, UIKit.Round(6), Palette.IconBox);
            b.Stroke = UIKit.Img(UIKit.Fill(rt, "Stroke"), UIKit.Round(6, 2f), Palette.Cyan);
            b.Label = UIKit.Label(UIKit.Fill(rt, "T"), "", 16, Palette.Text, FontStyle.Bold, TextAnchor.MiddleCenter);
            var dot = UIKit.TL(rt, "Dot", 0, 0, 11, 11);
            dot.anchorMin = dot.anchorMax = new Vector2(1, 1);
            dot.pivot = new Vector2(0.5f, 0.5f);
            dot.anchoredPosition = new Vector2(-1, -1);
            UIKit.Img(dot, UIKit.Circle(13), Palette.Background);
            b.Dot = UIKit.Img(UIKit.Fill(dot, "In"), UIKit.Circle(9), Palette.Cyan);
            ((RectTransform)b.Dot.transform).offsetMin = new Vector2(1.5f, 1.5f);
            ((RectTransform)b.Dot.transform).offsetMax = new Vector2(-1.5f, -1.5f);

            var warn = UIKit.TL(rt, "Warn", 0, 0, 18, 18);
            warn.anchorMin = warn.anchorMax = new Vector2(0, 0.5f);
            warn.pivot = new Vector2(1f, 0.5f);
            warn.anchoredPosition = new Vector2(-3, 0);
            UIKit.Img(warn, UIKit.Circle(18), Palette.Red);
            UIKit.Label(UIKit.Fill(warn, "T"), "!", 14, Palette.White, FontStyle.Bold, TextAnchor.MiddleCenter);
            b.Warn = warn;
            return b;
        }

        (Panel, Text) MakeTag(string name, Color stroke)
        {
            var p = UIKit.Panel(popupLayer, name, 0, 0, 80, 28, UIKit.A(Palette.Background, 0.94f), stroke, 7, false);
            p.Rect.anchorMin = p.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            p.Rect.pivot = new Vector2(0.5f, 0f);
            var t = UIKit.Label(UIKit.Fill(p.Rect, "T"), "", 16, stroke, FontStyle.Bold, TextAnchor.MiddleCenter);
            p.Show(false);
            return (p, t);
        }

        void PlaceTag(Panel tag, Text text, Entity e, string label)
        {
            var v = e != null ? gm.Views.FirstOrDefault(x => x.Entity == e) : null;
            Vector2 local = default;
            bool show = label != null && v != null && ToCanvas(popupLayer, v.BadgeAnchor, out local);
            tag.Show(show);
            if (!show) return;
            text.text = label;
            tag.Rect.sizeDelta = new Vector2(text.preferredWidth + 22, 28);
            tag.Rect.anchoredPosition = local + new Vector2(0, 32);
        }

        public void Popup(Vector3 world, string text, Color color)
        {
            var rt = UIKit.TL(popupLayer, "Popup", 0, 0, 160, 30);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            var t = UIKit.Label(UIKit.Fill(rt, "T"), text, 22, color, FontStyle.Bold, TextAnchor.MiddleCenter);
            t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.85f);
            popups.Add(new PopupItem { R = rt, T = t, World = world, Born = Time.time, C = color });
        }

        bool ToCanvas(RectTransform layer, Vector3 world, out Vector2 local)
        {
            local = default;
            var sp = gm.Rig.Cam.WorldToScreenPoint(world);
            if (sp.z < 0) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, sp, null, out local);
        }

        void LateUpdate()
        {
            if (gm == null || gm.Model == null || gm.Rig == null || gm.Rig.Cam == null) return;
            var m = gm.Model;

            foreach (var v in gm.Views)
            {
                var b = v.Badge;
                if (b == null) continue;
                bool show = v.Root.gameObject.activeInHierarchy && v.Entity != null && !v.Entity.Dead;
                string text = null;
                Color stroke = Palette.Cyan, label = Palette.Cyan;
                bool icon = false, dot = false;
                if (show && v.Entity is Unit u)
                {
                    bool mine = u.Faction == Faction.Player;
                    bool selected = gm.Selected == u;
                    bool done = mine && m.Current == Faction.Player && !m.CanAct(u);
                    text = u.Hp.ToString();
                    if (!mine) stroke = label = Palette.Red;
                    else if (selected) stroke = label = Palette.White;
                    else if (done) { stroke = label = Palette.Grey; text = "✓"; icon = true; }
                    dot = mine && m.Current == Faction.Player && m.CanAct(u) && gm.Mode != Mode.Busy;
                }
                else if (show && v.Entity is Building bd)
                {
                    if (!bd.Complete) { text = $"⌛{bd.TurnsLeft}"; icon = true; stroke = label = Palette.Grey; }
                    else if (bd.Hp < bd.MaxHp) { text = bd.Hp.ToString(); stroke = label = Palette.Of(bd.Faction); }
                    else show = false;
                }
                if (show && text != null && ToCanvas(badgeLayer, v.BadgeAnchor, out var local))
                {
                    if (!b.Rect.gameObject.activeSelf) b.Rect.gameObject.SetActive(true);
                    b.Rect.anchoredPosition = local;
                    b.Label.text = text;
                    b.Label.font = icon ? UIKit.IconFont : UIKit.Font;
                    b.Label.color = label;
                    b.Stroke.color = stroke;
                    b.Rect.sizeDelta = new Vector2(Mathf.Max(34, b.Label.preferredWidth + 18), 26);
                    if (b.Dot.transform.parent.gameObject.activeSelf != dot) b.Dot.transform.parent.gameObject.SetActive(dot);
                    bool warn = v.Entity is Unit wu && gm.InDanger(wu) && gm.Mode != Mode.Busy;
                    if (b.Warn.gameObject.activeSelf != warn) b.Warn.gameObject.SetActive(warn);
                }
                else if (b.Rect.gameObject.activeSelf) b.Rect.gameObject.SetActive(false);
            }

            var f = gm.Mode == Mode.Unit ? gm.Forecast : null;
            PlaceTag(fcTarget, fcTargetText, gm.ForecastTarget,
                f == null ? null : f.Kills ? $"−{f.Dealt}  <color=#e5484d>KILL</color>" : $"−{f.Dealt}");
            PlaceTag(fcSelf, fcSelfText, gm.ForecastAttacker,
                f == null || !f.Counter ? null : f.Dies ? $"−{f.Taken}  LOST" : $"−{f.Taken}");

            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                float age = (Time.time - p.Born) / 1.1f;
                if (age >= 1f || !ToCanvas(popupLayer, p.World, out var local))
                {
                    Object.Destroy(p.R.gameObject);
                    popups.RemoveAt(i);
                    continue;
                }
                p.R.anchoredPosition = local + new Vector2(0, 10 + age * 46f);
                p.T.color = UIKit.A(p.C, age < 0.6f ? 1f : 1f - (age - 0.6f) / 0.4f);
            }

            minimap.Draw();
            if (dialogue.Rect.gameObject.activeSelf && Time.time > dialogueUntil) NextDialogue();
        }

        // ================================================================ refresh

        public void Refresh()
        {
            if (gm == null || gm.Model == null || selPanel == null) return;
            RefreshObjective();
            RefreshManaBar();
            RefreshSelection();
            RefreshRoster();
            RefreshCard();
            RefreshResearch();
            RefreshBottom();
        }

        bool PlayerTurn => gm.Model.Current == Faction.Player && gm.Mode != Mode.Busy && gm.Mode != Mode.Over;

        void RefreshManaBar()
        {
            var m = gm.Model;
            var side = m.Of(Faction.Player);
            manaText.text = side.Mana.ToString();
            foreach (var b in spellBtns)
            {
                var type = gm.Spells[b.Index];
                var def = Defs.Spell(type);
                bool active = gm.Mode == Mode.Spell && gm.ActiveSpell == type;
                bool afford = side.Mana >= def.Cost && PlayerTurn;
                b.B.Interactable = PlayerTurn;
                var fill = active ? Palette.White : b.B.Hovered && afford ? Palette.RowHover : Palette.Row;
                b.P.Colors(fill, active ? Palette.White : Palette.RowStroke);
                var fg = active ? Palette.Background : afford ? Palette.Text : Palette.Dim;
                b.Icon.color = fg;
                b.Name.color = fg;
                b.Cost.color = afford || active ? Palette.Lilac : Palette.Dim;
            }

            string hint = gm.HintText;
            hintTop.Show(!string.IsNullOrEmpty(hint));
            if (!string.IsNullOrEmpty(hint))
            {
                hintTopText.text = hint;
                float w = Mathf.Max(260, hintTopText.preferredWidth + 56);
                UIKit.Place(hintTop.Rect, 800 - w / 2, 150, w, 40);
                bool lilac = gm.Mode == Mode.Spell || gm.Mode == Mode.Ability || (gm.HoverHint != null && gm.Flash == null);
                hintTop.Stroke.color = lilac ? Palette.Lilac : UIKit.A(Palette.Text, 0.85f);
                hintTopText.color = lilac ? Palette.Lilac : gm.Flash != null ? Palette.Gold : Palette.Text;
            }
        }

        static string Stat(int value, string label) => $"<size=30><b>{value}</b></size>  <size=13><b>{label}</b></size>";
        static string Stat(string value, string label) => $"<size=30><b>{value}</b></size>  <size=13><b>{label}</b></size>";

        void RefreshSelection()
        {
            var m = gm.Model;
            var e = gm.Selected;
            bool showStats = e != null;
            statBox.Show(showStats);
            selDot.gameObject.SetActive(true);
            selExtra.text = "";

            if (e == null)
            {
                selName.text = "Nothing";
                selDot.gameObject.SetActive(false);
                selStatus.rectTransform.anchoredPosition = new Vector2(21, -78);
                selStatus.text = gm.Mode == Mode.Over ? "Game over"
                    : m.Current == Faction.Player ? "Click a squad or your Keep" : "Enemy is moving";
                selStatus.color = Palette.Muted;
            }
            else
            {
                selStatus.rectTransform.anchoredPosition = new Vector2(37, -78);
                selName.text = e.Name;
                if (e is Unit u)
                {
                    bool mine = u.Faction == Faction.Player;
                    string status;
                    Color c;
                    if (!mine) { status = "Enemy squad"; c = Palette.Red; }
                    else if (m.Current != Faction.Player) { status = "Waiting"; c = Palette.Grey; }
                    else if (m.CanAct(u)) { status = "Ready to act"; c = Palette.Cyan; }
                    else if (u.Fresh) { status = "Just recruited"; c = Palette.Grey; }
                    else { status = "Done this turn"; c = Palette.Grey; }
                    selStatus.text = status;
                    selStatus.color = c;
                    selDot.color = c;
                    var extra = $"DEF {m.DefenseOf(u)} · MOVE {m.MoveOf(u)}";
                    if (u.Def.Range > 1) extra += $" · RANGE {m.RangeOf(u, u.Pos)}";
                    if (m.T(u.Pos).Height > 0) extra += " · <color=#f4f7f8>HIGH GROUND</color>";
                    if (m.T(u.Pos).Forest) extra += " · <color=#5fd3a2>FOREST</color>";
                    if (u.WardTurns > 0) extra += " · <color=#46b8f2>WARD</color>";
                    if (u.GuardTurns > 0) extra += " · <color=#46b8f2>GUARD</color>";
                    if (u.Rooted) extra += " · <color=#e5484d>PINNED</color>";
                    selExtra.text = extra;
                    stat1.text = Stat(m.AttackOf(u, false), "AD");
                    stat2.text = Stat(u.Hp, "HP");
                }
                else if (e is Building b)
                {
                    bool mine = b.Faction == Faction.Player;
                    string status;
                    Color c = mine ? Palette.Cyan : Palette.Red;
                    if (!mine) status = "Enemy " + b.Name.ToLowerInvariant();
                    else if (!b.Complete) { status = $"Under construction · {b.TurnsLeft}"; c = Palette.Grey; }
                    else if (b.Type == BuildingType.Keep && gm.Mode == Mode.Place)
                        status = (gm.PlaceIsBuilding ? "Building: " + Defs.Building(gm.PlaceBuilding).Name : "Recruiting: " + Defs.Unit(gm.PlaceUnit).Name);
                    else if (b.Type == BuildingType.Keep) status = gm.LastOrder ?? "Ready to recruit";
                    else if (b.Def.Researches)
                        status = b.Research.HasValue ? $"{Defs.Tech(b.Research.Value).Name} · {b.ResearchLeft}" : mine ? "Idle: pick research" : b.Def.PassiveName;
                    else status = b.Def.PassiveName;
                    selStatus.text = status;
                    selStatus.color = mine && b.Type == BuildingType.Keep ? Palette.Muted : c;
                    selDot.gameObject.SetActive(!(mine && b.Type == BuildingType.Keep));
                    if (mine && b.Type == BuildingType.Keep) selStatus.rectTransform.anchoredPosition = new Vector2(21, -78);
                    selDot.color = c;
                    selExtra.text = $"DEF {b.Def.Defense} · {b.Def.PassiveName}";
                    stat1.text = Stat(b.Hp, "HP");
                    stat2.text = mine && b.Type == BuildingType.Keep
                        ? Stat($"{m.Of(Faction.Player).SlotsUsed}/{m.MaxSlots(Faction.Player)}", "SLOTS")
                        : Stat(b.Def.Defense, "DEF");
                }
            }

            // The squad's ability, with its state: ready (Q), cooling down, or still to be researched.
            var su = gm.SelUnit;
            var ability = su != null && su.Faction == Faction.Player ? Defs.AbilityOf(su.Type) : null;
            abilityBtn.Show(ability != null);
            if (ability != null)
            {
                bool known = m.AbilityOf(su) != null;
                bool can = PlayerTurn && known && m.CanUseAbility(su);
                bool active = gm.Mode == Mode.Ability;
                abilityB.Interactable = true;
                abilityBtn.Colors(active ? Palette.Lilac : UIKit.A(Palette.Lilac, can && abilityB.Hovered ? 0.18f : 0.06f),
                    can || active ? Palette.Lilac : Palette.RowStroke);
                abilityText.color = active ? Palette.Background : can ? Palette.Lilac : Palette.Dim;
                string name = ability.Name.ToUpperInvariant();
                abilityText.text = !known ? $"{name}  ·  research {Defs.Tech(ability.Unlock.Value).Name}"
                    : su.Cooldown > 0 ? $"{name}  ·  ready in {su.Cooldown}"
                    : $"{name}  ·  Q   (every {ability.Cooldown} turns)";
            }
        }

        // ================================================================ research

        void BuildResearch(RectTransform tl)
        {
            researchPanel = UIKit.Panel(tl, "Research", 350, 150, 300, 100, Palette.Panel, UIKit.A(Palette.Jade, 0.8f), 12);
            var r = researchPanel.Rect;
            researchTitle = UIKit.Label(r, "", 21, 14, 260, 22, 13, Palette.Jade, FontStyle.Bold);
            researchSub = UIKit.Label(r, "", 21, 36, 260, 22, 14, Palette.Muted);
            for (int i = 0; i < 6; i++)
            {
                var b = new Btn { Index = i };
                b.P = UIKit.Panel(r, "Tech", 11, 66 + i * 82, 278, 76, Palette.Row, Palette.RowStroke, 8);
                b.B = UIKit.Button(b.P);
                int index = i;
                b.B.OnClick = () =>
                {
                    var techs = TechsFor(researchFor);
                    if (index < techs.Count) gm.OnResearchClick(techs[index]);
                };
                b.B.OnHover = _ => Refresh();
                var box = UIKit.Panel(b.P.Rect, "Icon", 10, 10, 36, 36, Palette.IconBox, Palette.RowStroke, 6, false);
                b.Icon = UIKit.Label(UIKit.Fill(box.Rect, "Glyph"), "", 18, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter, true);
                b.Name = UIKit.Label(b.P.Rect, "", 56, 6, 160, 24, 16, Palette.Text, FontStyle.Bold);
                var desc = UIKit.Label(b.P.Rect, "", 56, 30, 214, 40, 12, Palette.Muted, FontStyle.Normal, TextAnchor.UpperLeft);
                var status = UIKit.Label(b.P.Rect, "", 170, 6, 98, 24, 13, Palette.Jade, FontStyle.Bold, TextAnchor.MiddleRight);
                researchRows.Add((b, desc, status));
            }
            researchPanel.Show(false);
        }

        /// <summary>Techs a building studies in this battle, tier 1 first.</summary>
        List<TechType> TechsFor(BuildingType b) =>
            Defs.AllTechs.Where(t => Defs.Tech(t).Building == b && gm.Model.TechPool.Contains(t))
                .OrderBy(t => Defs.Tech(t).Tier).ToList();

        void RefreshResearch()
        {
            var m = gm.Model;
            var b = gm.SelBuilding;
            bool show = gm.Mode == Mode.Building && b != null && b.Faction == Faction.Player && b.Def.Researches && !b.Dead;
            researchPanel.Show(show);
            if (!show) return;
            researchFor = b.Type;
            var side = m.Of(Faction.Player);
            var techs = TechsFor(b.Type);
            researchTitle.text = $"RESEARCH · {b.Name.ToUpperInvariant()}";
            researchSub.text = b.Research.HasValue
                ? $"Studying {Defs.Tech(b.Research.Value).Name}: {b.ResearchLeft} {(b.ResearchLeft == 1 ? "turn" : "turns")} left"
                : $"<color=#5fd3a2>{side.Research} RP</color> (research points) · +{m.ResearchIncome(Faction.Player)} a turn";
            if (techs.Count == 0) researchSub.text = "Nothing to study here in this battle";
            for (int i = 0; i < researchRows.Count; i++)
            {
                var (row, desc, status) = researchRows[i];
                row.P.Show(i < techs.Count);
                if (i >= techs.Count) continue;
                var t = techs[i];
                var def = Defs.Tech(t);
                string problem = m.ResearchProblem(b, t);
                bool done = m.Has(Faction.Player, t);
                bool going = b.Research == t;
                bool locked = def.Requires.HasValue && !m.Has(Faction.Player, def.Requires.Value);
                bool ok = problem == null && PlayerTurn;
                row.Icon.text = def.Icon;
                row.Name.text = def.Name;
                desc.text = def.Desc;
                status.text = done ? "✓ Done" : going ? $"⌛ {b.ResearchLeft}" : locked ? "Locked" : $"{def.Cost} RP · {def.Turns}t";
                status.color = done ? Palette.Cyan : going || ok ? Palette.Jade : Palette.Dim;
                row.B.Interactable = PlayerTurn;
                row.P.Colors(ok && row.B.Hovered ? Palette.RowHover : Palette.Row, going ? Palette.Jade : done ? UIKit.A(Palette.Cyan, 0.5f) : Palette.RowStroke);
                row.Name.color = ok || done || going ? Palette.Text : Palette.Muted;
                row.Icon.color = row.Name.color;
                if (row.B.Hovered && problem != null && !done && !going) desc.text = $"<color=#e5484d>{problem}</color> · {def.Desc}";
            }
            UIKit.Place(researchPanel.Rect, 350, 150, 300, 66 + techs.Count * 82 + 6);
        }

        void RefreshRoster()
        {
            var m = gm.Model;
            var side = m.Of(Faction.Player);
            bool open = gm.RecruitOpen;
            goldText.text = side.Gold.ToString();
            researchPoints.text = side.Research.ToString();

            UIKit.Place(recruitPanel.Rect, 30, 150, 300, open ? RecruitHeight : 60);
            float buildY = open ? 150 + RecruitHeight + 13 : 230;
            UIKit.Place(buildPanel.Rect, 30, buildY, 300, open ? 60 : 56 + Defs.Buildable.Length * 65 + 6);
            recruitChevron.text = open ? "▾" : "▸";
            buildChevron.text = open ? "▸" : "▾";
            // When Recruit is collapsed its header still shows gold, as in screen 3.
            goldRing.gameObject.SetActive(true);
            goldText.gameObject.SetActive(true);

            for (int i = 0; i < recruitRows.Count; i++)
            {
                var row = recruitRows[i];
                row.P.Show(open);
                if (!open) continue;
                var type = roster[i];
                bool selected = gm.Mode == Mode.Place && !gm.PlaceIsBuilding && gm.PlaceUnit == type;
                bool ok = PlayerTurn && m.RecruitProblem(Faction.Player, type) == null;
                StyleRow(row, selected, ok);
                if (type == UnitType.Minions)
                {
                    row.Cost.text = $"×{side.Reserve}";
                    row.Pill.Stroke.color = ok ? Palette.Cyan : Palette.Dim;
                    row.Cost.color = ok ? Palette.Cyan : Palette.Dim;
                }
            }
            for (int i = 0; i < buildRows.Count; i++)
            {
                var row = buildRows[i];
                row.P.Show(!open);
                if (open) continue;
                var type = Defs.Buildable[i];
                bool selected = gm.Mode == Mode.Place && gm.PlaceIsBuilding && gm.PlaceBuilding == type;
                bool ok = PlayerTurn && m.BuildProblem(Faction.Player, type) == null;
                StyleRow(row, selected, ok);
            }
        }

        void StyleRow(Btn row, bool selected, bool ok)
        {
            row.B.Interactable = PlayerTurn;
            row.P.Colors(selected || row.B.Hovered ? Palette.RowHover : Palette.Row, selected ? Palette.White : Palette.RowStroke);
            row.Name.color = ok || selected ? Palette.Text : Palette.Dim;
            row.Icon.color = ok || selected ? Palette.Text : Palette.Dim;
            if (row.Ring != null)
            {
                row.Ring.color = ok || selected ? Palette.Gold : UIKit.A(Palette.Gold, 0.35f);
                row.Cost.color = row.Ring.color;
            }
        }

        void RefreshCard()
        {
            bool show = gm.Mode == Mode.Place;
            card.Show(show);
            cardArrow.gameObject.SetActive(show);
            hintPlace.Show(show);
            if (!show) return;

            var m = gm.Model;
            var side = m.Of(Faction.Player);
            int cost;
            string problem = gm.PlacementProblem;
            var stats = new List<(string v, string l, bool lilac)>();
            string pName, pText, aName = null, aText = null, aChip = null;
            int rowIndex;

            if (gm.PlaceIsBuilding)
            {
                var d = Defs.Building(gm.PlaceBuilding);
                cardTitle.text = d.Name;
                cardRole.text = d.Role;
                stats.Add((d.Health.ToString(), "HP", false));
                stats.Add((d.Defense.ToString(), "DEF", false));
                stats.Add((d.Turns.ToString(), "TURNS", false));
                pName = d.PassiveName;
                pText = d.PassiveText;
                cost = d.Cost;
                rowIndex = System.Array.IndexOf(Defs.Buildable, gm.PlaceBuilding);
                confirmText.text = "BUILD";
                hintPlaceText.text = "Pick a cyan tile, then press Build";
            }
            else
            {
                var d = Defs.Unit(gm.PlaceUnit);
                cardTitle.text = d.Name;
                cardRole.text = d.Role;
                bool minions = d.Type == UnitType.Minions;
                int n = gm.MinionsToSpawn;
                stats.Add(((minions ? n : d.Attack).ToString(), "AD", false));
                stats.Add(((minions ? n : d.Health).ToString(), "HP", false));
                stats.Add((d.Defense.ToString(), "DEF", false));
                stats.Add((d.Move.ToString(), "MOVE", false));
                if (d.Range > 1) stats.Add((d.Range.ToString(), "RANGE", false));
                pName = d.PassiveName;
                pText = d.PassiveText;
                var ab = Defs.Ability(d.Ability);
                if (ab != null)
                {
                    bool locked = ab.Unlock.HasValue && !m.Has(Faction.Player, ab.Unlock.Value);
                    aName = ab.Name;
                    aText = locked ? ab.Text + $" <color=#5fd3a2>Research {Defs.Tech(ab.Unlock.Value).Name} first.</color>" : ab.Text;
                    aChip = locked ? "RESEARCH" : $"EVERY {ab.Cooldown}";
                }
                cost = d.Cost;
                rowIndex = System.Array.IndexOf(roster, gm.PlaceUnit);
                confirmText.text = "RECRUIT";
                hintPlaceText.text = "Pick a cyan tile, then press Recruit";
            }
            float hw = Mathf.Max(300, hintPlaceText.preferredWidth + 56);
            UIKit.Place(hintPlace.Rect, 800 - hw / 2, 30, hw, 40);

            // Stat chips share the card width evenly, like the mockup.
            const float left = 21, inner = 258, gap = 8;
            float sw = (inner - gap * (stats.Count - 1)) / stats.Count;
            for (int i = 0; i < cardStats.Count; i++)
            {
                var (p, v, l) = cardStats[i];
                p.Show(i < stats.Count);
                if (i >= stats.Count) continue;
                UIKit.Place(p.Rect, left + i * (sw + gap), 84, sw, 62);
                v.rectTransform.sizeDelta = new Vector2(sw, 32);
                l.rectTransform.sizeDelta = new Vector2(sw, 20);
                v.text = stats[i].v;
                l.text = stats[i].l;
                v.color = stats[i].lilac ? Palette.Lilac : Palette.Text;
                l.color = stats[i].lilac ? Palette.Lilac : Palette.Muted;
            }

            float y = 162;
            UIKit.Place(passiveLabel.rectTransform, left, y, 200, 18); y += 20;
            passiveName.text = pName;
            UIKit.Place(passiveName.rectTransform, left, y, inner, 26); y += 28;
            passiveText.text = pText;
            UIKit.Place(passiveText.rectTransform, left, y, inner, 20);
            float ph = Mathf.Max(20, passiveText.preferredHeight);
            passiveText.rectTransform.sizeDelta = new Vector2(inner, ph);
            y += ph + 14;

            bool hasActive = aName != null;
            activeLabel.gameObject.SetActive(hasActive);
            activeName.gameObject.SetActive(hasActive);
            activeText.gameObject.SetActive(hasActive);
            activeChip.Show(hasActive);
            if (hasActive)
            {
                UIKit.Place(activeLabel.rectTransform, left, y, 200, 18); y += 20;
                activeName.text = aName;
                UIKit.Place(activeName.rectTransform, left, y, 180, 26);
                activeChipText.text = aChip;
                UIKit.Place(activeChip.Rect, left + inner - 80, y + 1, 80, 24);
                y += 28;
                activeText.text = aText;
                UIKit.Place(activeText.rectTransform, left, y, inner, 20);
                float ah = Mathf.Max(20, activeText.preferredHeight);
                activeText.rectTransform.sizeDelta = new Vector2(inner, ah);
                y += ah + 14;
            }

            // Minions: choose one minion or a full squad of four.
            bool squadPick = !gm.PlaceIsBuilding && gm.PlaceUnit == UnitType.Minions;
            sizeLabel.gameObject.SetActive(squadPick);
            for (int i = 0; i < sizeBtns.Count; i++)
            {
                var (p, t, b, n) = sizeBtns[i];
                p.Show(squadPick);
                if (!squadPick) continue;
                bool sel = gm.MinionCount == n;
                bool enough = n <= side.Reserve;
                b.Interactable = enough;
                UIKit.Place(p.Rect, left + i * 133, y + 22, 125, 40);
                p.Colors(sel ? Palette.White : b.Hovered && enough ? Palette.RowHover : Palette.Row, sel ? Palette.White : Palette.RowStroke);
                t.color = sel ? Palette.Background : enough ? Palette.Text : Palette.Dim;
            }
            if (squadPick)
            {
                UIKit.Place(sizeLabel.rectTransform, left, y, 200, 18);
                y += 76;
            }

            costText.text = cost == 0 ? "Free" : $"Cost {cost}";
            UIKit.Place(costText.rectTransform, left, y, 130, 24);
            goldPreview.text = $"Gold {side.Gold} → {side.Gold - cost}";
            goldPreview.color = side.Gold >= cost ? Palette.Gold : Palette.Red;
            UIKit.Place(goldPreview.rectTransform, 120, y, 159, 24);
            y += 34;

            problemText.gameObject.SetActive(problem != null);
            if (problem != null)
            {
                problemText.text = problem;
                UIKit.Place(problemText.rectTransform, left, y - 6, inner, 20);
                y += 20;
            }

            UIKit.Place(confirmBtn.Rect, left, y, 147, 50);
            UIKit.Place(cancelBtn.Rect, 178, y, 101, 50);
            bool can = gm.CanConfirm;
            confirmB.Interactable = can;
            confirmBtn.Colors(can ? (confirmB.Hovered ? Palette.Text : Palette.White) : UIKit.A(Palette.White, 0.25f),
                can ? Palette.White : UIKit.A(Palette.White, 0.3f));
            confirmText.color = can ? Palette.Background : UIKit.A(Palette.White, 0.6f);
            cancelBtn.Colors(cancelBtn.Rect.GetComponent<UIButton>().Hovered ? Palette.RowHover : new Color(0, 0, 0, 0), Palette.RowStroke);
            y += 50 + 20;
            UIKit.Place(card.Rect, 350, 150, 300, y);

            float rowY = (gm.PlaceIsBuilding ? 230 : 150) + 56 + rowIndex * 65 + 30;
            cardArrow.anchoredPosition = new Vector2(350, -rowY);
        }

        void RefreshBottom()
        {
            var m = gm.Model;
            bool enemyTurn = m.Current == Faction.Enemy;
            int ready = gm.ReadyUnits().Count;
            readyChip.Show(!enemyTurn && gm.Mode != Mode.Over);
            readyDot.gameObject.SetActive(ready > 0);
            readyText.text = ready == 0 ? "All units have acted" : ready == 1 ? "1 unit can still act" : $"{ready} units can still act";
            readyText.color = ready > 0 ? Palette.Cyan : Palette.Muted;
            readyChip.Stroke.color = ready > 0 ? Palette.Cyan : Palette.RowStroke;
            readyText.rectTransform.anchoredPosition = new Vector2(ready > 0 ? 40 : 0, 0);
            readyText.alignment = ready > 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            readyText.rectTransform.sizeDelta = new Vector2(ready > 0 ? 165 : 210, 34);

            bool canEnd = gm.CanEndTurn;
            endB.Interactable = canEnd;
            endText.text = gm.Mode == Mode.Over ? "GAME OVER" : enemyTurn ? "ENEMY TURN" : $"END TURN · {m.Turn}";
            endTurn.Colors(canEnd ? (endB.Hovered ? Palette.Text : Palette.White) : new Color(0, 0, 0, 0),
                canEnd ? Palette.White : Palette.RowStroke);
            endText.color = canEnd ? Palette.Background : Palette.Muted;

            bool canNext = PlayerTurn && ready > 0;
            nextB.Interactable = canNext;
            nextBtn.Colors(canNext && nextB.Hovered ? UIKit.A(Palette.Cyan, 0.15f) : new Color(0, 0, 0, 0),
                canNext ? Palette.Cyan : Palette.RowStroke);
            nextText.color = canNext ? Palette.Cyan : Palette.Dim;

            bool canUndo = gm.CanUndo;
            undoB.Interactable = canUndo;
            undoBtn.Colors(canUndo && undoB.Hovered ? UIKit.A(Palette.Text, 0.12f) : new Color(0, 0, 0, 0),
                canUndo ? Palette.Text : Palette.RowStroke);
            undoText.color = canUndo ? Palette.Text : Palette.Dim;

            bool muted = gm.Music.Muted;
            muteText.color = muted ? Palette.Muted : Palette.Text;
            muteSlash.gameObject.SetActive(muted);
        }

        // ================================================================ overlay

        public void ShowMenu(bool show)
        {
            overlay.gameObject.SetActive(show);
            if (!show) return;
            overlayTitle.text = "Paused";
            overlaySub.text = "Left-click to select and act · Right-click or Esc to cancel\n" +
                              "WASD / arrows or right-drag to pan · Scroll to zoom\n" +
                              "Space ends the turn · Tab jumps to the next ready unit · 1–4 cast spells\n" +
                              "Q uses the selected squad's ability · M music";
            SetOverlayButtons(("Resume", gm.ToggleMenu), ("Restart", gm.NewGame), ("Main menu", gm.ShowMainMenu));
        }

        public void ShowEndTurnWarning(int ready)
        {
            overlay.gameObject.SetActive(true);
            overlayTitle.text = "End turn?";
            overlayTitle.color = Palette.Text;
            overlaySub.text = (ready == 1 ? "1 squad can" : $"{ready} squads can") + " still move or attack.\n" +
                              "Space confirms · Esc goes to the next ready squad";
            SetOverlayButtons(("End turn", () => gm.ConfirmEndTurn(false)),
                ("Go to ready squad", gm.CancelEndTurn),
                ("End turn, don't ask again", () => gm.ConfirmEndTurn(true)));
        }

        public void HideOverlay() => overlay.gameObject.SetActive(false);

        public void ShowGameOver(bool won)
        {
            overlay.gameObject.SetActive(true);
            overlayTitle.text = won ? "Victory" : "Defeat";
            overlayTitle.color = won ? Palette.Cyan : Palette.Red;
            var ch = gm.Chapter;
            if (ch != null)
            {
                bool last = gm.ChapterIndex + 1 >= Campaign.Chapters.Length;
                overlaySub.text = won ? ch.Outro : $"Your Keep fell on turn {gm.Model.Turn}. The Host will not wait; try again.";
                if (won)
                    SetOverlayButtons((last ? "Back to campaign" : "Next chapter", gm.NextChapter),
                        (last ? null : "Campaign", () => gm.ShowMainMenu(true)), (null, null));
                else
                    SetOverlayButtons(("Retry chapter", gm.NewGame), ("Campaign", () => gm.ShowMainMenu(true)), (null, null));
                return;
            }
            overlaySub.text = won
                ? $"You captured the enemy Keep on turn {gm.Model.Turn}."
                : $"Your Keep fell on turn {gm.Model.Turn}.";
            SetOverlayButtons(("Play again", gm.NewGame), ("Main menu", gm.ShowMainMenu), (null, null));
        }

        void SetOverlayButtons((string label, System.Action act) a, (string label, System.Action act) b, (string label, System.Action act) c)
        {
            overlayTitle.color = overlayTitle.text == "Paused" ? Palette.Text : overlayTitle.color;
            void Set(Panel p, Text t, UIButton btn, (string label, System.Action act) d)
            {
                p.Show(d.label != null);
                t.text = d.label;
                btn.OnClick = d.act;
            }
            Set(overlayPrimary, overlayPrimaryText, overlayPrimaryB, a);
            Set(overlaySecondary, overlaySecondaryText, overlaySecondaryB, b);
            Set(overlayTertiary, overlayTertiaryText, overlayTertiaryB, c);
            // The message can run long (campaign outros), so the buttons sit under however tall it is.
            float subH = Mathf.Max(76, overlaySub.preferredHeight + 12);
            overlaySub.rectTransform.sizeDelta = new Vector2(380, subH);
            float y = 74 + subH;
            int n = 0;
            foreach (var p in new[] { overlayPrimary, overlaySecondary, overlayTertiary })
                if (p.Rect.gameObject.activeSelf) UIKit.Place(p.Rect, 70, y + 56 * n++, 300, 46);
            var box = (RectTransform)overlayPrimary.Rect.parent;
            box.sizeDelta = new Vector2(440, y + 56 * n + 12);
        }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
