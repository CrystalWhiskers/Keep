using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Strategy
{
    /// <summary>
    /// Title screen over the drifting board. Left: Campaign / Skirmish / Quit. Right: the campaign's chapter list and
    /// briefing, or the skirmish setup (squad and spell loadout tabs plus match settings).
    /// Same 1600×900 reference canvas and visual language as the HUD.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        GameManager gm;
        GameSettings S => gm.Settings;

        enum View { Campaign, Skirmish }
        enum Tab { Squads, Spells }
        View view;
        Tab tab;
        int chapter;

        class Toggle
        {
            public Panel P;
            public UIButton B;
            public Text Name, Icon;
            public RectTransform Check;
            public System.Func<bool> On;
            public bool Fixed;
        }
        class Choice
        {
            public Panel P;
            public UIButton B;
            public Text T;
            public System.Func<bool> Selected;
        }

        readonly List<Choice> navs = new List<Choice>(), choices = new List<Choice>(), tabs = new List<Choice>(), chapterRows = new List<Choice>();
        readonly List<Toggle> squadCards = new List<Toggle>(), spellCards = new List<Toggle>();
        RectTransform campaignRoot, skirmishRoot, squadGrid, spellGrid;
        Panel startBtn, beginBtn, warnBtn;
        UIButton startB, beginB;
        Text startText, beginText, tabNote, noteText, mapText, enemyText, warnText;
        Text chTitle, chPlace, chIntro, chGoal, chForces, chStatus;
        readonly List<Text> chapterStatus = new List<Text>();
        string note;
        float noteUntil;

        public static MainMenu Create(GameManager gm, bool campaign)
        {
            var go = new GameObject("MainMenu", typeof(RectTransform));
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 20;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            var menu = go.AddComponent<MainMenu>();
            menu.gm = gm;
            menu.view = campaign ? View.Campaign : View.Skirmish;
            menu.chapter = Mathf.Min(Campaign.Cleared, Campaign.Chapters.Length - 1);
            menu.Build((RectTransform)go.transform);
            return menu;
        }

        // ================================================================ build

        void Build(RectTransform root)
        {
            UIKit.Img(UIKit.Fill(root, "Dim"), null, UIKit.A(Palette.Background, 0.5f), true);
            var g = UIKit.Fill(root, "Layout");
            g.anchorMin = g.anchorMax = g.pivot = new Vector2(0.5f, 0.5f);
            g.sizeDelta = new Vector2(1600, 900);

            // ---- left column
            UIKit.Label(g, "KEEP", 80, 170, 400, 100, 92, Palette.White, FontStyle.Bold);
            UIKit.Label(g, "Tactics in the floating realms of the Vale", 84, 266, 420, 26, 19, Palette.Muted);
            // A gilded rule with a crest at its centre, for the fantasy title card.
            UIKit.Img(UIKit.TL(g, "RuleL", 84, 304, 140, 2), null, UIKit.A(Palette.Gold, 0.55f));
            UIKit.Img(UIKit.TL(g, "RuleR", 262, 304, 140, 2), null, UIKit.A(Palette.Gold, 0.55f));
            UIKit.Label(g, "♜", 226, 290, 34, 30, 20, Palette.Gold, FontStyle.Normal, TextAnchor.MiddleCenter, true);

            AddChoice(navs, g, "Campaign", 80, 330, 340, 58, () => view == View.Campaign, () => view = View.Campaign, 20);
            AddChoice(navs, g, "Skirmish", 80, 400, 340, 58, () => view == View.Skirmish, () => view = View.Skirmish, 20);

            var quit = UIKit.Panel(g, "Quit", 80, 470, 340, 50, new Color(0, 0, 0, 0), Palette.RowStroke, 10);
            var qb = UIKit.Button(quit);
            qb.OnClick = Hud.Quit;
            qb.OnHover = h => quit.Colors(h ? Palette.RowHover : new Color(0, 0, 0, 0), Palette.RowStroke);
            UIKit.Label(UIKit.Fill(quit.Rect, "T"), "Quit", 18, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter);

            // Ready-squad warning: same setting the in-game "don't ask again" turns off.
            warnBtn = UIKit.Panel(g, "Warn", 80, 556, 340, 44, Palette.Row, Palette.RowStroke, 8);
            var wb = UIKit.Button(warnBtn);
            wb.OnClick = () => { S.WarnEndTurn = !S.WarnEndTurn; S.Save(); Refresh(); };
            wb.OnHover = _ => Refresh();
            warnText = UIKit.Label(warnBtn.Rect, "", 16, 0, 310, 44, 15, Palette.Text);
            UIKit.Label(g, "M  music on / off", 82, 618, 340, 22, 14, Palette.Dim);

            // ---- right panel
            var panel = UIKit.Panel(g, "Right", 520, 70, 1020, 760, UIKit.A(Palette.Panel, 0.97f), Palette.PanelStroke, 14);
            campaignRoot = UIKit.Fill(panel.Rect, "Campaign");
            skirmishRoot = UIKit.Fill(panel.Rect, "Skirmish");
            BuildCampaign(campaignRoot);
            BuildSkirmish(skirmishRoot);
            Refresh();
        }

        void BuildCampaign(RectTransform r)
        {
            UIKit.Label(r, "CAMPAIGN", 32, 24, 300, 24, 15, Palette.Muted, FontStyle.Bold);
            UIKit.Label(r, Campaign.Name, 32, 48, 400, 34, 26, Palette.Text, FontStyle.Bold);

            for (int i = 0; i < Campaign.Chapters.Length; i++)
            {
                var ch = Campaign.Chapters[i];
                int index = i;
                var c = new Choice { Selected = () => chapter == index };
                c.P = UIKit.Panel(r, ch.Title, 32, 100 + i * 92, 300, 80, Palette.Row, Palette.RowStroke, 10);
                c.B = UIKit.Button(c.P);
                c.B.OnClick = () => { if (Campaign.Unlocked(index)) { chapter = index; Refresh(); } };
                c.B.OnHover = _ => Refresh();
                UIKit.Label(c.P.Rect, ch.Numeral, 16, 0, 44, 80, 24, Palette.Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
                c.T = UIKit.Label(c.P.Rect, ch.Title, 66, 14, 225, 26, 18, Palette.Text, FontStyle.Bold);
                chapterStatus.Add(UIKit.Label(c.P.Rect, "", 66, 42, 225, 22, 14, Palette.Muted));
                chapterRows.Add(c);
            }

            // Briefing for the selected chapter.
            chPlace = UIKit.Label(r, "", 364, 100, 620, 22, 14, Palette.Muted, FontStyle.Bold);
            chTitle = UIKit.Label(r, "", 364, 122, 620, 40, 32, Palette.Text, FontStyle.Bold);
            chIntro = UIKit.Label(r, "", 364, 176, 620, 240, 17, Palette.Text, FontStyle.Normal, TextAnchor.UpperLeft);
            chGoal = UIKit.Label(r, "", 364, 470, 620, 24, 17, Palette.White, FontStyle.Bold);
            chForces = UIKit.Label(r, "", 364, 500, 620, 160, 15, Palette.Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            chStatus = UIKit.Label(r, "", 32, 690, 400, 24, 14, Palette.Muted);

            beginBtn = UIKit.Panel(r, "Begin", 768, 676, 220, 54, Palette.White, Palette.White, 10);
            beginB = UIKit.Button(beginBtn);
            beginB.OnClick = () => { if (Campaign.Unlocked(chapter)) gm.StartChapter(chapter); };
            beginB.OnHover = _ => Refresh();
            beginText = UIKit.Label(UIKit.Fill(beginBtn.Rect, "T"), "BEGIN", 20, Palette.Background, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        void BuildSkirmish(RectTransform r)
        {
            UIKit.Label(r, "SKIRMISH · LOADOUT", 32, 24, 400, 24, 15, Palette.Muted, FontStyle.Bold);
            AddChoice(tabs, r, "Squads", 32, 52, 170, 42, () => tab == Tab.Squads, () => tab = Tab.Squads, 16);
            AddChoice(tabs, r, "Spells", 210, 52, 170, 42, () => tab == Tab.Spells, () => tab = Tab.Spells, 16);
            tabNote = UIKit.Label(r, "", 400, 52, 590, 42, 15, Palette.Muted, FontStyle.Normal, TextAnchor.MiddleRight);

            squadGrid = UIKit.Fill(r, "Squads");
            var squads = new List<UnitType>(GameSettings.Choices) { UnitType.Minions };
            for (int i = 0; i < squads.Count; i++)
                squadCards.Add(SquadCard(squadGrid, squads[i], 32 + (i % 5) * 192, 108 + (i / 5) * 164));

            spellGrid = UIKit.Fill(r, "Spells");
            for (int i = 0; i < Defs.AllSpells.Length; i++)
                spellCards.Add(SpellCard(spellGrid, Defs.AllSpells[i], 32 + (i % 4) * 243, 108 + (i / 4) * 246));

            UIKit.Label(r, "MAP", 32, 612, 200, 20, 13, Palette.Muted, FontStyle.Bold);
            string[] maps = { "Small", "Medium", "Large" };
            for (int i = 0; i < 3; i++)
            {
                var m = (MapSize)i;
                AddChoice(choices, r, maps[i], 32 + i * 124, 638, 116, 44, () => S.Map == m, () => S.Map = m, 16);
            }
            mapText = UIKit.Label(r, "", 32, 692, 380, 22, 14, Palette.Muted);

            UIKit.Label(r, "ENEMY", 420, 612, 200, 20, 13, Palette.Muted, FontStyle.Bold);
            string[] levels = { "Easy", "Normal", "Hard" };
            for (int i = 0; i < 3; i++)
            {
                var d = (Difficulty)i;
                AddChoice(choices, r, levels[i], 420 + i * 112, 638, 104, 44, () => S.Difficulty == d, () => S.Difficulty = d, 16);
            }
            enemyText = UIKit.Label(r, "", 420, 692, 340, 40, 13, Palette.Muted, FontStyle.Normal, TextAnchor.UpperLeft);

            startBtn = UIKit.Panel(r, "Start", 788, 632, 200, 56, Palette.White, Palette.White, 10);
            startB = UIKit.Button(startBtn);
            startB.OnClick = () => { if (S.Ready) gm.StartGame(); };
            startB.OnHover = _ => Refresh();
            startText = UIKit.Label(UIKit.Fill(startBtn.Rect, "T"), "START", 20, Palette.Background, FontStyle.Bold, TextAnchor.MiddleCenter);
            noteText = UIKit.Label(r, "", 600, 700, 388, 40, 13, Palette.Red, FontStyle.Normal, TextAnchor.UpperRight);
        }

        Toggle SquadCard(RectTransform parent, UnitType type, float x, float y)
        {
            var def = Defs.Unit(type);
            bool always = type == UnitType.Minions;
            var c = new Toggle { On = () => always || S.Loadout.Contains(type), Fixed = always };
            const float W = 182;
            c.P = UIKit.Panel(parent, def.Name, x, y, W, 152, Palette.Row, Palette.RowStroke, 10);
            var r = c.P.Rect;
            if (!always)
            {
                c.B = UIKit.Button(c.P);
                c.B.OnClick = () => Pick(S.Toggle(type), $"Your squads are full ({GameSettings.MaxSquads}). Remove one first.");
                c.B.OnHover = _ => Refresh();
            }
            var box = UIKit.Panel(r, "Icon", 12, 14, 34, 34, Palette.IconBox, Palette.RowStroke, 7, false);
            c.Icon = UIKit.Label(UIKit.Fill(box.Rect, "Glyph"), def.Icon, 18, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter, true);
            c.Name = UIKit.Label(r, def.Name, 54, 10, 96, 26, 16, Palette.Text, FontStyle.Bold);
            UIKit.Label(r, def.Role, 54, 34, 120, 18, 11, Palette.Muted);
            if (always)
            {
                var pill = UIKit.Panel(r, "Free", W - 58, 14, 46, 22, new Color(0, 0, 0, 0), Palette.Cyan, 12, false);
                UIKit.Label(UIKit.Fill(pill.Rect, "T"), "FREE", 11, Palette.Cyan, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            else
            {
                var ring = UIKit.TL(r, "Cost", W - 38, 12, 28, 28);
                UIKit.Img(ring, UIKit.Circle(28, 2f), Palette.Gold);
                UIKit.Label(UIKit.Fill(ring, "V"), def.Cost.ToString(), 14, Palette.Gold, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            string Stat(string v, string l) => $"{v}<color=#8b979d><size=10> {l}</size></color>";
            var stats = always
                ? new[] { Stat("×N", "AD"), Stat("×N", "HP"), Stat("0", "DEF"), Stat(def.Move.ToString(), "MOVE") }
                : new[] { Stat(def.Attack.ToString(), "AD"), Stat(def.Health.ToString(), "HP"), Stat(def.Defense.ToString(), "DEF"), Stat(def.Move.ToString(), "MOVE") };
            var line = string.Join("  ", stats) + (def.Range > 1 ? "  " + Stat(def.Range.ToString(), "RNG") : "");
            UIKit.Label(r, line, 12, 62, W - 18, 22, 13, Palette.Text, FontStyle.Bold);
            var ability = Defs.Ability(def.Ability);
            UIKit.Label(r, def.PassiveName + (ability != null ? " · " + ability.Name : ""), 12, 88, W - 20, 20, 13, Palette.Text, FontStyle.Bold);
            UIKit.Label(r, def.PassiveText, 12, 108, W - 20, 40, 11, Palette.Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            c.Check = CheckBadge(r);
            return c;
        }

        Toggle SpellCard(RectTransform parent, SpellType type, float x, float y)
        {
            var def = Defs.Spell(type);
            var c = new Toggle { On = () => S.Spells.Contains(type) };
            c.P = UIKit.Panel(parent, def.Name, x, y, 227, 234, Palette.Row, Palette.RowStroke, 10);
            var r = c.P.Rect;
            c.B = UIKit.Button(c.P);
            c.B.OnClick = () => Pick(S.Toggle(type), $"You already have {Defs.SpellSlots} spells. Remove one first.");
            c.B.OnHover = _ => Refresh();
            var box = UIKit.Panel(r, "Icon", 16, 16, 56, 56, Palette.IconBox, Palette.RowStroke, 8, false);
            c.Icon = UIKit.Label(UIKit.Fill(box.Rect, "Glyph"), def.Icon, 28, Palette.Text, FontStyle.Normal, TextAnchor.MiddleCenter, true);
            c.Name = UIKit.Label(r, def.Name, 84, 16, 130, 30, 21, Palette.Text, FontStyle.Bold);
            var chip = UIKit.Panel(r, "Mana", 84, 48, 78, 24, UIKit.A(Palette.Lilac, 0.15f), UIKit.A(Palette.Lilac, 0.5f), 6, false);
            UIKit.Label(UIKit.Fill(chip.Rect, "T"), $"{def.Cost} MANA", 12, Palette.Lilac, FontStyle.Bold, TextAnchor.MiddleCenter);
            string desc = char.ToUpperInvariant(def.Desc[0]) + def.Desc.Substring(1) + ".";
            UIKit.Label(r, desc, 16, 90, 196, 100, 15, Palette.Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            if (def.Instant) UIKit.Label(r, "Instant: no target", 16, 196, 196, 20, 13, Palette.Lilac);
            c.Check = CheckBadge(r);
            return c;
        }

        static RectTransform CheckBadge(RectTransform card)
        {
            var check = UIKit.TL(card, "Check", 0, 0, 26, 26);
            check.anchorMin = check.anchorMax = new Vector2(1, 1);
            check.pivot = new Vector2(0.5f, 0.5f);
            check.anchoredPosition = new Vector2(-4, -4);
            UIKit.Img(check, UIKit.Circle(26), Palette.White);
            UIKit.Label(UIKit.Fill(check, "T"), "✓", 16, Palette.Background, FontStyle.Bold, TextAnchor.MiddleCenter, true);
            return check;
        }

        void AddChoice(List<Choice> list, RectTransform parent, string label, float x, float y, float w, float h,
            System.Func<bool> selected, System.Action pick, int size)
        {
            var c = new Choice { Selected = selected };
            c.P = UIKit.Panel(parent, label, x, y, w, h, Palette.Row, Palette.RowStroke, 8);
            c.B = UIKit.Button(c.P);
            c.B.OnClick = () => { pick(); Refresh(); };
            c.B.OnHover = _ => Refresh();
            c.T = UIKit.Label(UIKit.Fill(c.P.Rect, "T"), label, size, Palette.Text, FontStyle.Bold, TextAnchor.MiddleCenter);
            list.Add(c);
        }

        void Pick(bool ok, string fullMessage)
        {
            if (!ok)
            {
                note = fullMessage;
                noteUntil = Time.unscaledTime + 2.5f;
            }
            Refresh();
        }

        void Update()
        {
            if (note != null && Time.unscaledTime > noteUntil)
            {
                note = null;
                Refresh();
            }
            if (GInput.KeyDown(GKey.Enter))
            {
                if (view == View.Campaign && Campaign.Unlocked(chapter)) gm.StartChapter(chapter);
                else if (view == View.Skirmish && S.Ready) gm.StartGame();
            }
        }

        // ================================================================ refresh

        void Refresh()
        {
            foreach (var c in navs) StyleChoice(c);
            warnText.text = $"End-turn warning (squads still ready): <b>{(S.WarnEndTurn ? "On" : "Off")}</b>";
            warnBtn.Colors(warnBtn.Rect.GetComponent<UIButton>().Hovered ? Palette.RowHover : Palette.Row, Palette.RowStroke);

            campaignRoot.gameObject.SetActive(view == View.Campaign);
            skirmishRoot.gameObject.SetActive(view == View.Skirmish);
            if (view == View.Campaign) RefreshCampaign();
            else RefreshSkirmish();
        }

        void RefreshCampaign()
        {
            int cleared = Campaign.Cleared;
            for (int i = 0; i < chapterRows.Count; i++)
            {
                var c = chapterRows[i];
                bool open = Campaign.Unlocked(i), sel = chapter == i, done = i < cleared;
                c.B.Interactable = open;
                c.P.Colors(sel ? Palette.RowHover : c.B.Hovered && open ? Palette.RowHover : Palette.Row, sel ? Palette.White : Palette.RowStroke);
                c.T.color = open ? Palette.Text : Palette.Dim;
                chapterStatus[i].text = done ? "✓ Won" : open ? "Ready" : "Locked";
                chapterStatus[i].color = done ? Palette.Cyan : open ? Palette.White : Palette.Dim;
            }

            var ch = Campaign.Chapters[chapter];
            chPlace.text = $"CHAPTER {ch.Numeral} · {ch.Place.ToUpperInvariant()}";
            chTitle.text = ch.Title;
            chIntro.text = ch.Intro;
            chGoal.text = "Objective: " + ch.ObjectiveText;
            string Names<T>(IEnumerable<T> items, System.Func<T, string> name) => string.Join(", ", items.Select(name));
            chForces.text =
                $"<color=#46b8f2>Your squads</color>   {Names(ch.Roster, t => Defs.Unit(t).Name)}\n" +
                (ch.PlayerStart.Length > 0 ? $"<color=#46b8f2>Already in the field</color>   {Names(ch.PlayerStart, t => Defs.Unit(t).Name)}\n" : "") +
                $"<color=#a993f5>Spells</color>   {Names(ch.Spells, s => Defs.Spell(s).Name)}\n" +
                $"<color=#e5484d>The Host fields</color>   {(ch.EnemyUnits == null ? "everything it has" : Names(ch.EnemyUnits, t => Defs.Unit(t).Name))}\n" +
                (ch.NewTechs.Length > 0 ? $"<color=#5fd3a2>New research</color>   {Names(ch.NewTechs, t => Defs.Tech(t).Name)}\n" : "") +
                $"Map {ch.Size} × {ch.Size}{(ch.Map.River ? " · a river with fords" : "")}";
            chStatus.text = cleared >= Campaign.Chapters.Length ? "Campaign complete. Replay any chapter." : $"{cleared} of {Campaign.Chapters.Length} chapters won";

            bool can = Campaign.Unlocked(chapter);
            beginB.Interactable = can;
            beginText.text = chapter < cleared ? "REPLAY" : "BEGIN";
            beginBtn.Colors(can ? (beginB.Hovered ? Palette.Text : Palette.White) : new Color(0, 0, 0, 0), can ? Palette.White : Palette.RowStroke);
            beginText.color = can ? Palette.Background : Palette.Dim;
        }

        void RefreshSkirmish()
        {
            foreach (var c in tabs) StyleChoice(c);
            squadGrid.gameObject.SetActive(tab == Tab.Squads);
            spellGrid.gameObject.SetActive(tab == Tab.Spells);
            tabs[0].T.text = $"Squads  {S.Loadout.Count}/{GameSettings.MaxSquads}";
            tabs[1].T.text = $"Spells  {S.Spells.Count}/{Defs.SpellSlots}";
            tabNote.text = tab == Tab.Squads
                ? $"Pick up to {GameSettings.MaxSquads} squads to recruit. Minions always come."
                : $"Pick {Defs.SpellSlots} spells for the mana bar (keys 1–4, in the order picked).";

            StyleToggles(squadCards, S.Loadout.Count >= GameSettings.MaxSquads);
            StyleToggles(spellCards, S.Spells.Count >= Defs.SpellSlots);
            foreach (var c in choices) StyleChoice(c);
            mapText.text = $"{S.MapTiles} × {S.MapTiles} tiles: hills, woods, water and sites to claim";
            enemyText.text = S.Difficulty == Difficulty.Easy ? "Enemy: −6 starting gold, −1 gold per turn"
                : S.Difficulty == Difficulty.Hard ? "Enemy: +6 starting gold, +1 gold per turn"
                : "Both sides start even";

            bool ready = S.Ready;
            startB.Interactable = ready;
            startBtn.Colors(ready ? (startB.Hovered ? Palette.Text : Palette.White) : new Color(0, 0, 0, 0), ready ? Palette.White : Palette.RowStroke);
            startText.color = ready ? Palette.Background : Palette.Dim;
            noteText.text = note ?? (S.Loadout.Count == 0 ? "Pick at least one squad."
                : S.Spells.Count != Defs.SpellSlots ? $"Pick {Defs.SpellSlots} spells ({S.Spells.Count} chosen)." : "");
        }

        void StyleToggles(List<Toggle> cards, bool full)
        {
            foreach (var c in cards)
            {
                bool on = c.On();
                bool hover = c.B != null && c.B.Hovered;
                bool dim = !on && full;
                c.P.Colors(on || hover ? Palette.RowHover : Palette.Row, on && !c.Fixed ? Palette.White : hover ? Palette.Muted : Palette.RowStroke);
                c.Name.color = dim ? Palette.Muted : Palette.Text;
                c.Icon.color = dim ? Palette.Muted : Palette.Text;
                c.Check.gameObject.SetActive(on && !c.Fixed);
            }
        }

        static void StyleChoice(Choice c)
        {
            bool sel = c.Selected();
            c.P.Colors(sel ? Palette.White : c.B.Hovered ? Palette.RowHover : Palette.Row, sel ? Palette.White : Palette.RowStroke);
            c.T.color = sel ? Palette.Background : Palette.Text;
        }
    }
}
