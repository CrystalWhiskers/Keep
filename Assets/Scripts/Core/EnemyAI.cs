using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Strategy
{
    /// <summary>
    /// Simple greedy opponent: finish off weak targets with spells, spend gold and research every turn,
    /// then each squad uses its ability, attacks the best target it can reach, or marches on the nearest
    /// threat or unclaimed site. It knows where everything is, which keeps it from wandering, but it can
    /// only strike what it can see. The decisions themselves live in <see cref="AiTactics"/>.
    /// </summary>
    public class EnemyAI
    {
        const Faction Me = Faction.Enemy;
        readonly GameModel m;
        /// <summary>Squad types this opponent may recruit (null = all). Campaign battles restrict it.</summary>
        readonly HashSet<UnitType> allowed;

        public EnemyAI(GameModel model, IEnumerable<UnitType> allowedUnits = null)
        {
            m = model;
            if (allowedUnits != null) allowed = new HashSet<UnitType>(allowedUnits);
        }

        public IEnumerator Run(GameManager gm)
        {
            yield return new WaitForSeconds(0.25f);
            yield return Spells(gm);
            yield return Economy(gm);

            var playerKeep = m.Of(Faction.Player).Keep.Pos;
            var squads = m.Units.Where(u => u.Faction == Me && !u.Fresh)
                .OrderBy(u => GameModel.Dist(u.Pos, playerKeep)).ToList();
            foreach (var u in squads)
            {
                if (u.Dead || m.Winner != null) continue;
                yield return Act(gm, u);
            }
            if (m.Winner == null) yield return Spells(gm);
            yield return new WaitForSeconds(0.15f);
        }

        // ---------------------------------------------------------------- spells

        IEnumerator Spells(GameManager gm)
        {
            var side = m.Of(Me);
            var keep = side.Keep.Pos;
            var foes = m.Entities(Faction.Player).Where(e => m.SeenBy(Me, e.Pos)).ToList();

            if (side.Mana >= Defs.Spell(SpellType.Bolt).Cost)
            {
                var target = foes.Where(e => e.Hp <= 3).OrderByDescending(Value).FirstOrDefault()
                             ?? foes.Where(e => e is Unit && GameModel.Dist(e.Pos, keep) <= 3).OrderBy(e => e.Hp).FirstOrDefault();
                if (target != null) yield return gm.AiCast(SpellType.Bolt, target.Pos);
            }

            if (side.Mana >= Defs.Spell(SpellType.Meteor).Cost)
            {
                Vector2Int? best = null;
                int bestScore = 3;
                foreach (var e in foes)
                {
                    int score = 3;
                    foreach (var d in GameModel.Dirs8)
                    {
                        var n = e.Pos + d;
                        if (m.In(n) && m.T(n).Occupant is Entity o && o.Faction == Faction.Player) score += 2;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = e.Pos;
                    }
                }
                if (best.HasValue) yield return gm.AiCast(SpellType.Meteor, best.Value);
            }

            if (side.Mana >= 7)
            {
                var frontline = m.Units.Where(u => u.Faction == Me && u.WardTurns == 0 &&
                    m.Entities(Faction.Player).Any(e => GameModel.Dist(e.Pos, u.Pos) <= 2)).OrderByDescending(u => u.Hp).FirstOrDefault();
                if (frontline != null) yield return gm.AiCast(SpellType.Ward, frontline.Pos);
            }
        }

        // ---------------------------------------------------------------- gold and research

        IEnumerator Economy(GameManager gm)
        {
            var side = m.Of(Me);
            var playerKeep = m.Of(Faction.Player).Keep.Pos;

            var build = AiTactics.PickBuild(m, Me, true);
            if (build.HasValue && m.BuildProblem(Me, build.Value) == null)
            {
                var tile = AiTactics.BuildSpot(m, Me);
                if (tile.HasValue) yield return gm.AiBuild(build.Value, tile.Value);
            }

            foreach (var lab in m.Buildings.Where(b => b.Faction == Me && b.Def.Researches && b.Complete && !b.Research.HasValue).ToList())
            {
                var tech = AiTactics.PickResearch(m, lab, m.Rng);
                if (tech.HasValue) m.StartResearch(lab, tech.Value);
            }

            for (int guard = 0; guard < 6 && m.SlotsLeft(Me) > 0; guard++)
            {
                var type = PickUnit(side);
                if (type == null) break;
                var tiles = m.RecruitTiles(Me);
                if (tiles.Count == 0) break;
                var tile = tiles.OrderBy(p => GameModel.Dist(p, playerKeep)).First();
                yield return gm.AiRecruit(type.Value, tile);
            }
        }

        /// <summary>Recruit weights for every squad type (the balance simulator uses the same table).</summary>
        public static readonly Dictionary<UnitType, float> Weights = new Dictionary<UnitType, float>
        {
            [UnitType.Knight] = 3f, [UnitType.Sword] = 4f, [UnitType.Bow] = 3f, [UnitType.Spear] = 1.5f,
            [UnitType.Shield] = 1.5f, [UnitType.Twinblade] = 3f, [UnitType.Healer] = 1.2f, [UnitType.Minions] = 2f,
            [UnitType.Crossbow] = 2f, [UnitType.Rider] = 2.5f, [UnitType.Pyromancer] = 1.5f,
            [UnitType.Ninja] = 1.5f, [UnitType.Giant] = 1.2f, [UnitType.Druid] = 1f, [UnitType.Wyvern] = 1.5f,
        };

        UnitType? PickUnit(Side side)
        {
            var options = Weights.Select(kv => (t: kv.Key, w: kv.Value)).ToList();
            if (allowed != null) options.RemoveAll(o => !allowed.Contains(o.t));
            // Wait for a decent squad of minions rather than spending a slot on one or two.
            var ok = options.Where(o => m.RecruitProblem(Me, o.t) == null && (o.t != UnitType.Minions || side.Reserve >= 3)).ToList();
            if (ok.Count == 0) return null;
            float total = ok.Sum(o => o.w);
            float r = (float)m.Rng.NextDouble() * total;
            foreach (var o in ok)
            {
                r -= o.w;
                if (r <= 0) return o.t;
            }
            return ok[ok.Count - 1].t;
        }

        // ---------------------------------------------------------------- squads

        IEnumerator Act(GameManager gm, Unit u)
        {
            var plan = AiTactics.AbilityPlan(m, u);
            if (plan.HasValue)
            {
                yield return gm.AiAbility(u, plan.Value);
                yield break;
            }

            bool support = u.Type == UnitType.Healer;
            var here = m.TargetsFrom(u, u.Pos);
            if (here.Count > 0 && !support && m.CanAttack(u))
            {
                yield return gm.AiAttack(u, here.OrderByDescending(t => AiTactics.Score(m, u, t, u.Pos)).First());
                yield break;
            }
            if (!m.CanMove(u)) yield break;

            var reach = m.Reach(u, m.MoveOf(u));
            var dests = m.Destinations(u, reach);

            if (!support)
            {
                Entity bestT = null;
                Vector2Int bestD = default;
                float bestS = float.MinValue;
                foreach (var d in dests)
                    foreach (var t in m.TargetsFrom(u, d))
                    {
                        float s = AiTactics.Score(m, u, t, d) - GameModel.PathTo(reach, d).Count * 0.1f;
                        if (s > bestS)
                        {
                            bestS = s;
                            bestT = t;
                            bestD = d;
                        }
                    }
                if (bestT != null)
                {
                    yield return gm.AiMove(u, bestD, reach);
                    if (!u.Dead && !bestT.Dead) yield return gm.AiAttack(u, bestT);
                    yield break;
                }

                var step = AiTactics.ShadowstepPlan(m, u);
                if (step.HasValue)
                {
                    yield return gm.AiAbility(u, step.Value.to);
                    if (!u.Dead && !step.Value.target.Dead && m.TargetsFrom(u, u.Pos).Contains(step.Value.target))
                        yield return gm.AiAttack(u, step.Value.target);
                    yield break;
                }
            }

            var go = AiTactics.March(m, u, dests);
            if (go.HasValue) yield return gm.AiMove(u, go.Value, reach);
            if (u.Dead) yield break;
            plan = AiTactics.AbilityPlan(m, u);
            if (plan.HasValue)
            {
                yield return gm.AiAbility(u, plan.Value);
                yield break;
            }
            if (!support)
            {
                var after = m.TargetsFrom(u, u.Pos);
                if (after.Count > 0) yield return gm.AiAttack(u, after.OrderByDescending(t => AiTactics.Score(m, u, t, u.Pos)).First());
            }
        }

        float Value(Entity e) => e is Unit u ? u.Def.Cost + u.Hp : 30f;
    }
}
