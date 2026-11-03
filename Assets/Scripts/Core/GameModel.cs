using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Strategy
{
    public class Tile
    {
        public readonly Vector2Int Pos;
        public Terrain Terrain;
        /// <summary>Roads speed up squads that walk along them.</summary>
        public bool Road;
        public int Gold;
        /// <summary>0 = lowland, 1–2 = hills. High ground helps ranged squads and widens vision.</summary>
        public int Height;
        public PoiType Poi;
        /// <summary>Who holds the point of interest on this tile, if anyone.</summary>
        public Faction? Owner;
        public bool Explored, Visible;
        public Unit Unit;
        public Building Building;

        public Tile(int x, int y) { Pos = new Vector2Int(x, y); }
        public bool Rock => Terrain == Terrain.Rock;
        public bool Water => Terrain == Terrain.Water;
        public bool Forest => Terrain == Terrain.Forest;
        public bool Blocked => Rock || Water || Building != null;
        public Entity Occupant => (Entity)Unit ?? Building;
    }

    public abstract class Entity
    {
        public int Id;
        public Faction Faction;
        public Vector2Int Pos;
        public int Hp, MaxHp;
        public bool Dead;
        public abstract string Name { get; }
    }

    public class Unit : Entity
    {
        public UnitType Type;
        public bool Moved, Acted, Fresh, Hasted, Rallied;
        public int WardTurns, GuardTurns;
        /// <summary>Turns until its ability can be used again.</summary>
        public int Cooldown;
        /// <summary>Pinned: can't move on its side's next turn.</summary>
        public bool Rooted;

        public UnitDef Def => Defs.Unit(Type);
        public bool IsMinions => Type == UnitType.Minions;
        public bool Flying => Def.Flying;
        public override string Name => IsMinions ? (Hp == 1 ? "1 Minion" : $"{Hp} Minions") : Def.Name;
    }

    public class Building : Entity
    {
        public BuildingType Type;
        public int TurnsLeft;
        /// <summary>Research under way in this Forge or Academy, and turns until it finishes.</summary>
        public TechType? Research;
        public int ResearchLeft;

        public BuildingDef Def => Defs.Building(Type);
        public bool Complete => TurnsLeft <= 0;
        public override string Name => Def.Name;
    }

    public class Side
    {
        public Faction Faction;
        public int Gold, Mana, Reserve, SlotsUsed, Research;
        /// <summary>Extra (or fewer) gold per turn; difficulty adjusts the enemy's.</summary>
        public int IncomeBonus;
        public Building Keep;
        public readonly HashSet<TechType> Techs = new HashSet<TechType>();
    }

    /// <summary>Map generation switches a chapter can turn on.</summary>
    public class MapOptions
    {
        /// <summary>A river across the middle with a few fords, instead of scattered lakes.</summary>
        public bool River;
    }

    /// <summary>What an attack would do, without doing it. Drives the hover preview.</summary>
    public class Forecast
    {
        public int Dealt, Taken;
        public bool Kills, Counter, Dies;
    }

    /// <summary>One damage or heal event, used to drive animations and popups.</summary>
    public class Hit
    {
        public Entity Source, Target;
        public int Amount;
        public bool Heal, Killed, Counter;
        public string Label;
    }

    /// <summary>Pure game rules. No rendering or input in here.</summary>
    public class GameModel
    {
        public readonly int W, H;
        public readonly Tile[,] Tiles;
        public readonly List<Unit> Units = new List<Unit>();
        public readonly List<Building> Buildings = new List<Building>();
        public readonly Side[] Sides = new Side[2];
        public int Turn = 1;
        public Faction Current = Faction.Player;
        public Faction? Winner;
        public readonly System.Random Rng;
        /// <summary>Research both sides may study in this battle (campaign chapters open the tree a branch at a time).</summary>
        public HashSet<TechType> TechPool = new HashSet<TechType>(Defs.AllTechs);
        /// <summary>Set whenever terrain changes (forests grown or burnt) so the view can rebuild trees.</summary>
        public int TerrainVersion;
        int nextId = 1;
        readonly bool[][,] seen;

        public static readonly Vector2Int[] Dirs4 = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        public static readonly Vector2Int[] Dirs8 =
        {
            new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1), new Vector2Int(-1, 0),
            new Vector2Int(1, 0), new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1),
        };

        public GameModel(int w, int h, int seed, MapOptions options = null)
        {
            W = w; H = h;
            Rng = new System.Random(seed);
            Tiles = new Tile[w, h];
            seen = new[] { new bool[w, h], new bool[w, h] };
            Generate(options ?? new MapOptions());
        }

        public Tile T(Vector2Int p) => Tiles[p.x, p.y];
        public bool In(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < W && p.y < H;
        public Side Of(Faction f) => Sides[(int)f];
        public static Faction Other(Faction f) => f == Faction.Player ? Faction.Enemy : Faction.Player;
        public static int Dist(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        public static int Cheb(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        public IEnumerable<Tile> AllTiles() { foreach (var t in Tiles) yield return t; }
        public bool Has(Faction f, TechType t) => Of(f).Techs.Contains(t);

        public IEnumerable<Entity> Entities(Faction f)
        {
            foreach (var u in Units) if (u.Faction == f && !u.Dead) yield return u;
            foreach (var b in Buildings) if (b.Faction == f && !b.Dead) yield return b;
        }

        // ---------------------------------------------------------------- setup

        void Generate(MapOptions opt)
        {
            var playerKeep = new Vector2Int(2, 2);
            var enemyKeep = new Vector2Int(W - 3, H - 3);
            bool NearKeep(Vector2Int p, int r) => Cheb(p, playerKeep) <= r || Cheb(p, enemyKeep) <= r;
            var fords = new List<Vector2Int>();

            // Everything is mirrored through the centre so both sides get the same map.
            for (int attempt = 0; ; attempt++)
            {
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                        Tiles[x, y] = new Tile(x, y);
                fords.Clear();

                if (opt.River)
                {
                    // The anti-diagonal maps onto itself, so a river along it splits the board evenly.
                    for (int x = 0; x < W; x++)
                    {
                        var p = new Vector2Int(x, W - 1 - x);
                        if (In(p)) T(p).Terrain = Terrain.Water;
                    }
                    foreach (int x in new[] { 2, W / 2 })
                    {
                        var p = new Vector2Int(x, W - 1 - x);
                        foreach (var q in new[] { p, Mirror(p) })
                            if (In(q))
                            {
                                T(q).Terrain = Terrain.Plain;
                                fords.Add(q);
                            }
                    }
                }
                else
                {
                    int lakes = Mathf.Max(1, W * H / 70);
                    for (int i = 0; i < lakes; i++)
                    {
                        var c = new Vector2Int(Rng.Next(W), Rng.Next(H));
                        int size = 2 + Rng.Next(3);
                        for (int k = 0; k < size; k++)
                        {
                            if (In(c) && !NearKeep(c, 2))
                            {
                                T(c).Terrain = Terrain.Water;
                                T(Mirror(c)).Terrain = Terrain.Water;
                            }
                            c += Dirs4[Rng.Next(4)];
                        }
                    }
                }

                int rocks = W * H / 24;
                for (int i = 0; i < rocks; i++)
                {
                    var p = new Vector2Int(Rng.Next(W), Rng.Next(H));
                    if (NearKeep(p, 2) || T(p).Water || fords.Contains(p)) continue;
                    T(p).Terrain = Terrain.Rock;
                    T(Mirror(p)).Terrain = Terrain.Rock;
                }
                if (Connected(playerKeep, enemyKeep) || attempt > 50) break;
            }

            // Forest clumps.
            int groves = Mathf.Max(2, W * H / 36);
            for (int i = 0; i < groves; i++)
            {
                var c = new Vector2Int(Rng.Next(W), Rng.Next(H));
                int size = 2 + Rng.Next(3);
                for (int k = 0; k < size; k++)
                {
                    if (In(c) && !NearKeep(c, 2) && T(c).Terrain == Terrain.Plain && !fords.Contains(c))
                    {
                        T(c).Terrain = Terrain.Forest;
                        if (T(Mirror(c)).Terrain == Terrain.Plain) T(Mirror(c)).Terrain = Terrain.Forest;
                    }
                    c += Dirs4[Rng.Next(4)];
                }
            }

            // Hills: 3×3 plateaus (height 1) or peaks (height 2 with a height-1 ring), mirrored. Keeps stay on flat ground.
            int hills = Mathf.Max(2, W * H / 50);
            for (int i = 0; i < hills; i++)
            {
                var c = new Vector2Int(Rng.Next(W), Rng.Next(H));
                int peak = 1 + Rng.Next(2);
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        var p = c + new Vector2Int(dx, dy);
                        int h = Mathf.Clamp(peak + 1 - Cheb(p, c), 0, peak);
                        if (h == 0 || !In(p) || NearKeep(p, 2) || T(p).Water || T(Mirror(p)).Water) continue;
                        T(p).Height = Mathf.Max(T(p).Height, h);
                        T(Mirror(p)).Height = Mathf.Max(T(Mirror(p)).Height, h);
                    }
            }

            // A road from Keep to Keep (and its mirror image), through any fords.
            foreach (var p in RoadPath(playerKeep, enemyKeep))
                foreach (var q in new[] { p, Mirror(p) })
                {
                    T(q).Road = true;
                    if (T(q).Forest) T(q).Terrain = Terrain.Plain;
                }
            foreach (var f in fords) T(f).Road = true;

            // Points of interest, one of each kind on each half.
            var kinds = new List<PoiType> { PoiType.GoldMine, PoiType.Library, PoiType.Shrine, PoiType.Outpost };
            if (W >= 12) kinds.Add(PoiType.Spring);
            if (W >= 14) kinds.Add(PoiType.GoldMine);
            var placed = new List<Vector2Int>();
            foreach (var kind in kinds)
            {
                for (int guard = 0; guard < 400; guard++)
                {
                    var p = new Vector2Int(Rng.Next(W), Rng.Next(H));
                    var m = Mirror(p);
                    var t = T(p);
                    if (p == m || Cheb(p, m) < 3 || t.Water || t.Rock || t.Poi != PoiType.None || NearKeep(p, 2)) continue;
                    if (placed.Any(o => Cheb(o, p) < 3)) continue;
                    // Keep the first copy on the player's half, so each side has one closer to home.
                    if (Dist(p, playerKeep) > Dist(p, enemyKeep)) { var s = p; p = m; m = s; }
                    foreach (var q in new[] { p, m })
                    {
                        T(q).Poi = kind;
                        if (T(q).Forest) T(q).Terrain = Terrain.Plain;
                        placed.Add(q);
                    }
                    break;
                }
            }

            int pickups = 0;
            for (int guard = 0; pickups < 3 && guard < 500; guard++)
            {
                var p = new Vector2Int(Rng.Next(W), Rng.Next(H));
                var m = Mirror(p);
                var t = T(p);
                if (p == m || t.Rock || t.Water || t.Gold > 0 || t.Poi != PoiType.None) continue;
                if (Cheb(p, playerKeep) < 2 || Cheb(p, enemyKeep) < 2) continue;
                T(p).Gold = Defs.PickupGold;
                T(m).Gold = Defs.PickupGold;
                pickups++;
            }

            for (int i = 0; i < 2; i++)
            {
                var f = (Faction)i;
                var keepPos = f == Faction.Player ? playerKeep : enemyKeep;
                Sides[i] = new Side
                {
                    Faction = f, Gold = Defs.StartGold, Mana = Defs.StartMana, Reserve = Defs.MinionReserve, Research = Defs.StartResearch,
                };
                Sides[i].Keep = AddBuilding(BuildingType.Keep, f, keepPos, true);
                int s = f == Faction.Player ? 1 : -1;
                Spawn(UnitType.Minions, f, keepPos + new Vector2Int(s, 0), Defs.MinionSquad);
                Spawn(UnitType.Spear, f, keepPos + new Vector2Int(0, s), 0);
            }
            UpdateFog();
        }

        Vector2Int Mirror(Vector2Int p) => new Vector2Int(W - 1 - p.x, H - 1 - p.y);

        bool Walkable(Vector2Int p) => In(p) && !T(p).Rock && !T(p).Water;

        bool Connected(Vector2Int a, Vector2Int b) => RoadPath(a, b).Count > 0;

        /// <summary>Shortest walk between two tiles over land, preferring flat ground (empty if there is none).</summary>
        List<Vector2Int> RoadPath(Vector2Int a, Vector2Int b)
        {
            var parent = new Dictionary<Vector2Int, Vector2Int> { [a] = a };
            var q = new Queue<Vector2Int>();
            q.Enqueue(a);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (c == b) break;
                foreach (var d in Dirs4)
                {
                    var n = c + d;
                    if (Walkable(n) && !parent.ContainsKey(n))
                    {
                        parent[n] = c;
                        q.Enqueue(n);
                    }
                }
            }
            if (!parent.ContainsKey(b)) return new List<Vector2Int>();
            var path = new List<Vector2Int> { b };
            for (var c = b; c != a; c = parent[c]) path.Add(parent[c]);
            return path;
        }

        public Unit Spawn(UnitType type, Faction f, Vector2Int pos, int minionCount)
        {
            var def = Defs.Unit(type);
            int hp = type == UnitType.Minions ? minionCount : def.Health + (Has(f, TechType.Padding) ? 2 : 0);
            var u = new Unit { Id = nextId++, Type = type, Faction = f, Pos = pos, Hp = hp, MaxHp = hp };
            Units.Add(u);
            T(pos).Unit = u;
            Capture(u);
            return u;
        }

        Building AddBuilding(BuildingType type, Faction f, Vector2Int pos, bool complete)
        {
            var def = Defs.Building(type);
            var b = new Building
            {
                Id = nextId++, Type = type, Faction = f, Pos = pos, Hp = def.Health, MaxHp = def.Health,
                TurnsLeft = complete ? 0 : def.Turns,
            };
            Buildings.Add(b);
            T(pos).Building = b;
            return b;
        }

        /// <summary>A squad standing on a point of interest claims it. Returns the previous holder for undo.</summary>
        Faction? Capture(Unit u)
        {
            var t = T(u.Pos);
            var before = t.Owner;
            if (t.Poi != PoiType.None && t.Poi != PoiType.Spring) t.Owner = u.Faction;
            return before;
        }

        public int CountPoi(Faction f, PoiType kind) => AllTiles().Count(t => t.Poi == kind && t.Owner == f);

        // ---------------------------------------------------------------- stats

        /// <summary>Attack range from a tile: ranged squads reach 1 further from a hill.</summary>
        public int RangeOf(Unit u, Vector2Int at) =>
            u.Def.Range + (u.Def.Range > 1 && T(at).Height > 0 ? Defs.HillRange : 0) +
            ((u.Type == UnitType.Bow || u.Type == UnitType.Crossbow) && Has(u.Faction, TechType.Longbows) ? 1 : 0);

        /// <summary>Attack against a specific target: ranged squads hit harder shooting down at lower ground.</summary>
        public int AttackOf(Unit u, bool counter, Entity target) =>
            AttackOf(u, counter) + (u.Def.Range > 1 && T(u.Pos).Height > T(target.Pos).Height ? Defs.HighGroundAttack : 0);

        public int AttackOf(Unit u, bool counter)
        {
            int a = u.IsMinions ? u.Hp : u.Def.Attack;
            if (counter && u.Type == UnitType.Spear) a += 2;
            if (counter && u.Type == UnitType.Sword) a += 1;
            if (!counter && u.Type == UnitType.Crossbow && !u.Moved) a += 1;
            if (!counter && u.Type == UnitType.Rider && u.Moved) a += 2;
            if (u.Type == UnitType.Druid && T(u.Pos).Forest) a += 1;
            if (u.Rallied) a += Defs.Spell(SpellType.Rally).Power;
            if (!u.IsMinions && u.Def.Melee && Has(u.Faction, TechType.Blades)) a += 1;
            if (!u.Def.Melee && Has(u.Faction, TechType.Fletching)) a += 1;
            return a;
        }

        public int MoveOf(Unit u)
        {
            int m = u.Def.Move + (u.Hasted ? 1 : 0);
            if (Has(u.Faction, TechType.ForcedMarch)) m += 1;
            if ((u.Type == UnitType.Knight || u.Type == UnitType.Rider) && Has(u.Faction, TechType.WarHorses)) m += 1;
            return m;
        }

        /// <summary>Damage from one blow: attack minus defense, but armour can at most halve a hit.</summary>
        public static int Blow(int attack, int defense) => Mathf.Max(1, (attack + 1) / 2, attack - defense);

        /// <summary>A Giant's hide caps any single hit.</summary>
        public static int Soak(Entity target, int dmg) =>
            target is Unit u && u.Type == UnitType.Giant ? Mathf.Min(dmg, Defs.GiantHitCap) : dmg;

        public int DefenseOf(Entity e)
        {
            if (e is Building b) return b.Def.Defense;
            var u = (Unit)e;
            int d = u.Def.Defense;
            if (u.WardTurns > 0) d += Defs.Spell(SpellType.Ward).Power;
            if (u.GuardTurns > 0) d += Defs.GuardDefense;
            if (u.Type == UnitType.Knight && !u.Moved) d += 1;
            if (T(u.Pos).Forest) d += Defs.ForestDefense + (u.Type == UnitType.Druid ? 1 : 0);
            if (!u.IsMinions && Has(u.Faction, TechType.Plate)) d += 1;
            foreach (var dir in Dirs4)
            {
                var n = u.Pos + dir;
                if (In(n) && T(n).Unit is Unit ally && ally.Faction == u.Faction && ally.Type == UnitType.Shield)
                {
                    d += 1;
                    break;
                }
            }
            return d;
        }

        public int MaxSlots(Faction f) =>
            Defs.BaseSlots + Buildings.Count(b => b.Faction == f && !b.Dead && b.Complete && b.Type == BuildingType.Barracks);

        public int SlotsLeft(Faction f) => MaxSlots(f) - Of(f).SlotsUsed;

        // ---------------------------------------------------------------- movement

        /// <summary>Cost (in thirds of a tile) to step onto a tile. Flyers pay the same everywhere.</summary>
        public int StepCost(bool flying, Vector2Int from, Vector2Int to)
        {
            if (flying) return Defs.StepCost;
            var t = T(to);
            if (t.Forest) return Defs.ForestCost;
            if (t.Road && T(from).Road) return Defs.RoadCost;
            return Defs.StepCost;
        }

        /// <summary>
        /// Cheapest paths within a move budget (Dijkstra). Squads pass through allies but not enemies;
        /// flyers also pass over water, rock and buildings. Returns each reachable tile's parent.
        /// </summary>
        public Dictionary<Vector2Int, Vector2Int> Reach(Unit u, int range)
        {
            int budget = range * Defs.StepCost;
            var parent = new Dictionary<Vector2Int, Vector2Int> { [u.Pos] = u.Pos };
            var cost = new Dictionary<Vector2Int, int> { [u.Pos] = 0 };
            var open = new List<Vector2Int> { u.Pos };
            var done = new HashSet<Vector2Int>();
            while (open.Count > 0)
            {
                int bi = 0;
                for (int i = 1; i < open.Count; i++)
                    if (cost[open[i]] < cost[open[bi]]) bi = i;
                var c = open[bi];
                open.RemoveAt(bi);
                if (!done.Add(c)) continue;
                foreach (var d in Dirs4)
                {
                    var n = c + d;
                    if (!In(n) || done.Contains(n)) continue;
                    var t = T(n);
                    if (!u.Flying && t.Blocked) continue;
                    if (t.Unit != null && t.Unit.Faction != u.Faction) continue;
                    int nc = cost[c] + StepCost(u.Flying, c, n);
                    if (nc > budget || (cost.TryGetValue(n, out var old) && old <= nc)) continue;
                    cost[n] = nc;
                    parent[n] = c;
                    open.Add(n);
                }
            }
            return parent;
        }

        public bool CanStand(Vector2Int p) => In(p) && !T(p).Blocked && T(p).Unit == null;

        public List<Vector2Int> Destinations(Unit u, Dictionary<Vector2Int, Vector2Int> reach) =>
            reach.Keys.Where(p => p != u.Pos && CanStand(p)).ToList();

        public static List<Vector2Int> PathTo(Dictionary<Vector2Int, Vector2Int> parent, Vector2Int target)
        {
            var path = new List<Vector2Int>();
            var c = target;
            while (parent[c] != c)
            {
                path.Add(c);
                c = parent[c];
            }
            path.Reverse();
            return path;
        }

        /// <summary>Walking cost from every tile to the nearest goal, for AI marching. Ignores squads.</summary>
        public Dictionary<Vector2Int, int> CostField(IEnumerable<Vector2Int> goals, bool flying)
        {
            var dist = new Dictionary<Vector2Int, int>();
            var open = new List<Vector2Int>();
            foreach (var g in goals)
                if (!dist.ContainsKey(g))
                {
                    dist[g] = 0;
                    open.Add(g);
                }
            var done = new HashSet<Vector2Int>();
            while (open.Count > 0)
            {
                int bi = 0;
                for (int i = 1; i < open.Count; i++)
                    if (dist[open[i]] < dist[open[bi]]) bi = i;
                var c = open[bi];
                open.RemoveAt(bi);
                if (!done.Add(c)) continue;
                foreach (var d in Dirs4)
                {
                    var n = c + d;
                    if (!In(n) || done.Contains(n) || (!flying && T(n).Blocked)) continue;
                    int nc = dist[c] + StepCost(flying, n, c);
                    if (dist.TryGetValue(n, out var old) && old <= nc) continue;
                    dist[n] = nc;
                    open.Add(n);
                }
            }
            return dist;
        }

        public bool CanMove(Unit u) => !u.Moved && !u.Acted && !u.Fresh && !u.Rooted;
        public bool CanAttack(Unit u) => !u.Acted && !u.Fresh;

        /// <summary>Moves a squad, collects gold and claims any point of interest there. Returns gold picked up.</summary>
        public int Move(Unit u, Vector2Int dest) => Move(u, dest, out _);

        public int Move(Unit u, Vector2Int dest, out Faction? previousOwner)
        {
            T(u.Pos).Unit = null;
            u.Pos = dest;
            T(dest).Unit = u;
            u.Moved = true;
            var t = T(dest);
            int gold = t.Gold;
            if (gold > 0)
            {
                Of(u.Faction).Gold += gold;
                t.Gold = 0;
            }
            previousOwner = Capture(u);
            UpdateFog();
            return gold;
        }

        /// <summary>Reverses <see cref="Move"/>: puts the squad back, returns gold to the tile and the site to its old holder.</summary>
        public void UndoMove(Unit u, Vector2Int from, bool wasMoved, int gold, Faction? previousOwner)
        {
            var t = T(u.Pos);
            t.Unit = null;
            if (gold > 0)
            {
                t.Gold += gold;
                Of(u.Faction).Gold -= gold;
            }
            t.Owner = previousOwner;
            u.Pos = from;
            T(from).Unit = u;
            u.Moved = wasMoved;
            UpdateFog();
        }

        // ---------------------------------------------------------------- combat

        public List<Entity> TargetsFrom(Unit u, Vector2Int from)
        {
            var list = new List<Entity>();
            foreach (var e in Entities(Other(u.Faction)))
            {
                int d = Dist(from, e.Pos);
                if (d < 1 || d > RangeOf(u, from)) continue;
                if (!SeenBy(u.Faction, e.Pos)) continue;
                list.Add(e);
            }
            return list;
        }

        /// <summary>True while a squad still has something useful to do this turn.</summary>
        public bool CanAct(Unit u)
        {
            if (u.Dead || u.Acted || u.Fresh) return false;
            if (CanMove(u)) return true;
            return TargetsFrom(u, u.Pos).Count > 0 || CanUseAbility(u);
        }

        /// <summary>Whether a struck squad hits back: it must survive, reach the attacker, and the attacker can't be a Ninja.</summary>
        bool Counters(Unit attacker, Unit defender) =>
            attacker.Type != UnitType.Ninja && Dist(attacker.Pos, defender.Pos) <= RangeOf(defender, defender.Pos);

        public List<Hit> Attack(Unit a, Entity target)
        {
            var hits = new List<Hit>();
            int strikes = a.Type == UnitType.Twinblade ? 2 : 1;
            for (int i = 0; i < strikes && !target.Dead; i++)
            {
                int dmg = Blow(AttackOf(a, false, target), DefenseOf(target));
                hits.Add(Damage(a, target, dmg));
            }
            if (a.Type == UnitType.Pyromancer)
                foreach (var d4 in Dirs4)
                {
                    var n = target.Pos + d4;
                    if (In(n) && T(n).Occupant is Entity burn && burn.Faction != a.Faction && !burn.Dead)
                        hits.Add(Damage(a, burn, 1));
                }
            if (!target.Dead && target is Unit d && Counters(a, d))
            {
                int dmg = Blow(AttackOf(d, true, a), DefenseOf(a));
                var h = Damage(d, a, dmg);
                h.Counter = true;
                hits.Add(h);
            }
            a.Acted = true;
            a.Moved = true;
            return hits;
        }

        /// <summary>
        /// Runs the same maths as <see cref="Attack"/> with the attacker standing on <paramref name="from"/>,
        /// then puts everything back.
        /// </summary>
        public Forecast Preview(Unit a, Entity target, Vector2Int from)
        {
            var f = new Forecast();
            var oldPos = a.Pos;
            bool oldMoved = a.Moved;
            int oldHp = target.Hp;
            bool moved = from != oldPos;
            if (moved)
            {
                T(oldPos).Unit = null;
                T(from).Unit = a;
                a.Pos = from;
                a.Moved = true;
            }
            try
            {
                int strikes = a.Type == UnitType.Twinblade ? 2 : 1;
                for (int i = 0; i < strikes && target.Hp > 0; i++)
                {
                    int dmg = Mathf.Min(target.Hp, Soak(target, Blow(AttackOf(a, false, target), DefenseOf(target))));
                    target.Hp -= dmg;
                    f.Dealt += dmg;
                }
                f.Kills = target.Hp <= 0;
                if (!f.Kills && target is Unit d && Counters(a, d))
                {
                    f.Counter = true;
                    f.Taken = Mathf.Min(a.Hp, Soak(a, Blow(AttackOf(d, true, a), DefenseOf(a))));
                    f.Dies = f.Taken >= a.Hp;
                }
            }
            finally
            {
                target.Hp = oldHp;
                if (moved)
                {
                    T(from).Unit = null;
                    T(oldPos).Unit = a;
                    a.Pos = oldPos;
                    a.Moved = oldMoved;
                }
            }
            return f;
        }

        /// <summary>Every tile a squad could strike on its next turn: anywhere it can walk to, plus its range.</summary>
        public HashSet<Vector2Int> ThreatOf(Unit u)
        {
            var tiles = new HashSet<Vector2Int>();
            foreach (var stand in Reach(u, MoveOf(u)).Keys)
            {
                if (stand != u.Pos && !CanStand(stand)) continue;
                int r = RangeOf(u, stand);
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int d = Mathf.Abs(dx) + Mathf.Abs(dy);
                        var p = stand + new Vector2Int(dx, dy);
                        if (d >= 1 && d <= r && In(p)) tiles.Add(p);
                    }
            }
            return tiles;
        }

        public Hit Damage(Entity source, Entity target, int dmg)
        {
            dmg = Mathf.Min(Soak(target, dmg), target.Hp);
            target.Hp -= dmg;
            var hit = new Hit { Source = source, Target = target, Amount = dmg };
            if (target.Hp <= 0) Kill(target, hit);
            return hit;
        }

        public Hit HealUnit(Entity source, Unit target, int amount)
        {
            amount = Mathf.Min(amount, target.MaxHp - target.Hp);
            target.Hp += amount;
            return new Hit { Source = source, Target = target, Amount = amount, Heal = true };
        }

        void Kill(Entity e, Hit hit)
        {
            hit.Killed = true;
            e.Dead = true;
            var t = T(e.Pos);
            if (e is Unit u)
            {
                if (t.Unit == u) t.Unit = null;
                Units.Remove(u);
            }
            else if (e is Building b)
            {
                if (t.Building == b) t.Building = null;
                Buildings.Remove(b);
                if (b.Type == BuildingType.Keep) Winner = Other(b.Faction);
            }
            UpdateFog();
        }

        // ---------------------------------------------------------------- abilities

        /// <summary>The squad's ability, if its side knows it (some need research first).</summary>
        public AbilityDef AbilityOf(Unit u)
        {
            var a = Defs.AbilityOf(u.Type);
            return a != null && (a.Unlock == null || Has(u.Faction, a.Unlock.Value)) ? a : null;
        }

        public int AbilityPower(Unit u, AbilityDef a) =>
            a.Type == AbilityType.Mend && Has(u.Faction, TechType.Arcana) ? a.Power + 2 : a.Power;

        public bool AbilityReady(Unit u)
        {
            var a = AbilityOf(u);
            if (a == null || u.Dead || u.Fresh || u.Cooldown > 0) return false;
            return a.Target == AbilityTarget.Move ? CanMove(u) : !u.Acted;
        }

        public bool CanUseAbility(Unit u) => AbilityReady(u) && AbilityTargets(u).Count > 0;

        /// <summary>Tiles the squad's ability can be aimed at right now. Self abilities return the squad's own tile.</summary>
        public List<Vector2Int> AbilityTargets(Unit u)
        {
            var list = new List<Vector2Int>();
            var a = AbilityOf(u);
            if (a == null) return list;
            var f = u.Faction;
            int range = a.Range > 0 ? a.Range : RangeOf(u, u.Pos);
            switch (a.Type)
            {
                case AbilityType.Whirlwind:
                    if (Dirs4.Any(d => In(u.Pos + d) && T(u.Pos + d).Occupant is Entity e && e.Faction != f && SeenBy(f, e.Pos)))
                        list.Add(u.Pos);
                    break;
                case AbilityType.ShieldWall:
                    list.Add(u.Pos);
                    break;
                case AbilityType.Mend:
                    foreach (var ally in Units)
                        if (!ally.Dead && ally.Faction == f && ally.Hp < ally.MaxHp && Dist(ally.Pos, u.Pos) <= range) list.Add(ally.Pos);
                    break;
                case AbilityType.ShieldBash:
                case AbilityType.PinningShot:
                    foreach (var e in Units)
                        if (!e.Dead && e.Faction != f && Dist(e.Pos, u.Pos) <= range && SeenBy(f, e.Pos)) list.Add(e.Pos);
                    break;
                case AbilityType.PiercingShot:
                case AbilityType.HurlBoulder:
                    foreach (var e in Entities(Other(f)))
                        if (Dist(e.Pos, u.Pos) <= range && SeenBy(f, e.Pos)) list.Add(e.Pos);
                    break;
                case AbilityType.Firestorm:
                    foreach (var t in AllTiles())
                        if (Dist(t.Pos, u.Pos) <= range && Dist(t.Pos, u.Pos) >= 1 && SeenBy(f, t.Pos) && !t.Water && !t.Rock) list.Add(t.Pos);
                    break;
                case AbilityType.Overgrowth:
                    foreach (var t in AllTiles())
                        if (Dist(t.Pos, u.Pos) <= range && t.Terrain == Terrain.Plain && t.Occupant == null && t.Poi == PoiType.None &&
                            !t.Road && t.Gold == 0 && SeenBy(f, t.Pos)) list.Add(t.Pos);
                    break;
                case AbilityType.Shadowstep:
                    foreach (var t in AllTiles())
                        if (Dist(t.Pos, u.Pos) <= range && t.Pos != u.Pos && CanStand(t.Pos)) list.Add(t.Pos);
                    break;
            }
            return list;
        }

        public List<Hit> UseAbility(Unit u, Vector2Int p)
        {
            var a = AbilityOf(u);
            var hits = new List<Hit>();
            var f = u.Faction;
            var t = T(p);
            int power = AbilityPower(u, a);
            u.Cooldown = a.Cooldown;
            switch (a.Type)
            {
                case AbilityType.Mend:
                    hits.Add(HealUnit(u, t.Unit, power));
                    break;
                case AbilityType.Whirlwind:
                    foreach (var d in Dirs4)
                        if (In(u.Pos + d) && T(u.Pos + d).Occupant is Entity e && e.Faction != f)
                            hits.Add(Damage(u, e, Blow(AttackOf(u, false, e), DefenseOf(e))));
                    break;
                case AbilityType.ShieldWall:
                    u.GuardTurns = 1;
                    hits.Add(new Hit { Target = u, Label = $"+{Defs.GuardDefense} DEF" });
                    foreach (var d in Dirs4)
                        if (In(u.Pos + d) && T(u.Pos + d).Unit is Unit ally && ally.Faction == f)
                        {
                            ally.GuardTurns = 1;
                            hits.Add(new Hit { Target = ally, Label = $"+{Defs.GuardDefense} DEF" });
                        }
                    break;
                case AbilityType.ShieldBash:
                {
                    var victim = t.Unit;
                    var dir = victim.Pos - u.Pos;
                    var to = victim.Pos + dir;
                    int dmg = Blow(AttackOf(u, false, victim), DefenseOf(victim));
                    bool shove = (victim.Flying ? In(to) && CanStand(to) : CanStand(to));
                    if (!shove) dmg += 1;
                    hits.Add(Damage(u, victim, dmg));
                    if (!victim.Dead && shove)
                    {
                        T(victim.Pos).Unit = null;
                        victim.Pos = to;
                        T(to).Unit = victim;
                        Capture(victim);
                        hits.Add(new Hit { Target = victim, Label = "Shoved!" });
                        UpdateFog();
                    }
                    break;
                }
                case AbilityType.PinningShot:
                {
                    var victim = t.Unit;
                    hits.Add(Damage(u, victim, Blow(AttackOf(u, false, victim), DefenseOf(victim))));
                    if (!victim.Dead)
                    {
                        victim.Rooted = true;
                        hits.Add(new Hit { Target = victim, Label = "Pinned!" });
                    }
                    break;
                }
                case AbilityType.PiercingShot:
                    hits.Add(Damage(u, t.Occupant, AttackOf(u, false, t.Occupant)));
                    break;
                case AbilityType.HurlBoulder:
                    hits.Add(Damage(u, t.Occupant, power));
                    break;
                case AbilityType.Firestorm:
                    if (t.Occupant != null && t.Occupant.Faction != f) hits.Add(Damage(u, t.Occupant, power));
                    foreach (var d in Dirs8)
                    {
                        var n = p + d;
                        if (In(n) && T(n).Occupant is Entity e && e.Faction != f && !e.Dead) hits.Add(Damage(u, e, 1));
                    }
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            var n = p + new Vector2Int(dx, dy);
                            if (In(n) && T(n).Forest) T(n).Terrain = Terrain.Plain;
                        }
                    TerrainVersion++;
                    UpdateFog();
                    break;
                case AbilityType.Overgrowth:
                    t.Terrain = Terrain.Forest;
                    TerrainVersion++;
                    hits.Add(new Hit { Target = u, Label = "Overgrowth" });
                    UpdateFog();
                    break;
                case AbilityType.Shadowstep:
                    T(u.Pos).Unit = null;
                    u.Pos = p;
                    t.Unit = u;
                    int gold = t.Gold;
                    if (gold > 0)
                    {
                        Of(f).Gold += gold;
                        t.Gold = 0;
                    }
                    Capture(u);
                    u.Moved = true;
                    UpdateFog();
                    hits.Add(new Hit { Target = u, Label = gold > 0 ? $"+{gold} gold" : "Shadowstep" });
                    return hits;
            }
            u.Acted = true;
            u.Moved = true;
            return hits;
        }

        // ---------------------------------------------------------------- spells

        public List<Vector2Int> SpellTargets(Faction f, SpellType s)
        {
            var list = new List<Vector2Int>();
            foreach (var t in AllTiles())
            {
                bool seen = SeenBy(f, t.Pos);
                switch (s)
                {
                    case SpellType.Meteor:
                        if (seen && !t.Rock && !t.Water) list.Add(t.Pos);
                        break;
                    case SpellType.Ward:
                    case SpellType.Haste:
                        if (t.Unit != null && t.Unit.Faction == f) list.Add(t.Pos);
                        break;
                    case SpellType.Bolt:
                        if (seen && t.Occupant != null && t.Occupant.Faction != f) list.Add(t.Pos);
                        break;
                    case SpellType.Renew:
                    case SpellType.Rally:
                        if (t.Unit != null && t.Unit.Faction == f) list.Add(t.Pos);
                        break;
                    case SpellType.Stonewall:
                        if (seen && Buildable(t)) list.Add(t.Pos);
                        break;
                    case SpellType.Transmute:
                        if (t.Building == Of(f).Keep) list.Add(t.Pos);
                        break;
                }
            }
            return list;
        }

        public List<Hit> Cast(Faction f, SpellType s, Vector2Int p)
        {
            var hits = new List<Hit>();
            Of(f).Mana -= Defs.Spell(s).Cost;
            var t = T(p);
            switch (s)
            {
                case SpellType.Meteor:
                    if (t.Occupant != null && t.Occupant.Faction != f) hits.Add(Damage(null, t.Occupant, Defs.Spell(s).Power));
                    foreach (var d in Dirs8)
                    {
                        var n = p + d;
                        if (In(n) && T(n).Occupant is Entity e && e.Faction != f) hits.Add(Damage(null, e, 1));
                    }
                    break;
                case SpellType.Bolt:
                    if (t.Occupant != null) hits.Add(Damage(null, t.Occupant, Defs.Spell(s).Power));
                    break;
                case SpellType.Ward:
                    t.Unit.WardTurns = 1;
                    hits.Add(new Hit { Target = t.Unit, Label = $"+{Defs.Spell(s).Power} DEF" });
                    break;
                case SpellType.Haste:
                    var u = t.Unit;
                    u.Moved = u.Acted = u.Fresh = false;
                    u.Hasted = true;
                    hits.Add(new Hit { Target = u, Label = "Haste!" });
                    break;
                case SpellType.Renew:
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            var n = p + new Vector2Int(dx, dy);
                            if (In(n) && T(n).Unit is Unit ally && ally.Faction == f && ally.Hp < ally.MaxHp)
                                hits.Add(HealUnit(null, ally, Defs.Spell(s).Power));
                        }
                    break;
                case SpellType.Rally:
                    t.Unit.Rallied = true;
                    hits.Add(new Hit { Target = t.Unit, Label = $"+{Defs.Spell(s).Power} AD" });
                    break;
                case SpellType.Stonewall:
                    var wall = AddBuilding(BuildingType.Wall, f, p, true);
                    hits.Add(new Hit { Target = wall, Label = "Wall!" });
                    UpdateFog();
                    break;
                case SpellType.Transmute:
                    Of(f).Gold += Defs.Spell(s).Power;
                    hits.Add(new Hit { Target = Of(f).Keep, Label = $"+{Defs.Spell(s).Power} gold" });
                    break;
            }
            return hits;
        }

        // ---------------------------------------------------------------- recruit and build

        public List<Vector2Int> RecruitTiles(Faction f)
        {
            var set = new HashSet<Vector2Int>();
            var hubs = Buildings.Where(b => b.Faction == f && !b.Dead && b.Complete &&
                                            (b.Type == BuildingType.Keep || b.Type == BuildingType.Barracks)).Select(b => b.Pos)
                .Concat(AllTiles().Where(t => t.Poi == PoiType.Outpost && t.Owner == f).Select(t => t.Pos));
            foreach (var hub in hubs)
                foreach (var d in Dirs8)
                {
                    var n = hub + d;
                    if (CanStand(n)) set.Add(n);
                }
            return set.ToList();
        }

        /// <summary>Empty open ground: no water, rock, forest, building, squad, gold or point of interest.</summary>
        public static bool Buildable(Tile t) =>
            !t.Blocked && !t.Forest && t.Unit == null && t.Gold == 0 && t.Poi == PoiType.None;

        /// <summary>Buildings can go anywhere the side can currently see.</summary>
        public List<Vector2Int> BuildTiles(Faction f) =>
            AllTiles().Where(t => Buildable(t) && SeenBy(f, t.Pos)).Select(t => t.Pos).ToList();

        public int CostOf(UnitType t) => Defs.Unit(t).Cost;

        public string RecruitProblem(Faction f, UnitType t)
        {
            var s = Of(f);
            if (SlotsLeft(f) <= 0) return "No recruit slots left this turn";
            if (t == UnitType.Minions && s.Reserve <= 0) return "No minions in reserve";
            if (s.Gold < CostOf(t)) return $"Need {CostOf(t)} gold";
            if (RecruitTiles(f).Count == 0) return "No free tile next to your Keep";
            return null;
        }

        public string BuildProblem(Faction f, BuildingType t)
        {
            if (SlotsLeft(f) <= 0) return "No slots left this turn";
            if (Of(f).Gold < Defs.Building(t).Cost) return $"Need {Defs.Building(t).Cost} gold";
            if (BuildTiles(f).Count == 0) return "No free tile in sight to build on";
            return null;
        }

        /// <param name="minions">Squad size when recruiting Minions: 1 or 4 (capped by the reserve).</param>
        public Unit Recruit(Faction f, UnitType t, Vector2Int pos, int minions = Defs.MinionSquad)
        {
            var s = Of(f);
            s.Gold -= CostOf(t);
            s.SlotsUsed++;
            int count = 0;
            if (t == UnitType.Minions)
            {
                count = Mathf.Clamp(minions, 1, s.Reserve);
                s.Reserve -= count;
            }
            var u = Spawn(t, f, pos, count);
            u.Fresh = true;
            UpdateFog();
            return u;
        }

        public Building Build(Faction f, BuildingType t, Vector2Int pos)
        {
            var s = Of(f);
            s.Gold -= Defs.Building(t).Cost;
            s.SlotsUsed++;
            var b = AddBuilding(t, f, pos, false);
            UpdateFog();
            return b;
        }

        // ---------------------------------------------------------------- research

        public bool Researching(Faction f, TechType t) =>
            Buildings.Any(b => b.Faction == f && !b.Dead && b.Research == t);

        /// <summary>Why a research building can't start this tech right now (null = it can).</summary>
        public string ResearchProblem(Building b, TechType t)
        {
            var def = Defs.Tech(t);
            var f = b.Faction;
            if (!TechPool.Contains(t)) return "Not available in this battle";
            if (Has(f, t)) return "Researched";
            if (Researching(f, t)) return "Under way";
            if (def.Building != b.Type) return $"Needs a {Defs.Building(def.Building).Name}";
            if (!b.Complete) return "Still being built";
            if (def.Requires.HasValue && !Has(f, def.Requires.Value)) return "Needs " + Defs.Tech(def.Requires.Value).Name;
            if (b.Research.HasValue) return "Busy with " + Defs.Tech(b.Research.Value).Name;
            if (Of(f).Research < def.Cost) return $"Need {def.Cost} research";
            return null;
        }

        public void StartResearch(Building b, TechType t)
        {
            Of(b.Faction).Research -= Defs.Tech(t).Cost;
            b.Research = t;
            b.ResearchLeft = Defs.Tech(t).Turns;
        }

        void Learn(Faction f, TechType t)
        {
            Of(f).Techs.Add(t);
            if (t == TechType.Padding)
                foreach (var u in Units.Where(u => u.Faction == f && !u.IsMinions))
                {
                    u.MaxHp += 2;
                    u.Hp += 2;
                }
        }

        public int ResearchIncome(Faction f) =>
            Defs.KeepResearch + Defs.BuildingResearch * Buildings.Count(b => b.Faction == f && b.Complete && b.Def.Researches) +
            Defs.LibraryResearch * CountPoi(f, PoiType.Library);

        // ---------------------------------------------------------------- turns

        /// <summary>Start-of-turn upkeep for one side. Returns heals, tower shots and finished research for the view to show.</summary>
        public List<Hit> BeginTurn(Faction f)
        {
            Current = f;
            var hits = new List<Hit>();
            var s = Of(f);
            s.SlotsUsed = 0;

            foreach (var b in Buildings.ToList())
                if (b.Faction == f && b.TurnsLeft > 0) b.TurnsLeft--;

            int wells = Buildings.Count(b => b.Faction == f && b.Complete && b.Type == BuildingType.ManaWell);
            int mana = Defs.ManaPerTurn + wells + Defs.ShrineMana * CountPoi(f, PoiType.Shrine) + (Has(f, TechType.Arcana) ? 1 : 0);
            s.Gold += Mathf.Max(0, Defs.KeepIncome + s.IncomeBonus) + Defs.MineGold * CountPoi(f, PoiType.GoldMine);
            s.Mana = Mathf.Min(Defs.MaxMana, s.Mana + mana);
            s.Reserve = Mathf.Min(Defs.MinionReserve, s.Reserve + 1);
            s.Research += ResearchIncome(f);

            // Libraries double research speed.
            int progress = CountPoi(f, PoiType.Library) > 0 ? 2 : 1;
            foreach (var b in Buildings.Where(b => b.Faction == f && b.Research.HasValue).ToList())
            {
                b.ResearchLeft -= progress;
                if (b.ResearchLeft > 0) continue;
                var t = b.Research.Value;
                b.Research = null;
                Learn(f, t);
                hits.Add(new Hit { Target = b, Label = Defs.Tech(t).Name + "!" });
            }

            foreach (var u in Units)
            {
                if (u.Faction != f)
                {
                    // The other side's turn is over, so its pins wear off.
                    u.Rooted = false;
                    continue;
                }
                u.Moved = u.Acted = u.Fresh = u.Hasted = u.Rallied = false;
                if (u.WardTurns > 0) u.WardTurns--;
                if (u.GuardTurns > 0) u.GuardTurns--;
                if (u.Cooldown > 0) u.Cooldown--;
            }

            // Auras don't stack: each ally gets healed once, however many healers stand next to it.
            int aura = Has(f, TechType.Arcana) ? 2 : 1;
            var soothed = new HashSet<Unit>();
            foreach (var healer in Units.Where(u => u.Faction == f && u.Type == UnitType.Healer).ToList())
                foreach (var d in Dirs4)
                {
                    var n = healer.Pos + d;
                    if (In(n) && T(n).Unit is Unit ally && ally.Faction == f && ally.Hp < ally.MaxHp && soothed.Add(ally))
                        hits.Add(HealUnit(healer, ally, aura));
                }
            foreach (var u in Units.Where(u => u.Faction == f && u.Hp < u.MaxHp && T(u.Pos).Poi == PoiType.Spring).ToList())
                hits.Add(HealUnit(null, u, Defs.SpringHeal));

            foreach (var tower in Buildings.Where(b => b.Faction == f && b.Complete && b.Type == BuildingType.Watchtower).ToList())
            {
                var target = Entities(Other(f)).Where(e => Cheb(e.Pos, tower.Pos) <= 2).OrderBy(e => e.Hp).FirstOrDefault();
                if (target != null) hits.Add(Damage(tower, target, 1));
            }

            UpdateFog();
            return hits;
        }

        // ---------------------------------------------------------------- fog of war

        /// <summary>
        /// Recomputes what each side can see. Both sides use the same rules: a sight radius around every squad, building
        /// and held Outpost; squads in a forest are only seen from next door. The player's view also marks tiles explored.
        /// </summary>
        public void UpdateFog()
        {
            for (int i = 0; i < 2; i++)
            {
                var grid = seen[i];
                Array.Clear(grid, 0, grid.Length);
                var f = (Faction)i;
                foreach (var (pos, r) in Eyes(f))
                    for (int dx = -r; dx <= r; dx++)
                        for (int dy = -r; dy <= r; dy++)
                        {
                            if (dx * dx + dy * dy > r * r + r) continue;
                            var p = pos + new Vector2Int(dx, dy);
                            if (!In(p)) continue;
                            if (f == Faction.Player) T(p).Explored = true;
                            if (T(p).Forest && Cheb(p, pos) > 1) continue;
                            grid[p.x, p.y] = true;
                        }
            }
            foreach (var t in Tiles) t.Visible = seen[0][t.Pos.x, t.Pos.y];
        }

        IEnumerable<(Vector2Int, int)> Eyes(Faction f)
        {
            foreach (var e in Entities(f)) yield return (e.Pos, VisionOf(e));
            foreach (var t in Tiles)
                if (t.Poi == PoiType.Outpost && t.Owner == f) yield return (t.Pos, Defs.OutpostVision);
        }

        int VisionOf(Entity e) => e is Unit u ? u.Def.Vision + (T(u.Pos).Height > 0 ? 1 : 0)
            : ((Building)e).Complete ? ((Building)e).Def.Vision : 1;

        /// <summary>Whether a side can currently see a tile (its spells and attacks need sight).</summary>
        public bool SeenBy(Faction f, Vector2Int p) => seen[(int)f][p.x, p.y];
    }
}
