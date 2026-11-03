using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Strategy
{
    /// <summary>
    /// Decisions shared by the in-game <see cref="EnemyAI"/> and the editor's balance simulator: where squads march,
    /// when to use abilities, what to build and what to research. Pure functions over the model; callers carry them out.
    /// </summary>
    public static class AiTactics
    {
        /// <summary>Where a squad should head: home if the Keep is threatened, otherwise enemies and unclaimed sites.</summary>
        public static List<Vector2Int> Goals(GameModel m, Unit u)
        {
            var me = u.Faction;
            var foe = GameModel.Other(me);
            var keep = m.Of(me).Keep.Pos;
            if (u.Type == UnitType.Healer)
            {
                var allies = m.Units.Where(a => a.Faction == me && a != u && a.Type != UnitType.Healer).Select(a => a.Pos).ToList();
                if (allies.Count > 0) return allies;
            }
            var threats = m.Entities(foe).Where(e => GameModel.Dist(e.Pos, keep) <= 4).Select(e => e.Pos).ToList();
            if (threats.Count > 0) return threats;
            var goals = m.Entities(foe).Select(e => e.Pos).ToList();
            goals.AddRange(m.AllTiles().Where(t => t.Poi != PoiType.None && t.Poi != PoiType.Spring && t.Owner != me && t.Unit == null)
                .Select(t => t.Pos));
            return goals;
        }

        /// <summary>Best tile to march to this turn: the cheapest walk towards the nearest goal.</summary>
        public static Vector2Int? March(GameModel m, Unit u, List<Vector2Int> dests)
        {
            var field = m.CostField(Goals(m, u), u.Flying);
            int At(Vector2Int p) => field.TryGetValue(p, out var v) ? v : 9999;
            int best = At(u.Pos);
            Vector2Int? go = null;
            foreach (var d in dests)
            {
                int f = At(d) - (m.T(d).Gold > 0 ? 6 : 0) - (m.T(d).Poi != PoiType.None && m.T(d).Owner != u.Faction ? 6 : 0);
                if (f < best)
                {
                    best = f;
                    go = d;
                }
            }
            return go;
        }

        /// <summary>How much an attack on this target is worth; kills and the enemy Keep score highest.</summary>
        public static float Score(GameModel m, Unit u, Entity t, Vector2Int from)
        {
            int strikes = u.Type == UnitType.Twinblade ? 2 : 1;
            int dmg = GameModel.Soak(t, GameModel.Blow(m.AttackOf(u, false), m.DefenseOf(t))) * strikes;
            float s = dmg * 3f - t.Hp * 0.5f;
            if (dmg >= t.Hp) s += 40f;
            if (t is Building b && b.Type == BuildingType.Keep) s += 25f;
            if (u.Type != UnitType.Ninja && t is Unit tu && GameModel.Dist(from, t.Pos) <= m.RangeOf(tu, t.Pos) && dmg < t.Hp)
                s -= m.AttackOf(tu, true) * 0.8f;
            // Ranged squads like to stand back, and better still on a hill or in a wood.
            if (u.Def.Range > 1) s += GameModel.Dist(from, t.Pos) + m.T(from).Height * 1.5f + (m.T(from).Forest ? 1f : 0f);
            return s;
        }

        /// <summary>
        /// Whether to use the squad's ability now, and where. Null means "not worth it"; Shadowstep is planned separately
        /// by <see cref="ShadowstepPlan"/>.
        /// </summary>
        public static Vector2Int? AbilityPlan(GameModel m, Unit u)
        {
            if (!m.AbilityReady(u)) return null;
            var a = m.AbilityOf(u);
            var targets = m.AbilityTargets(u);
            if (targets.Count == 0) return null;
            var me = u.Faction;
            int power = m.AbilityPower(u, a);
            switch (a.Type)
            {
                case AbilityType.Mend:
                {
                    var hurt = targets.Select(p => m.T(p).Unit).OrderByDescending(x => x.MaxHp - x.Hp).First();
                    return hurt.MaxHp - hurt.Hp >= 2 ? hurt.Pos : (Vector2Int?)null;
                }
                case AbilityType.Whirlwind:
                {
                    var foes = GameModel.Dirs4.Select(d => u.Pos + d).Where(p => m.In(p) && m.T(p).Occupant is Entity e && e.Faction != me)
                        .Select(p => m.T(p).Occupant).ToList();
                    bool kill = foes.Any(e => GameModel.Soak(e, GameModel.Blow(m.AttackOf(u, false, e), m.DefenseOf(e))) >= e.Hp);
                    return foes.Count >= 2 || kill ? u.Pos : (Vector2Int?)null;
                }
                case AbilityType.ShieldWall:
                {
                    bool close = m.Units.Any(e => e.Faction != me && GameModel.Dist(e.Pos, u.Pos) <= 3);
                    return close ? u.Pos : (Vector2Int?)null;
                }
                case AbilityType.ShieldBash:
                case AbilityType.PinningShot:
                case AbilityType.PiercingShot:
                case AbilityType.HurlBoulder:
                {
                    Vector2Int? best = null;
                    float bestS = float.MinValue;
                    foreach (var p in targets)
                    {
                        var t = m.T(p).Occupant;
                        int dmg = a.Type == AbilityType.HurlBoulder ? power
                            : a.Type == AbilityType.PiercingShot ? m.AttackOf(u, false, t)
                            : GameModel.Blow(m.AttackOf(u, false, t), m.DefenseOf(t)) + (a.Type == AbilityType.ShieldBash ? 1 : 0);
                        dmg = GameModel.Soak(t, dmg);
                        float s = dmg * 3f - t.Hp * 0.5f + (dmg >= t.Hp ? 40f : 0f) + (t is Building b && b.Type == BuildingType.Keep ? 25f : 0f);
                        if (s > bestS)
                        {
                            bestS = s;
                            best = p;
                        }
                    }
                    // A Giant would rather club whatever is next to it unless the boulder kills.
                    if (a.Type == AbilityType.HurlBoulder && best.HasValue && m.TargetsFrom(u, u.Pos).Count > 0 && bestS < 40f) return null;
                    return best;
                }
                case AbilityType.Firestorm:
                {
                    Vector2Int? best = null;
                    int bestS = 2;
                    foreach (var p in targets)
                    {
                        int s = m.T(p).Occupant is Entity c && c.Faction != me ? power * 2 : 0;
                        foreach (var d in GameModel.Dirs8)
                            if (m.In(p + d) && m.T(p + d).Occupant is Entity e && e.Faction != me) s += 2;
                        if (s > bestS)
                        {
                            bestS = s;
                            best = p;
                        }
                    }
                    return best;
                }
                case AbilityType.Overgrowth:
                {
                    // Grow cover between the druid and the nearest enemy.
                    var foe = m.Units.Where(e => e.Faction != me).OrderBy(e => GameModel.Dist(e.Pos, u.Pos)).FirstOrDefault();
                    if (foe == null || GameModel.Dist(foe.Pos, u.Pos) > 6) return null;
                    return targets.OrderBy(p => GameModel.Dist(p, foe.Pos) + GameModel.Dist(p, u.Pos)).First();
                }
            }
            return null;
        }

        /// <summary>For a squad with Shadowstep: a tile to blink to that lets it strike something worth hitting.</summary>
        public static (Vector2Int to, Entity target)? ShadowstepPlan(GameModel m, Unit u)
        {
            var a = m.AbilityOf(u);
            if (a == null || a.Type != AbilityType.Shadowstep || !m.AbilityReady(u)) return null;
            (Vector2Int, Entity)? best = null;
            float bestS = 10f;
            foreach (var p in m.AbilityTargets(u))
                foreach (var e in m.Entities(GameModel.Other(u.Faction)))
                {
                    if (GameModel.Dist(p, e.Pos) != 1 || !m.SeenBy(u.Faction, e.Pos)) continue;
                    float s = Score(m, u, e, p);
                    if (s > bestS)
                    {
                        bestS = s;
                        best = (p, e);
                    }
                }
            return best;
        }

        /// <summary>One building a turn: a Forge early, then a Mana Well, an Academy and a Barracks as gold allows.</summary>
        public static BuildingType? PickBuild(GameModel m, Faction me, bool wantsMana)
        {
            bool Has(BuildingType t) => m.Buildings.Any(b => b.Faction == me && b.Type == t);
            bool Pool(BuildingType t) => m.TechPool.Any(x => Defs.Tech(x).Building == t);
            int gold = m.Of(me).Gold;
            if (m.Turn >= 2 && !Has(BuildingType.Forge) && Pool(BuildingType.Forge) && gold >= 9) return BuildingType.Forge;
            if (m.Turn >= 3 && wantsMana && !Has(BuildingType.ManaWell) && gold >= 9) return BuildingType.ManaWell;
            if (m.Turn >= 5 && !Has(BuildingType.Academy) && Pool(BuildingType.Academy) && gold >= 9) return BuildingType.Academy;
            if (m.Turn >= 6 && !Has(BuildingType.Barracks) && gold >= 12) return BuildingType.Barracks;
            return null;
        }

        /// <summary>Close to home and on the side away from the enemy.</summary>
        public static Vector2Int? BuildSpot(GameModel m, Faction me)
        {
            var keep = m.Of(me).Keep.Pos;
            var foe = m.Of(GameModel.Other(me)).Keep.Pos;
            var tiles = m.BuildTiles(me);
            if (tiles.Count == 0) return null;
            return tiles.OrderBy(p => Mathf.Max(0, GameModel.Cheb(p, keep) - 2) * 10 - GameModel.Dist(p, foe)).First();
        }

        /// <summary>Research for an idle Forge or Academy, favouring upgrades for squads the side actually fields.</summary>
        public static TechType? PickResearch(GameModel m, Building b, System.Random rng)
        {
            var me = b.Faction;
            var army = new HashSet<UnitType>(m.Units.Where(u => u.Faction == me).Select(u => u.Type));
            float Weight(TechType t)
            {
                switch (t)
                {
                    case TechType.Blades: return army.Any(x => Defs.Unit(x).Melee && x != UnitType.Minions) ? 3f : 0.5f;
                    case TechType.Fletching: return army.Any(x => !Defs.Unit(x).Melee) ? 3f : 0.3f;
                    case TechType.Longbows: return army.Contains(UnitType.Bow) || army.Contains(UnitType.Crossbow) ? 2f : 0.2f;
                    case TechType.WarHorses: return army.Contains(UnitType.Knight) || army.Contains(UnitType.Rider) ? 2f : 0.2f;
                    case TechType.ShieldWall: return army.Contains(UnitType.Shield) ? 2f : 0.5f;
                    case TechType.PinningShot: return army.Contains(UnitType.Bow) ? 2f : 0.5f;
                    case TechType.PiercingShot: return army.Contains(UnitType.Crossbow) ? 2f : 0.2f;
                    case TechType.Firestorm: return army.Contains(UnitType.Pyromancer) ? 2f : 0.2f;
                    default: return 2f;
                }
            }
            var ok = Defs.AllTechs.Where(t => m.ResearchProblem(b, t) == null).ToList();
            if (ok.Count == 0) return null;
            float r = (float)rng.NextDouble() * ok.Sum(Weight);
            foreach (var t in ok)
            {
                r -= Weight(t);
                if (r <= 0) return t;
            }
            return ok[ok.Count - 1];
        }
    }
}
