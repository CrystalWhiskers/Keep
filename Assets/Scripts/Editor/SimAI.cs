using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Strategy.EditorTools
{
    /// <summary>
    /// Synchronous version of <see cref="EnemyAI"/> for <see cref="BalanceSim"/>: plays either side, takes a recruit mix
    /// and a spell list, and has a simple rule for every spell (EnemyAI only casts Bolt, Meteor and Ward).
    /// Like EnemyAI it ignores fog for movement, but its spells and attacks only reach what its side can see.
    /// Squad decisions come from <see cref="AiTactics"/>, the same as EnemyAI.
    /// </summary>
    public class SimAI
    {
        public class Cfg
        {
            /// <summary>Recruit weights; only these squad types are bought.</summary>
            public Dictionary<UnitType, float> Weights;
            public SpellType[] Spells = new SpellType[0];
            public int GoldBonus, IncomeBonus;
            public UnitType[] Start = new UnitType[0];
        }

        /// <summary>The same recruit weights as EnemyAI.</summary>
        public static Dictionary<UnitType, float> DefaultWeights => EnemyAI.Weights;


        public static Dictionary<UnitType, float> Only(IEnumerable<UnitType> types) =>
            DefaultWeights.Where(kv => types.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        readonly GameModel m;
        readonly Faction me, foe;
        readonly Cfg cfg;
        readonly System.Random rng;
        readonly BalanceSim.Tally tally;
        bool hasteUsed, wallUsed;

        public SimAI(GameModel model, Faction f, Cfg c, System.Random r, BalanceSim.Tally t)
        {
            m = model;
            me = f;
            foe = GameModel.Other(f);
            cfg = c;
            rng = r;
            tally = t;
        }

        Side side => m.Of(me);
        bool CanCast(SpellType s) => cfg.Spells.Contains(s) && side.Mana >= Defs.Spell(s).Cost;


        void Cast(SpellType s, Vector2Int p) => Record(m.Cast(me, s, p));

        void Record(List<Hit> hits)
        {
            foreach (var h in hits)
            {
                if (h.Heal || h.Label != null) continue;
                if (h.Source is Unit s) tally.Dealt(s.Type, h.Amount, h.Killed, h.Counter);
                if (h.Target is Unit tu && h.Killed) tally.Lost(tu.Type);
            }
        }

        public void Run()
        {
            hasteUsed = wallUsed = false;
            Spells(true);
            if (m.Winner != null) return;
            Economy();
            var enemyKeep = m.Of(foe).Keep.Pos;
            var squads = m.Units.Where(u => u.Faction == me && !u.Fresh).OrderBy(u => GameModel.Dist(u.Pos, enemyKeep)).ToList();
            foreach (var u in squads)
            {
                if (u.Dead || m.Winner != null) continue;
                Act(u);
                if (!u.Dead && m.Winner == null && !hasteUsed && CanCast(SpellType.Haste) && u.Type != UnitType.Healer &&
                    u.Hp * 2 > u.MaxHp && HasteKill(u))
                {
                    hasteUsed = true;
                    Cast(SpellType.Haste, u.Pos);
                    Act(u);
                }
            }
            if (m.Winner == null) Spells(false);
        }

        /// <summary>Would a second action (with +1 move) let this squad finish off an enemy?</summary>
        bool HasteKill(Unit u)
        {
            var reach = m.Reach(u, m.MoveOf(u) + 1);
            int strikes = u.Type == UnitType.Twinblade ? 2 : 1;
            foreach (var d in m.Destinations(u, reach).Append(u.Pos))
                foreach (var t in m.TargetsFrom(u, d))
                    if (GameModel.Blow(m.AttackOf(u, false, t), m.DefenseOf(t)) * strikes >= t.Hp) return true;
            return false;
        }

        /// <summary>Mana to hold back for the dearest damage spell in the loadout (other than this one).</summary>
        int Reserve(SpellType except)
        {
            int r = 0;
            foreach (var s in cfg.Spells)
                if (s != except && (s == SpellType.Bolt || s == SpellType.Meteor)) r = Mathf.Max(r, Defs.Spell(s).Cost);
            return r;
        }

        void Spells(bool start)
        {
            var keep = side.Keep.Pos;
            var foes = m.Entities(foe).Where(e => m.SeenBy(me, e.Pos)).ToList();

            if (start && CanCast(SpellType.Transmute) && side.Mana >= Defs.Spell(SpellType.Transmute).Cost + Reserve(SpellType.Transmute))
                Cast(SpellType.Transmute, keep);

            if (CanCast(SpellType.Bolt))
            {
                var target = foes.Where(e => e.Hp <= Defs.Spell(SpellType.Bolt).Power).OrderByDescending(Value).FirstOrDefault()
                             ?? foes.Where(e => e is Unit && GameModel.Dist(e.Pos, keep) <= 3).OrderBy(e => e.Hp).FirstOrDefault();
                if (target != null) Cast(SpellType.Bolt, target.Pos);
            }

            if (CanCast(SpellType.Meteor))
            {
                Vector2Int? best = null;
                int bestScore = 3;
                foreach (var e in foes)
                {
                    int score = 3;
                    foreach (var d in GameModel.Dirs8)
                    {
                        var n = e.Pos + d;
                        if (m.In(n) && m.T(n).Occupant is Entity o && o.Faction == foe) score += 2;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = e.Pos;
                    }
                }
                if (best.HasValue) Cast(SpellType.Meteor, best.Value);
            }

            if (CanCast(SpellType.Renew) && side.Mana >= Defs.Spell(SpellType.Renew).Cost + Reserve(SpellType.Renew))
            {
                Vector2Int? best = null;
                int bestHeal = 3, power = Defs.Spell(SpellType.Renew).Power;
                foreach (var u in m.Units.Where(a => a.Faction == me))
                {
                    int heal = 0;
                    foreach (var a in m.Units)
                        if (a.Faction == me && GameModel.Cheb(a.Pos, u.Pos) <= 1) heal += Mathf.Min(power, a.MaxHp - a.Hp);
                    if (heal > bestHeal)
                    {
                        bestHeal = heal;
                        best = u.Pos;
                    }
                }
                if (best.HasValue) Cast(SpellType.Renew, best.Value);
            }

            if (!wallUsed && CanCast(SpellType.Stonewall))
            {
                // Plug the gap next to the Keep that faces the nearest attacker.
                var near = foes.OfType<Unit>().Where(e => GameModel.Dist(e.Pos, keep) <= 4).ToList();
                var spot = near.Count == 0 ? null : GameModel.Dirs8.Select(d => keep + d)
                    .Where(p => m.In(p) && GameModel.Buildable(m.T(p)))
                    .OrderBy(p => near.Min(e => GameModel.Dist(e.Pos, p))).Cast<Vector2Int?>().FirstOrDefault();
                if (spot.HasValue)
                {
                    wallUsed = true;
                    Cast(SpellType.Stonewall, spot.Value);
                }
            }

            if (CanCast(SpellType.Ward) && side.Mana >= Reserve(SpellType.Ward) + Defs.Spell(SpellType.Ward).Cost + 1)
            {
                var frontline = m.Units.Where(u => u.Faction == me && u.WardTurns == 0 &&
                    m.Entities(foe).Any(e => GameModel.Dist(e.Pos, u.Pos) <= 2)).OrderByDescending(u => u.Hp).FirstOrDefault();
                if (frontline != null) Cast(SpellType.Ward, frontline.Pos);
            }

            // End of turn: spend mana that would otherwise overflow the bar next turn.
            if (!start && side.Mana > Defs.MaxMana - Defs.ManaPerTurn)
            {
                if (CanCast(SpellType.Transmute)) Cast(SpellType.Transmute, keep);
                else if (foes.Count > 0 && CanCast(SpellType.Bolt)) Cast(SpellType.Bolt, foes.OrderBy(e => e.Hp).First().Pos);
                else if (foes.Count > 0 && CanCast(SpellType.Meteor)) Cast(SpellType.Meteor, foes.OrderBy(e => e.Hp).First().Pos);
            }
        }

        void Economy()
        {
            var enemyKeep = m.Of(foe).Keep.Pos;
            var build = AiTactics.PickBuild(m, me, cfg.Spells.Length > 0);
            if (build.HasValue && m.BuildProblem(me, build.Value) == null)
            {
                var spot = AiTactics.BuildSpot(m, me);
                if (spot.HasValue) m.Build(me, build.Value, spot.Value);
            }
            foreach (var lab in m.Buildings.Where(b => b.Faction == me && b.Def.Researches && b.Complete && !b.Research.HasValue).ToList())
            {
                var tech = AiTactics.PickResearch(m, lab, rng);
                if (tech.HasValue) m.StartResearch(lab, tech.Value);
            }

            for (int guard = 0; guard < 6 && m.SlotsLeft(me) > 0; guard++)
            {
                var type = PickUnit();
                if (type == null) break;
                var tiles = m.RecruitTiles(me);
                if (tiles.Count == 0) break;
                m.Recruit(me, type.Value, tiles.OrderBy(p => GameModel.Dist(p, enemyKeep)).First());
                tally.Recruited(type.Value);
            }
        }

        UnitType? PickUnit()
        {
            var ok = cfg.Weights.Where(o => m.RecruitProblem(me, o.Key) == null && (o.Key != UnitType.Minions || side.Reserve >= 3)).ToList();
            if (ok.Count == 0) return null;
            float r = (float)rng.NextDouble() * ok.Sum(o => o.Value);
            foreach (var o in ok)
            {
                r -= o.Value;
                if (r <= 0) return o.Key;
            }
            return ok[ok.Count - 1].Key;
        }

        void Attack(Unit u, Entity t)
        {
            // Rally when the boost turns a hit into a kill, or for any swing at the Keep.
            if (CanCast(SpellType.Rally) && !u.Rallied && side.Mana >= Defs.Spell(SpellType.Rally).Cost + Reserve(SpellType.Rally) / 2)
            {
                int strikes = u.Type == UnitType.Twinblade ? 2 : 1;
                int dmg = GameModel.Blow(m.AttackOf(u, false, t), m.DefenseOf(t)) * strikes;
                int boosted = GameModel.Blow(m.AttackOf(u, false, t) + Defs.Spell(SpellType.Rally).Power, m.DefenseOf(t)) * strikes;
                bool keep = t is Building b && b.Type == BuildingType.Keep;
                if ((dmg < t.Hp && boosted >= t.Hp) || keep) Cast(SpellType.Rally, u.Pos);
            }
            tally.Attacked(u.Type);
            Record(m.Attack(u, t));
        }

        void Ability(Unit u, Vector2Int p)
        {
            tally.Attacked(u.Type);
            Record(m.UseAbility(u, p));
        }

        /// <summary>Same order of choices as EnemyAI.Act.</summary>
        void Act(Unit u)
        {
            var plan = AiTactics.AbilityPlan(m, u);
            if (plan.HasValue)
            {
                Ability(u, plan.Value);
                return;
            }

            bool support = u.Type == UnitType.Healer;
            var here = m.TargetsFrom(u, u.Pos);
            if (here.Count > 0 && !support && m.CanAttack(u))
            {
                Attack(u, here.OrderByDescending(t => AiTactics.Score(m, u, t, u.Pos)).First());
                return;
            }
            if (!m.CanMove(u)) return;

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
                    m.Move(u, bestD);
                    if (!u.Dead && !bestT.Dead) Attack(u, bestT);
                    return;
                }

                var step = AiTactics.ShadowstepPlan(m, u);
                if (step.HasValue)
                {
                    Ability(u, step.Value.to);
                    if (!u.Dead && !step.Value.target.Dead && m.TargetsFrom(u, u.Pos).Contains(step.Value.target))
                        Attack(u, step.Value.target);
                    return;
                }
            }

            var go = AiTactics.March(m, u, dests);
            if (go.HasValue) m.Move(u, go.Value);
            if (u.Dead) return;
            plan = AiTactics.AbilityPlan(m, u);
            if (plan.HasValue)
            {
                Ability(u, plan.Value);
                return;
            }
            if (!support)
            {
                var after = m.TargetsFrom(u, u.Pos);
                if (after.Count > 0) Attack(u, after.OrderByDescending(t => AiTactics.Score(m, u, t, u.Pos)).First());
            }
        }

        float Value(Entity e) => e is Unit u ? u.Def.Cost + u.Hp : 30f;
    }
}