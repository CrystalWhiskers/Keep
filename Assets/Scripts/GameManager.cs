using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Strategy
{
    public enum Mode { Idle, Unit, Building, Place, Spell, Ability, Busy, Over }

    /// <summary>
    /// Owns the game: builds the world and HUD, turns clicks into orders, animates results, and runs turns.
    /// Drop it on an empty GameObject in any scene and press Play.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Tooltip("0 = a new random map every game")]
        public int Seed = 0;

        public GameSettings Settings { get; private set; }
        public bool InMainMenu => mainMenu != null;

        /// <summary>The campaign battle being played, or null for a skirmish.</summary>
        public Chapter Chapter { get; private set; }
        public int ChapterIndex { get; private set; } = -1;
        /// <summary>Recruitable squads and mana-bar spells for this battle: the chapter's, or the skirmish loadout.</summary>
        public UnitType[] Roster => Chapter != null ? Chapter.Roster : Settings.Roster;
        public IList<SpellType> Spells => Chapter != null ? (IList<SpellType>)Chapter.Spells : Settings.Spells;
        public GameModel Model { get; private set; }
        public MapView Map { get; private set; }
        public CameraRig Rig { get; private set; }
        public Hud Hud { get; private set; }
        public Music Music { get; private set; }
        public Mode Mode { get; private set; }
        public Unit SelUnit { get; private set; }
        public Building SelBuilding { get; private set; }
        public Entity Selected => (Entity)SelUnit ?? SelBuilding;

        public bool PlaceIsBuilding { get; private set; }
        public UnitType PlaceUnit { get; private set; }
        public BuildingType PlaceBuilding { get; private set; }
        public Vector2Int? PlaceChoice { get; private set; }
        public SpellType ActiveSpell { get; private set; }
        public bool RecruitOpen { get; private set; } = true;
        public bool MenuOpen { get; private set; }
        public string LastOrder { get; private set; }
        public string Flash { get; private set; }
        public string HoverHint;

        /// <summary>The attack the pointer is over, if any: who would hit whom, and how it would go.</summary>
        public Unit ForecastAttacker { get; private set; }
        public Entity ForecastTarget { get; private set; }
        public Forecast Forecast { get; private set; }
        /// <summary>Player squads a visible enemy could strike next turn.</summary>
        readonly HashSet<Unit> danger = new HashSet<Unit>();
        public bool InDanger(Unit u) => danger.Contains(u);
        MainMenu mainMenu;

        /// <summary>True while the "squads can still act" warning is up.</summary>
        public bool ConfirmOpen { get; private set; }

        class MoveRecord
        {
            public Unit U;
            public Vector2Int From, Dest;
            public bool WasMoved;
            public int Gold;
            /// <summary>Who held the point of interest on the destination before this move claimed it.</summary>
            public Faction? Owner;
        }
        readonly Stack<MoveRecord> undo = new Stack<MoveRecord>();

        readonly Dictionary<Entity, EntityView> views = new Dictionary<Entity, EntityView>();
        readonly List<PickupView> pickups = new List<PickupView>();
        readonly HashSet<Vector2Int> moveTiles = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> placeTiles = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> spellTiles = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> abilityTiles = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, Entity> attackTiles = new Dictionary<Vector2Int, Entity>();
        readonly Dictionary<Entity, Vector2Int> attackFrom = new Dictionary<Entity, Vector2Int>();
        readonly Dictionary<EntityView, float> flashes = new Dictionary<EntityView, float>();
        Dictionary<Vector2Int, Vector2Int> reach;
        Transform world;
        EntityView ghost;
        Mode modeBeforeSpell;
        Vector2Int? hoverTile;
        float flashUntil;
        EnemyAI ai;

        public IEnumerable<EntityView> Views => views.Values;

        // ================================================================ setup

        void Start()
        {
            EnsureEventSystem();
            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            Rig = cam.GetComponent<CameraRig>();
            if (Rig == null) Rig = cam.gameObject.AddComponent<CameraRig>();
            Music = gameObject.AddComponent<Music>();
            Backdrop.Attach(cam);
            Settings = GameSettings.Load();
            ShowMainMenu(true);
        }

        /// <summary>Title screen over a live board that drifts in the background.</summary>
        public void ShowMainMenu() => ShowMainMenu(Chapter != null);

        public void ShowMainMenu(bool campaignView)
        {
            Chapter = null;
            ChapterIndex = -1;
            NewGame();
            Hud.gameObject.SetActive(false);
            Rig.InputEnabled = false;
            if (mainMenu != null) Destroy(mainMenu.gameObject);
            mainMenu = MainMenu.Create(this, campaignView);
        }

        /// <summary>Skirmish with the saved loadout and match settings.</summary>
        public void StartGame()
        {
            Settings.Save();
            Chapter = null;
            ChapterIndex = -1;
            CloseMainMenu();
            NewGame();
        }

        public void StartChapter(int index)
        {
            if (index < 0 || index >= Campaign.Chapters.Length) return;
            Chapter = Campaign.Chapters[index];
            ChapterIndex = index;
            CloseMainMenu();
            NewGame();
        }

        public void NextChapter()
        {
            if (ChapterIndex + 1 < Campaign.Chapters.Length) StartChapter(ChapterIndex + 1);
            else ShowMainMenu(true);
        }

        void CloseMainMenu()
        {
            if (mainMenu != null) Destroy(mainMenu.gameObject);
            mainMenu = null;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        public void NewGame()
        {
            StopAllCoroutines();
            foreach (var v in views.Values) v.Destroy();
            views.Clear();
            pickups.Clear();
            flashes.Clear();
            ghost = null;
            if (world != null) Destroy(world.gameObject);
            if (Hud != null) Destroy(Hud.gameObject);

            int seed = Chapter != null ? Chapter.Seed : Seed != 0 ? Seed : Random.Range(1, 1000000);
            int size = Chapter != null ? Chapter.Size : Settings.MapTiles;
            Model = new GameModel(size, size, seed, Chapter?.Map);
            if (Chapter != null) Model.TechPool = Chapter.TechPool;
            var enemy = Model.Of(Faction.Enemy);
            var (bonusGold, bonusIncome) = Chapter != null ? (Chapter.EnemyGold, Chapter.EnemyIncome) : Settings.EnemyBonus;
            enemy.Gold = Mathf.Max(0, enemy.Gold + bonusGold);
            enemy.IncomeBonus = bonusIncome;
            if (Chapter != null)
            {
                SpawnStart(Faction.Player, Chapter.PlayerStart);
                SpawnStart(Faction.Enemy, Chapter.EnemyStart);
            }
            ai = new EnemyAI(Model, Chapter?.EnemyUnits);
            world = new GameObject("World").transform;
            Map = new MapView(Model, world);
            foreach (var t in Model.AllTiles())
                if (t.Gold > 0) pickups.Add(new PickupView(t, world));

            var keep = Model.Of(Faction.Player).Keep.Pos;
            var start = new Vector3(keep.x + 1.5f, 0, keep.y + 1.5f);
            Rig.Init(Vector2.zero, new Vector2(size - 1, size - 1), start);
            Rig.JumpTo(start, true);
            Hud = Hud.Create(this);

            Mode = Mode.Idle;
            SelUnit = null;
            SelBuilding = null;
            RecruitOpen = true;
            MenuOpen = false;
            ConfirmOpen = false;
            undo.Clear();
            LastOrder = null;
            Flash = null;
            HoverHint = null;
            ClearForecast();
            Rig.InputEnabled = true;
            SyncViews();
            Refresh();
            Music.PlayGameplay();
            ShowBeats();
        }

        /// <summary>Chapter's extra starting squads, placed beside the Keep on the side facing the enemy.</summary>
        void SpawnStart(Faction f, UnitType[] types)
        {
            var foe = Model.Of(GameModel.Other(f)).Keep.Pos;
            foreach (var t in types)
            {
                var tiles = Model.RecruitTiles(f);
                if (tiles.Count == 0) break;
                var p = tiles.OrderBy(d => GameModel.Dist(d, foe)).First();
                Model.Spawn(t, f, p, t == UnitType.Minions ? Defs.MinionSquad : 0);
            }
            Model.UpdateFog();
        }

        /// <summary>Story lines for the current turn, shown as the player's turn begins.</summary>
        void ShowBeats()
        {
            if (Chapter == null || InMainMenu || Hud == null) return;
            foreach (var b in Chapter.Beats)
                if (b.Turn == Model.Turn) Hud.ShowDialogue(b.Speaker, b.Text);
        }

        // ================================================================ per-frame

        void Update()
        {
            if (Model == null) return;
            Map.Tick();
            foreach (var v in views.Values) v.Tick();
            if (ghost != null) ghost.Tick();
            foreach (var p in pickups) p.Sync(Rig.Cam);
            UpdateViewStates();

            if (Flash != null && Time.time > flashUntil)
            {
                Flash = null;
                Refresh();
            }

            if (GInput.KeyDown(GKey.M)) ToggleMusic();
            if (InMainMenu)
            {
                // Slow orbit around the middle of the board behind the title screen.
                float a = Time.unscaledTime * 0.08f, r = Model.W * 0.22f;
                Rig.JumpTo(new Vector3((Model.W - 1) / 2f + Mathf.Cos(a) * r, 0, (Model.H - 1) / 2f + Mathf.Sin(a) * r));
                return;
            }
            Rig.InputEnabled = !MenuOpen && !ConfirmOpen;
            if (MenuOpen)
            {
                if (GInput.KeyDown(GKey.Escape)) ToggleMenu();
                return;
            }
            if (ConfirmOpen)
            {
                if (GInput.KeyDown(GKey.Escape)) CancelEndTurn();
                else if (GInput.KeyDown(GKey.Space) || GInput.KeyDown(GKey.Enter)) ConfirmEndTurn(false);
                return;
            }
            if (Mode == Mode.Over || Mode == Mode.Busy) return;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            UpdateHover(overUi ? null : PickTile(GInput.MousePosition));

            if (GInput.KeyDown(GKey.Escape))
            {
                if (Mode == Mode.Idle) ToggleMenu();
                else Cancel();
                return;
            }
            if (GInput.KeyDown(GKey.Space) || GInput.KeyDown(GKey.Enter)) EndTurn();
            if (GInput.KeyDown(GKey.Tab)) NextUnit();
            if (GInput.KeyDown(GKey.Z) || GInput.KeyDown(GKey.Backspace)) Undo();
            if (GInput.KeyDown(GKey.Q)) OnAbilityClick();
            var keys = new[] { GKey.Alpha1, GKey.Alpha2, GKey.Alpha3, GKey.Alpha4 };
            for (int i = 0; i < keys.Length && i < Spells.Count; i++)
                if (GInput.KeyDown(keys[i])) OnSpellClick(Spells[i]);

            if (GInput.MouseDown(0) && !overUi)
            {
                var p = PickTile(GInput.MousePosition);
                if (p.HasValue) ClickTile(p.Value);
                else if (Mode == Mode.Unit || Mode == Mode.Building) Deselect();
            }
            if (Rig.RightClicked) Cancel();
        }

        void UpdateViewStates()
        {
            foreach (var kv in views)
            {
                var e = kv.Key;
                var v = kv.Value;
                if (e.Dead) continue;
                var tile = Model.T(e.Pos);
                bool visible = e.Faction == Faction.Player || (e is Unit ? tile.Visible : tile.Explored);
                v.SetVisible(visible);

                Color c = Palette.Of(e.Faction);
                if (e is Unit u)
                {
                    if (u.Faction == Faction.Player && Model.Current == Faction.Player && !Model.CanAct(u)) c = Palette.Grey;
                    if (v is UnitView uv) uv.SyncCount(u.Hp);
                }
                else if (e is Building b)
                {
                    if (!b.Complete) c = Color.Lerp(c, Palette.Explored, 0.55f);
                    else if (!tile.Visible) c = Color.Lerp(c, Palette.Explored, 0.4f);
                }
                if (flashes.TryGetValue(v, out var until) && Time.time < until) c = Color.white;
                v.SetTint(c);
            }
        }

        void UpdateHover(Vector2Int? tile)
        {
            if (tile == hoverTile) return;
            hoverTile = tile;
            UpdateForecast(tile);
            UpdateThreat(tile);
            bool show = tile.HasValue && (
                (Mode == Mode.Unit && (moveTiles.Contains(tile.Value) || attackTiles.ContainsKey(tile.Value))) ||
                (Mode == Mode.Place && placeTiles.Contains(tile.Value)) ||
                (Mode == Mode.Spell && spellTiles.Contains(tile.Value)) ||
                (Mode == Mode.Ability && abilityTiles.Contains(tile.Value)));
            TileHint = tile.HasValue ? DescribeTile(tile.Value) : null;
            if (!show)
            {
                Map.Hover.Clear();
                return;
            }
            var set = new HashSet<Vector2Int> { tile.Value };
            bool blast = (Mode == Mode.Spell && ActiveSpell == SpellType.Meteor) ||
                         (Mode == Mode.Ability && SelUnit != null && Model.AbilityOf(SelUnit)?.Type == AbilityType.Firestorm);
            if (blast)
                foreach (var d in GameModel.Dirs8)
                    if (Model.In(tile.Value + d)) set.Add(tile.Value + d);
            // Move-then-attack: also outline the tile the squad would attack from.
            bool split = ForecastTarget != null && attackFrom.TryGetValue(ForecastTarget, out var from) && set.Add(from);
            Map.Hover.Set(set, UIKit.A(Palette.White, 0.9f), split);
        }

        void UpdateForecast(Vector2Int? tile)
        {
            ClearForecast();
            if (Mode == Mode.Unit && SelUnit != null && tile.HasValue && attackTiles.TryGetValue(tile.Value, out var target))
            {
                var from = attackFrom.TryGetValue(target, out var f) ? f : SelUnit.Pos;
                ForecastAttacker = SelUnit;
                ForecastTarget = target;
                Forecast = Model.Preview(SelUnit, target, from);
            }
            Hud.Refresh();
        }

        void ClearForecast()
        {
            ForecastAttacker = null;
            ForecastTarget = null;
            Forecast = null;
        }

        /// <summary>Red outline of everywhere an enemy squad could hit next turn: the hovered one, else the selected one.</summary>
        void UpdateThreat(Vector2Int? tile)
        {
            Unit enemy = null;
            if (tile.HasValue && Mode != Mode.Place && Mode != Mode.Spell && Mode != Mode.Ability && ForecastTarget == null &&
                Model.T(tile.Value).Unit is Unit hovered && hovered.Faction == Faction.Enemy && Model.T(tile.Value).Visible)
                enemy = hovered;
            if (enemy == null && Mode == Mode.Unit && SelUnit != null && SelUnit.Faction == Faction.Enemy && !SelUnit.Dead)
                enemy = SelUnit;
            if (enemy != null) Map.Threat.Set(Model.ThreatOf(enemy), UIKit.A(Palette.Red, 0.75f));
            else Map.Threat.Clear();
        }

        void ComputeDanger()
        {
            danger.Clear();
            if (Model.Current != Faction.Player || Model.Winner != null) return;
            var threat = new HashSet<Vector2Int>();
            foreach (var e in Model.Units)
                if (e.Faction == Faction.Enemy && !e.Dead && Model.T(e.Pos).Visible)
                    threat.UnionWith(Model.ThreatOf(e));
            foreach (var u in Model.Units)
                if (u.Faction == Faction.Player && !u.Dead && threat.Contains(u.Pos))
                    danger.Add(u);
        }

        Vector2Int? PickTile(Vector2 screen)
        {
            // Squads stand up off the ground, so test them by screen distance first.
            if (Mode != Mode.Place)
            {
                float best = 24f * (Hud != null ? Hud.Scale : 1f);
                Vector2Int? hit = null;
                foreach (var v in views.Values)
                {
                    if (!(v.Entity is Unit) || v.Entity.Dead || !v.Root.gameObject.activeSelf) continue;
                    var sp = Rig.Cam.WorldToScreenPoint(v.Root.position + Vector3.up * 0.3f);
                    float d = Vector2.Distance(sp, screen);
                    if (d < best)
                    {
                        best = d;
                        hit = v.Entity.Pos;
                    }
                }
                if (hit.HasValue) return hit;
            }
            // Raised tiles: test each height level from the top down and take the first tile that is actually that high.
            var ray = Rig.Cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return null;
            for (int level = 2; level >= 0; level--)
            {
                float t = (level * EntityView.Level - ray.origin.y) / ray.direction.y;
                var g = ray.origin + ray.direction * t;
                var p = new Vector2Int(Mathf.RoundToInt(g.x), Mathf.RoundToInt(g.z));
                if (!Model.In(p)) continue;
                if (Model.T(p).Height == level) return p;
            }
            return null;
        }

        // ================================================================ view sync

        void SyncViews()
        {
            foreach (var u in Model.Units)
                if (!views.ContainsKey(u)) Track(u, new UnitView(u, world));
            foreach (var b in Model.Buildings)
                if (!views.ContainsKey(b)) Track(b, new BuildingView(b, world));
            foreach (var kv in views.Where(kv => kv.Key.Dead).ToList())
            {
                kv.Value.Destroy();
                views.Remove(kv.Key);
            }
            foreach (var p in pickups.Where(p => p.Tile.Gold == 0 && p.Tile.Explored).ToList())
            {
                p.Destroy();
                pickups.Remove(p);
            }
        }

        void Track(Entity e, EntityView v)
        {
            v.Badge = Hud.CreateBadge();
            views[e] = v;
        }

        EntityView View(Entity e) => e != null && views.TryGetValue(e, out var v) ? v : null;

        bool Shown(Entity e) => e.Faction == Faction.Player || Model.T(e.Pos).Visible;

        public void Refresh()
        {
            RefreshMap();
            Hud.Refresh();
        }

        void RefreshMap()
        {
            var enemyThreat = Mode == Mode.Unit && SelUnit != null && SelUnit.Faction == Faction.Enemy && !SelUnit.Dead
                ? Model.ThreatOf(SelUnit) : null;
            Map.SetColors(t =>
            {
                if (!t.Explored) return Palette.Unexplored;
                var p = t.Pos;
                var c = (p.x + p.y) % 2 == 0 ? Palette.VisibleA : Palette.VisibleB;
                if (t.Water) c = (p.x + p.y) % 2 == 0 ? Palette.Water : Color.Lerp(Palette.Water, Palette.WaterDeep, 0.5f);
                else if (t.Forest) c = Palette.ForestFloor;
                else if (t.Road) c = Color.Lerp(c, Palette.Road, 0.75f);
                if (!t.Visible) c = Color.Lerp(c, Palette.Explored, t.Forest && SeenAround(p) ? 0.35f : 0.62f);
                switch (Mode)
                {
                    case Mode.Unit:
                        if (moveTiles.Contains(p) || (SelUnit != null && SelUnit.Pos == p && moveTiles.Count > 0)) c = Palette.MoveFill;
                        else if (attackTiles.ContainsKey(p)) c = Palette.AttackFill;
                        else if (enemyThreat != null && enemyThreat.Contains(p)) c = Color.Lerp(c, Palette.AttackFill, 0.6f);
                        break;
                    case Mode.Place:
                        if (PlaceChoice == p) c = Palette.PlaceChosen;
                        else if (placeTiles.Contains(p)) c = Palette.PlaceFill;
                        break;
                    case Mode.Spell:
                        if (spellTiles.Contains(p)) c = Color.Lerp(c, Palette.SpellFill, 0.75f);
                        break;
                    case Mode.Ability:
                        if (abilityTiles.Contains(p)) c = Color.Lerp(c, Palette.SpellFill, 0.8f);
                        break;
                }
                return c;
            });

            Map.Range.Clear();
            Map.Targets.Clear();
            Map.Place.Clear();
            hoverTile = null;
            Map.Hover.Clear();
            ClearForecast();
            UpdateThreat(null);
            ComputeDanger();
            switch (Mode)
            {
                case Mode.Unit:
                    if (moveTiles.Count > 0)
                    {
                        var region = new HashSet<Vector2Int>(moveTiles) { SelUnit.Pos };
                        Map.Range.Set(region, UIKit.A(Palette.White, 0.95f));
                    }
                    if (attackTiles.Count > 0) Map.Targets.Set(attackTiles.Keys, Palette.Red, true);
                    break;
                case Mode.Place:
                    Map.Place.Set(placeTiles, Palette.Cyan, true);
                    break;
                case Mode.Spell:
                    Map.Place.Set(spellTiles, UIKit.A(Palette.Lilac, 0.9f), ActiveSpell != SpellType.Meteor);
                    break;
                case Mode.Ability:
                    Map.Place.Set(abilityTiles, Palette.Lilac, true);
                    break;
            }

            var sel = Selected;
            if (sel != null && Mode != Mode.Busy)
            {
                var color = sel.Faction == Faction.Enemy ? Palette.Red : sel is Building ? Palette.Cyan : Palette.White;
                Map.SetBrackets(sel.Pos, color);
            }
            else Map.SetBrackets(null, Color.white);
        }

        // ================================================================ hints

        public string HintText
        {
            get
            {
                if (Flash != null) return Flash;
                if (HoverHint != null) return HoverHint;
                if (Forecast != null)
                {
                    var f = Forecast;
                    string hit = f.Kills ? $"Deal {f.Dealt} · destroys {ForecastTarget.Name}" : $"Deal {f.Dealt}";
                    string back = !f.Counter ? "no counter" : f.Dies ? $"take {f.Taken} · your squad falls" : $"take {f.Taken}";
                    return $"{hit} · {back}";
                }
                if (TileHint != null && (Mode == Mode.Idle || Mode == Mode.Unit || Mode == Mode.Building)) return TileHint;
                switch (Mode)
                {
                    case Mode.Busy: return Model.Current == Faction.Enemy ? "Enemy turn…" : null;
                    case Mode.Over: return null;
                    case Mode.Place: return null;
                    case Mode.Spell: return Defs.Spell(ActiveSpell).Hint;
                    case Mode.Ability: return SelUnit != null ? Model.AbilityOf(SelUnit)?.Hint : null;
                    case Mode.Building:
                        if (SelBuilding != null && SelBuilding.Faction == Faction.Player && SelBuilding.Def.Researches)
                            return SelBuilding.Complete ? "Pick research on the left" : "Research can start once it's built";
                        return SelBuilding != null && SelBuilding.Faction == Faction.Player && SelBuilding.Type == BuildingType.Keep
                            ? "Pick a unit to recruit, or open Build"
                            : SelBuilding?.Def.PassiveText;
                    case Mode.Unit:
                        if (SelUnit == null) return null;
                        if (SelUnit.Faction != Faction.Player) return $"Enemy {SelUnit.Name}";
                        if (SelUnit.Rooted && attackTiles.Count == 0) return "Pinned: it can't move this turn";
                        if (moveTiles.Count > 0 && attackTiles.Count > 0) return "Click a white tile to move, or a red target to attack";
                        if (moveTiles.Count > 0) return "Click a highlighted tile to move";
                        if (attackTiles.Count > 0) return "Click a red target to attack";
                        if (Model.CanUseAbility(SelUnit)) return $"Use {Model.AbilityOf(SelUnit).Name} (Q), or select another squad";
                        return SelUnit.Fresh ? "Recruits can act next turn" : "This squad is done for the turn";
                    default:
                        return Model.Turn == 1 && Model.Units.All(u => !u.Moved) ? "Select a squad or your Keep to begin" : null;
                }
            }
        }

        /// <summary>What the pointer is over when it's a site or special ground: shown in the hint bar.</summary>
        public string TileHint { get; private set; }

        string DescribeTile(Vector2Int p)
        {
            var t = Model.T(p);
            if (!t.Explored) return null;
            if (t.Poi != PoiType.None)
            {
                var def = Defs.Poi(t.Poi);
                string owner = t.Poi == PoiType.Spring ? "" : t.Owner == Faction.Player ? " · yours" : t.Owner == Faction.Enemy ? " · enemy's" : " · unclaimed";
                return $"{def.Name}{owner}: {def.Desc}";
            }
            if (t.Unit != null || t.Building != null) return null;
            if (t.Forest) return "Forest: costs 2 moves to enter, +1 DEF, and squads inside are only seen from next door";
            if (t.Water) return "Water: only flyers can cross";
            if (t.Road) return "Road: squads moving along it go further";
            return null;
        }

        /// <summary>Forest tiles look half-lit when a squad of ours stands near enough to see the trees, if not inside.</summary>
        bool SeenAround(Vector2Int p)
        {
            foreach (var d in GameModel.Dirs8)
                if (Model.In(p + d) && Model.T(p + d).Visible && !Model.T(p + d).Forest) return true;
            return false;
        }

        void FlashHint(string text)
        {
            Flash = text;
            flashUntil = Time.time + 2f;
            Hud.Refresh();
        }

        // ================================================================ selection

        bool PlayerInput => Model.Current == Faction.Player && Mode != Mode.Busy && Mode != Mode.Over && !MenuOpen && !ConfirmOpen;

        void ClickTile(Vector2Int p)
        {
            switch (Mode)
            {
                case Mode.Place:
                    if (placeTiles.Contains(p))
                    {
                        PlaceChoice = p;
                        UpdateGhost();
                        Refresh();
                    }
                    return;
                case Mode.Spell:
                    if (spellTiles.Contains(p)) StartCoroutine(CastRoutine(ActiveSpell, p));
                    else ExitSpell();
                    return;
                case Mode.Ability:
                    if (abilityTiles.Contains(p)) StartCoroutine(AbilityRoutine(SelUnit, p));
                    else SelectUnit(SelUnit);
                    return;
                case Mode.Unit:
                    if (moveTiles.Contains(p))
                    {
                        StartCoroutine(MoveRoutine(SelUnit, p));
                        return;
                    }
                    if (attackTiles.TryGetValue(p, out var target))
                    {
                        StartCoroutine(AttackRoutine(SelUnit, target));
                        return;
                    }
                    break;
            }
            var t = Model.T(p);
            if (t.Unit != null && (t.Unit.Faction == Faction.Player || t.Visible)) SelectUnit(t.Unit);
            else if (t.Building != null && t.Explored) SelectBuilding(t.Building);
            else Deselect();
        }

        public void SelectUnit(Unit u)
        {
            ClearGhost();
            SelUnit = u;
            SelBuilding = null;
            Mode = Mode.Unit;
            ComputeUnitOptions();
            Refresh();
        }

        void SelectBuilding(Building b)
        {
            ClearGhost();
            SelUnit = null;
            SelBuilding = b;
            Mode = Mode.Building;
            if (b.Faction == Faction.Player && b.Type != BuildingType.Keep && b.Complete) LastOrder = null;
            Refresh();
        }

        void Deselect()
        {
            ClearGhost();
            SelUnit = null;
            SelBuilding = null;
            Mode = Mode.Idle;
            moveTiles.Clear();
            attackTiles.Clear();
            Refresh();
        }

        void ComputeUnitOptions()
        {
            moveTiles.Clear();
            attackTiles.Clear();
            attackFrom.Clear();
            reach = null;
            var u = SelUnit;
            if (u == null || u.Dead || u.Faction != Faction.Player || Model.Current != Faction.Player) return;

            if (Model.CanMove(u))
            {
                reach = Model.Reach(u, Model.MoveOf(u));
                foreach (var d in Model.Destinations(u, reach)) moveTiles.Add(d);
            }
            if (!Model.CanAttack(u)) return;
            foreach (var t in Model.TargetsFrom(u, u.Pos)) attackTiles[t.Pos] = t;
            if (reach == null) return;

            // Targets reachable by moving first: pick the shortest walk (ranged units prefer standing back).
            var best = new Dictionary<Entity, float>();
            foreach (var d in moveTiles)
            {
                int walk = GameModel.PathTo(reach, d).Count;
                foreach (var t in Model.TargetsFrom(u, d))
                {
                    if (attackTiles.TryGetValue(t.Pos, out var existing) && !attackFrom.ContainsKey(existing)) continue;
                    float score = walk - (u.Def.Range > 1 ? GameModel.Dist(d, t.Pos) * 2f : 0f);
                    if (best.TryGetValue(t, out var s) && s <= score) continue;
                    best[t] = score;
                    attackTiles[t.Pos] = t;
                    attackFrom[t] = d;
                }
            }
        }

        public List<Unit> ReadyUnits() =>
            Model.Current == Faction.Player
                ? Model.Units.Where(u => u.Faction == Faction.Player && Model.CanAct(u)).OrderBy(u => u.Id).ToList()
                : new List<Unit>();

        public void NextUnit()
        {
            if (!PlayerInput) return;
            var list = ReadyUnits();
            if (list.Count == 0) return;
            int i = SelUnit != null ? list.IndexOf(SelUnit) : -1;
            var u = list[(i + 1) % list.Count];
            SelectUnit(u);
            Rig.JumpTo(EntityView.Tile(u.Pos));
        }

        public void Cancel()
        {
            switch (Mode)
            {
                case Mode.Place: CancelPlacement(); break;
                case Mode.Spell: ExitSpell(); break;
                case Mode.Ability: SelectUnit(SelUnit); break;
                case Mode.Unit:
                case Mode.Building: Deselect(); break;
            }
        }

        public void ToggleMenu()
        {
            if (Mode == Mode.Over) return;
            MenuOpen = !MenuOpen;
            Hud.ShowMenu(MenuOpen);
        }

        // ================================================================ recruit / build

        public void ShowRoster(bool recruit)
        {
            if (RecruitOpen == recruit) return;
            if (Mode == Mode.Place) CancelPlacement();
            RecruitOpen = recruit;
            Refresh();
        }

        public void OnRosterClick(UnitType t)
        {
            if (!PlayerInput || System.Array.IndexOf(Roster, t) < 0) return;
            if (Mode == Mode.Place && !PlaceIsBuilding && PlaceUnit == t)
            {
                CancelPlacement();
                return;
            }
            BeginPlacement(false, t, default);
        }

        public void OnBuildClick(BuildingType t)
        {
            if (!PlayerInput) return;
            if (Mode == Mode.Place && PlaceIsBuilding && PlaceBuilding == t)
            {
                CancelPlacement();
                return;
            }
            BeginPlacement(true, default, t);
        }

        void BeginPlacement(bool building, UnitType ut, BuildingType bt)
        {
            ClearGhost();
            SelUnit = null;
            SelBuilding = Model.Of(Faction.Player).Keep;
            Mode = Mode.Place;
            PlaceIsBuilding = building;
            PlaceUnit = ut;
            PlaceBuilding = bt;
            RecruitOpen = !building;
            placeTiles.Clear();
            foreach (var p in building ? Model.BuildTiles(Faction.Player) : Model.RecruitTiles(Faction.Player)) placeTiles.Add(p);
            PlaceChoice = placeTiles.Count == 1 ? placeTiles.First() : (Vector2Int?)null;
            UpdateGhost();
            Refresh();
        }

        /// <summary>Squad size chosen on the Minions card: one minion or a full squad of four.</summary>
        public int MinionCount { get; private set; } = Defs.MinionSquad;
        public int MinionsToSpawn => Mathf.Max(1, Mathf.Min(MinionCount, Model.Of(Faction.Player).Reserve));

        public void SetMinionCount(int n)
        {
            MinionCount = n;
            UpdateGhost();
            Refresh();
        }

        public string PlacementProblem =>
            Mode != Mode.Place ? null
            : PlaceIsBuilding ? Model.BuildProblem(Faction.Player, PlaceBuilding)
            : Model.RecruitProblem(Faction.Player, PlaceUnit);

        public bool CanConfirm => Mode == Mode.Place && PlaceChoice.HasValue && PlacementProblem == null;

        public void ConfirmPlacement()
        {
            if (!CanConfirm) return;
            undo.Clear();
            var p = PlaceChoice.Value;
            var keep = Model.Of(Faction.Player).Keep;
            int cost;
            if (PlaceIsBuilding)
            {
                Model.Build(Faction.Player, PlaceBuilding, p);
                cost = Defs.Building(PlaceBuilding).Cost;
                LastOrder = "Building: " + Defs.Building(PlaceBuilding).Name;
            }
            else
            {
                Model.Recruit(Faction.Player, PlaceUnit, p, MinionCount);
                cost = Defs.Unit(PlaceUnit).Cost;
                LastOrder = "Recruiting: " + Defs.Unit(PlaceUnit).Name;
            }
            ClearGhost();
            SyncViews();
            if (cost > 0) Hud.Popup(EntityView.Tile(keep.Pos) + Vector3.up, $"-{cost}", Palette.Gold);
            SelBuilding = keep;
            Mode = Mode.Building;
            Refresh();
        }

        void CancelPlacement()
        {
            ClearGhost();
            SelUnit = null;
            SelBuilding = Model.Of(Faction.Player).Keep;
            Mode = Mode.Building;
            Refresh();
        }

        void UpdateGhost()
        {
            ClearGhost();
            if (!PlaceChoice.HasValue) return;
            int count = MinionsToSpawn;
            ghost = PlaceIsBuilding
                ? (EntityView)new BuildingView(null, world, true, PlaceBuilding)
                : new UnitView(null, world, true, PlaceUnit, count);
            ghost.Root.localPosition = EntityView.Tile(PlaceChoice.Value);
        }

        void ClearGhost()
        {
            if (ghost == null) return;
            ghost.Destroy();
            ghost = null;
        }

        // ================================================================ spells and abilities

        public void OnSpellClick(SpellType s)
        {
            if (!PlayerInput || !Spells.Contains(s)) return;
            if (Mode == Mode.Spell && ActiveSpell == s)
            {
                ExitSpell();
                return;
            }
            var def = Defs.Spell(s);
            if (Model.Of(Faction.Player).Mana < def.Cost)
            {
                FlashHint($"Not enough mana for {def.Name} ({def.Cost})");
                return;
            }
            var targets = Model.SpellTargets(Faction.Player, s);
            if (targets.Count == 0)
            {
                FlashHint($"No targets for {def.Name}");
                return;
            }
            if (Mode == Mode.Place) CancelPlacement();
            if (Mode != Mode.Spell) modeBeforeSpell = Mode;
            if (def.Instant)
            {
                StartCoroutine(CastRoutine(s, targets[0]));
                return;
            }
            spellTiles.Clear();
            foreach (var t in targets) spellTiles.Add(t);
            ActiveSpell = s;
            Mode = Mode.Spell;
            Refresh();
        }

        void ExitSpell()
        {
            RestoreSelection(modeBeforeSpell);
        }

        void RestoreSelection(Mode previous)
        {
            if (previous == Mode.Unit && SelUnit != null && !SelUnit.Dead) SelectUnit(SelUnit);
            else if (previous == Mode.Building && SelBuilding != null && !SelBuilding.Dead) SelectBuilding(SelBuilding);
            else Deselect();
        }

        /// <summary>The selected squad's ability button (or Q). Self abilities fire at once; others ask for a target.</summary>
        public void OnAbilityClick()
        {
            if (!PlayerInput || SelUnit == null || SelUnit.Faction != Faction.Player) return;
            if (Mode == Mode.Ability)
            {
                SelectUnit(SelUnit);
                return;
            }
            var a = Model.AbilityOf(SelUnit);
            if (a == null) return;
            if (!Model.AbilityReady(SelUnit))
            {
                FlashHint(SelUnit.Cooldown > 0 ? $"{a.Name} is ready in {SelUnit.Cooldown} {(SelUnit.Cooldown == 1 ? "turn" : "turns")}"
                    : SelUnit.Fresh ? "Recruits can act next turn" : $"{SelUnit.Name} can't use {a.Name} now");
                return;
            }
            var targets = Model.AbilityTargets(SelUnit);
            if (targets.Count == 0)
            {
                FlashHint($"No targets for {a.Name}");
                return;
            }
            if (a.Target == AbilityTarget.Self)
            {
                StartCoroutine(AbilityRoutine(SelUnit, SelUnit.Pos));
                return;
            }
            abilityTiles.Clear();
            foreach (var p in targets) abilityTiles.Add(p);
            Mode = Mode.Ability;
            Refresh();
        }

        /// <summary>Start research at the selected Forge or Academy.</summary>
        public void OnResearchClick(TechType t)
        {
            var b = SelBuilding;
            if (!PlayerInput || b == null || b.Faction != Faction.Player || !b.Def.Researches) return;
            var problem = Model.ResearchProblem(b, t);
            if (problem != null)
            {
                FlashHint(problem);
                return;
            }
            undo.Clear();
            Model.StartResearch(b, t);
            Hud.Popup(View(b).BadgeAnchor, $"-{Defs.Tech(t).Cost} RP", Palette.Jade);
            Refresh();
        }

        // ================================================================ player actions

        IEnumerator MoveRoutine(Unit u, Vector2Int dest)
        {
            Mode = Mode.Busy;
            Refresh();
            var path = GameModel.PathTo(reach, dest);
            var record = new MoveRecord { U = u, From = u.Pos, Dest = dest, WasMoved = u.Moved };
            var seenBefore = VisibleEnemies();
            record.Gold = Model.Move(u, dest, out record.Owner);
            // A move that scouted new enemies can't be taken back, or undo would be a free peek through the fog.
            bool revealed = VisibleEnemies().Any(e => !seenBefore.Contains(e));
            if (revealed) undo.Clear();
            else undo.Push(record);
            yield return AnimMove(u, path);
            if (record.Gold > 0) Hud.Popup(View(u).BadgeAnchor, $"+{record.Gold} gold", Palette.Gold);
            if (Model.T(dest).Owner == Faction.Player && record.Owner != Faction.Player)
                Hud.Popup(View(u).BadgeAnchor + Vector3.up * 0.3f, Defs.Poi(Model.T(dest).Poi).Name + " claimed", Palette.Cyan);
            SyncViews();
            AfterAction(u);
            if (revealed) FlashHint("Enemy spotted: this move can't be undone");
        }

        HashSet<Unit> VisibleEnemies() =>
            new HashSet<Unit>(Model.Units.Where(e => e.Faction == Faction.Enemy && !e.Dead && Model.T(e.Pos).Visible));

        public bool CanUndo => PlayerInput && undo.Count > 0 && Mode != Mode.Place;

        /// <summary>Takes back the most recent move. Any other action (attack, spell, recruit, end turn) clears the history.</summary>
        public void Undo()
        {
            if (!CanUndo) return;
            var r = undo.Pop();
            if (Mode == Mode.Spell || Mode == Mode.Ability) Deselect();
            Model.UndoMove(r.U, r.From, r.WasMoved, r.Gold, r.Owner);
            if (r.Gold > 0 && pickups.All(p => p.Tile.Pos != r.Dest)) pickups.Add(new PickupView(Model.T(r.Dest), world));
            var v = View(r.U);
            if (v != null) v.Root.localPosition = EntityView.Tile(r.From);
            if (r.Gold > 0) Hud.Popup(EntityView.Tile(r.Dest) + Vector3.up * 0.6f, $"-{r.Gold} gold", Palette.Gold);
            SelectUnit(r.U);
            Rig.JumpTo(EntityView.Tile(r.From));
        }

        IEnumerator AttackRoutine(Unit u, Entity target)
        {
            undo.Clear();
            Mode = Mode.Busy;
            Refresh();
            if (attackFrom.TryGetValue(target, out var from) && GameModel.Dist(u.Pos, target.Pos) > Model.RangeOf(u, u.Pos))
            {
                var path = GameModel.PathTo(reach, from);
                int gold = Model.Move(u, from);
                yield return AnimMove(u, path);
                if (gold > 0) Hud.Popup(View(u).BadgeAnchor, $"+{gold} gold", Palette.Gold);
            }
            var hits = Model.Attack(u, target);
            yield return AnimAttack(u, target, hits);
            AfterAction(u);
        }

        IEnumerator AbilityRoutine(Unit u, Vector2Int p)
        {
            undo.Clear();
            Mode = Mode.Busy;
            Refresh();
            yield return PlayAbility(u, p);
            AfterAction(u);
        }

        /// <summary>Uses an ability and shows it: a lunge or pulse, any squads that moved sliding over, then the hits.</summary>
        IEnumerator PlayAbility(Unit u, Vector2Int p)
        {
            var a = Model.AbilityOf(u);
            var before = Model.Units.ToDictionary(x => x, x => x.Pos);
            var hits = Model.UseAbility(u, p);
            SyncViews();
            bool shown = Shown(u) || Model.T(p).Visible;
            if (shown)
            {
                if (a.Target == AbilityTarget.Enemy && View(u) != null) yield return Lunge(View(u), EntityView.Tile(p));
                else if (a.Target != AbilityTarget.Move) yield return AnimPulse(p, a.Type == AbilityType.Firestorm ? Palette.Gold : Palette.Lilac,
                    a.Type == AbilityType.Firestorm || a.Type == AbilityType.Whirlwind || a.Type == AbilityType.ShieldWall ? 3.2f : 1.4f);
            }
            foreach (var kv in before)
                if (!kv.Key.Dead && kv.Key.Pos != kv.Value && View(kv.Key) != null)
                {
                    if (shown) yield return AnimMove(kv.Key, new List<Vector2Int> { kv.Key.Pos });
                    else View(kv.Key).Root.localPosition = EntityView.Tile(kv.Key.Pos);
                }
            yield return PlayHits(hits);
        }

        IEnumerator CastRoutine(SpellType s, Vector2Int p)
        {
            var previous = modeBeforeSpell;
            undo.Clear();
            Mode = Mode.Busy;
            Refresh();
            var hits = Model.Cast(Faction.Player, s, p);
            SyncViews();
            yield return AnimSpell(s, p);
            yield return PlayHits(hits);
            SyncViews();
            if (CheckOver()) yield break;
            var u = Model.T(p).Unit;
            if ((s == SpellType.Ward || s == SpellType.Haste) && u != null) SelectUnit(u);
            else RestoreSelection(previous);
        }

        void AfterAction(Unit u)
        {
            SyncViews();
            if (CheckOver()) return;
            if (u != null && !u.Dead) SelectUnit(u);
            else Deselect();
        }

        public void ToggleMusic()
        {
            Music.Muted = !Music.Muted;
            Refresh();
        }

        bool CheckOver()
        {
            if (Model.Winner == null) return false;
            ClearGhost();
            SelUnit = null;
            SelBuilding = null;
            Mode = Mode.Over;
            if (Chapter != null && Model.Winner == Faction.Player && Campaign.Cleared < ChapterIndex + 1)
                Campaign.Cleared = ChapterIndex + 1;
            Refresh();
            Hud.ShowGameOver(Model.Winner == Faction.Player);
            Music.PlayEnd(Model.Winner == Faction.Player);
            return true;
        }

        // ================================================================ turns

        public bool CanEndTurn => PlayerInput && Mode != Mode.Place;

        public void EndTurn()
        {
            if (!CanEndTurn) return;
            int ready = ReadyUnits().Count;
            if (Settings.WarnEndTurn && ready > 0)
            {
                ConfirmOpen = true;
                Hud.ShowEndTurnWarning(ready);
                return;
            }
            StartCoroutine(EndTurnRoutine());
        }

        /// <summary>Answer to the "squads can still act" warning.</summary>
        public void ConfirmEndTurn(bool dontAskAgain)
        {
            if (!ConfirmOpen) return;
            CloseConfirm();
            if (dontAskAgain)
            {
                Settings.WarnEndTurn = false;
                Settings.Save();
            }
            StartCoroutine(EndTurnRoutine());
        }

        public void CancelEndTurn()
        {
            if (!ConfirmOpen) return;
            CloseConfirm();
            NextUnit();
        }

        void CloseConfirm()
        {
            ConfirmOpen = false;
            Hud.HideOverlay();
        }

        IEnumerator EndTurnRoutine()
        {
            undo.Clear();
            ClearGhost();
            SelUnit = null;
            SelBuilding = null;
            LastOrder = null;
            moveTiles.Clear();
            attackTiles.Clear();
            Mode = Mode.Busy;
            var hits = Model.BeginTurn(Faction.Enemy);
            SyncViews();
            Refresh();
            yield return PlayHits(hits);
            if (CheckOver()) yield break;

            yield return ai.Run(this);
            SyncViews();
            if (CheckOver()) yield break;

            Model.Turn++;
            if (Chapter != null && Chapter.Goal == Objective.Survive && Model.Turn >= Chapter.SurviveTurns)
                Model.Winner = Faction.Player;
            if (CheckOver()) yield break;
            hits = Model.BeginTurn(Faction.Player);
            SyncViews();
            Refresh();
            yield return PlayHits(hits);
            if (CheckOver()) yield break;
            Mode = Mode.Idle;
            Refresh();
            ShowBeats();
        }

        // ---------------------------------------------------------------- hooks used by the enemy AI

        public IEnumerator AiMove(Unit u, Vector2Int dest, Dictionary<Vector2Int, Vector2Int> r)
        {
            var path = GameModel.PathTo(r, dest);
            bool seen = path.Any(p => Model.T(p).Visible) || Model.T(u.Pos).Visible;
            Model.Move(u, dest);
            if (seen) yield return AnimMove(u, path);
            else if (View(u) != null) View(u).Root.localPosition = EntityView.Tile(u.Pos);
        }

        public IEnumerator AiAttack(Unit u, Entity target)
        {
            var hits = Model.Attack(u, target);
            if (Shown(u) || Shown(target)) yield return AnimAttack(u, target, hits);
            SyncViews();
        }

        public IEnumerator AiAbility(Unit u, Vector2Int p)
        {
            yield return PlayAbility(u, p);
            SyncViews();
        }

        public IEnumerator AiRecruit(UnitType t, Vector2Int p)
        {
            var u = Model.Recruit(Faction.Enemy, t, p);
            SyncViews();
            if (Shown(u)) yield return new WaitForSeconds(0.25f);
        }

        public IEnumerator AiBuild(BuildingType t, Vector2Int p)
        {
            Model.Build(Faction.Enemy, t, p);
            SyncViews();
            yield return null;
        }

        public IEnumerator AiCast(SpellType s, Vector2Int p)
        {
            var hits = Model.Cast(Faction.Enemy, s, p);
            if (Model.T(p).Visible)
            {
                Rig.JumpTo(EntityView.Tile(p));
                yield return AnimSpell(s, p);
            }
            yield return PlayHits(hits);
            SyncViews();
        }

        // ================================================================ animation

        IEnumerator AnimMove(Unit u, List<Vector2Int> path)
        {
            var v = View(u);
            if (v == null) yield break;
            var from = v.Root.localPosition;
            foreach (var step in path)
            {
                var to = EntityView.Tile(step);
                for (float t = 0; t < 1f; t += Time.deltaTime / 0.13f)
                {
                    var p = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, t));
                    p.y += Mathf.Sin(t * Mathf.PI) * (0.08f + Mathf.Abs(to.y - from.y) * 0.5f);
                    v.Root.localPosition = p;
                    yield return null;
                }
                from = to;
            }
            v.Root.localPosition = EntityView.Tile(u.Pos);
        }

        IEnumerator Lunge(EntityView v, Vector3 toward)
        {
            if (v == null) yield break;
            var home = v.Root.localPosition;
            var dir = toward - home;
            dir.y = 0;
            dir = dir.normalized * 0.28f;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.08f)
            {
                v.Root.localPosition = home + dir * t;
                yield return null;
            }
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.14f)
            {
                v.Root.localPosition = home + dir * (1 - t);
                yield return null;
            }
            v.Root.localPosition = home;
        }

        IEnumerator AnimAttack(Unit attacker, Entity target, List<Hit> hits)
        {
            var av = View(attacker);
            var tv = View(target);
            if (av != null && tv != null) yield return Lunge(av, tv.Root.localPosition);
            yield return PlayHits(hits);
        }

        IEnumerator PlayHits(List<Hit> hits)
        {
            foreach (var h in hits)
            {
                var v = View(h.Target);
                if (v == null || !Shown(h.Target)) continue;
                if (h.Counter && h.Source != null && View(h.Source) != null)
                    yield return Lunge(View(h.Source), v.Root.localPosition);
                if (h.Label != null) Hud.Popup(v.BadgeAnchor, h.Label, Palette.Cyan);
                else if (h.Heal) Hud.Popup(v.BadgeAnchor, $"+{h.Amount}", Palette.Cyan);
                else
                {
                    Hud.Popup(v.BadgeAnchor, $"-{h.Amount}", h.Target.Faction == Faction.Player ? Palette.Red : Palette.White);
                    flashes[v] = Time.time + 0.14f;
                }
                yield return new WaitForSeconds(0.24f);
            }

            var dead = hits.Where(h => h.Killed).Select(h => h.Target).Distinct().Select(View).Where(v => v != null).ToList();
            if (dead.Count > 0)
            {
                var scales = dead.Select(v => v.Root.localScale).ToList();
                for (float t = 0; t < 1f; t += Time.deltaTime / 0.3f)
                {
                    for (int i = 0; i < dead.Count; i++)
                        dead[i].Root.localScale = scales[i] * (1f - Mathf.SmoothStep(0, 1, t));
                    yield return null;
                }
            }
            SyncViews();
            Refresh();
        }

        IEnumerator AnimSpell(SpellType s, Vector2Int p)
        {
            var pos = EntityView.Tile(p);
            if (s == SpellType.Meteor)
            {
                var rock = MeshKit.Obj("Meteor", world, MeshKit.Sphere, MeshKit.Flat, Palette.Gold, pos, Vector3.one * 0.45f);
                var start = pos + new Vector3(-2.5f, 7f, -2.5f);
                for (float t = 0; t < 1f; t += Time.deltaTime / 0.38f)
                {
                    rock.transform.localPosition = Vector3.Lerp(start, pos + Vector3.up * 0.2f, t * t);
                    yield return null;
                }
                Destroy(rock);
                yield return AnimPulse(p, Palette.Gold, 3.2f);
            }
            else if (s == SpellType.Bolt)
            {
                var beam = MeshKit.Obj("Bolt", world, MeshKit.Box, MeshKit.Trans, Palette.Lilac,
                    pos + Vector3.up * 3f, new Vector3(0.12f, 6f, 0.12f));
                var r = beam.GetComponent<Renderer>();
                for (float t = 0; t < 1f; t += Time.deltaTime / 0.3f)
                {
                    MeshKit.SetColor(r, UIKit.A(Palette.Lilac, 1f - t));
                    beam.transform.localScale = new Vector3(0.12f + t * 0.2f, 6f, 0.12f + t * 0.2f);
                    yield return null;
                }
                Destroy(beam);
            }
            else if (s == SpellType.Renew) yield return AnimPulse(p, Palette.Cyan, 3.2f);
            else if (s == SpellType.Transmute) yield return AnimPulse(p, Palette.Gold, 2.2f);
            else yield return AnimPulse(p, s == SpellType.Ward ? Palette.Cyan : Palette.Lilac);
        }

        IEnumerator AnimPulse(Vector2Int p, Color c, float size = 1.4f)
        {
            var ring = MeshKit.Obj("Pulse", world, MeshKit.Disc, MeshKit.Trans, c, EntityView.Tile(p) + Vector3.up * 0.03f, Vector3.one * 0.2f);
            var r = ring.GetComponent<Renderer>();
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.35f)
            {
                ring.transform.localScale = new Vector3(0.2f + t * size, 1, 0.2f + t * size);
                MeshKit.SetColor(r, UIKit.A(c, 0.6f * (1f - t)));
                yield return null;
            }
            Destroy(ring);
        }
    }
}
