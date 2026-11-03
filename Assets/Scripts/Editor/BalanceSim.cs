using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Cfg = Strategy.EditorTools.SimAI.Cfg;

namespace Strategy.EditorTools
{
    /// <summary>
    /// Balance harness: plays <see cref="SimAI"/> against itself on the real <see cref="GameModel"/>, with no rendering,
    /// and writes win rates per squad, spell, terrain and chapter to a text report.
    ///
    /// From the menu: Strategy → Run Balance Report (about a minute; writes BalanceReport.txt next to Assets).
    /// From the command line:
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Strategy.EditorTools.BalanceSim.Run -quit
    ///         -simExp core,spells -simN 40 -simOut report.txt -simTune "Bow.Health=4;Transmute.Cost=4"
    /// -simTune overrides def fields for that run only, so numbers can be tried before editing Defs.cs.
    /// </summary>
    public static class BalanceSim
    {
        /// <summary>Per-squad-type counters: recruited, damage, kills, lost, attacks, attack damage, counter damage.</summary>
        public class Tally
        {
            public readonly Dictionary<UnitType, int[]> Rows = new Dictionary<UnitType, int[]>();
            int[] Row(UnitType t) => Rows.TryGetValue(t, out var r) ? r : Rows[t] = new int[7];
            public void Recruited(UnitType t) => Row(t)[0]++;
            public void Lost(UnitType t) => Row(t)[3]++;
            public void Attacked(UnitType t) => Row(t)[4]++;

            public void Dealt(UnitType t, int amount, bool kill, bool counter)
            {
                var r = Row(t);
                r[1] += amount;
                if (kill) r[2]++;
                r[counter ? 6 : 5] += amount;
            }
        }

        public class Result
        {
            /// <summary>Real winner, or at the turn limit the side with clearly more army and Keep left (null if close).</summary>
            public Faction? Winner;
            /// <summary>True when a Keep actually fell (or a survival chapter was won).</summary>
            public bool Decided;
            public int Turns, PlayerKeep;
        }

        const int MaxTurns = 40;
        static StringBuilder log;
        static readonly Tally totals = new Tally();

        static readonly SpellType[] DefaultSpells = { SpellType.Meteor, SpellType.Ward, SpellType.Haste, SpellType.Bolt };
        /// <summary>The spells <see cref="EnemyAI"/> knows how to cast.</summary>
        static readonly SpellType[] EnemySpells = { SpellType.Meteor, SpellType.Ward, SpellType.Bolt };

        [MenuItem("Strategy/Run Balance Report")]
        static void RunFromMenu()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BalanceReport.txt"));
            var exps = new[] { "core", "mono", "spells", "hills", "campaign", "skirmish" };
            try
            {
                Report(exps, 20, path, (i, name) =>
                    EditorUtility.DisplayProgressBar("Balance report", $"Simulating: {name}", i / (float)exps.Length));
            }
            finally { EditorUtility.ClearProgressBar(); }
            Debug.Log("Balance report written to " + path);
            EditorUtility.RevealInFinder(path);
        }

        /// <summary>Entry point for -executeMethod.</summary>
        public static void Run()
        {
            var tune = Arg("-simTune", "");
            Report(Arg("-simExp", "core,mono,spells,hills,campaign,skirmish").Split(','), int.Parse(Arg("-simN", "20")),
                Arg("-simOut", "BalanceReport.txt"), null, tune);
        }

        static void Report(string[] exps, int n, string outFile, Action<int, string> progress, string tune = "")
        {
            log = new StringBuilder();
            var saved = tune != "" ? Apply(tune) : null;
            if (saved != null) Out("tuning: " + tune);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                for (int i = 0; i < exps.Length; i++)
                {
                    progress?.Invoke(i, exps[i]);
                    Out($"\n=== {exps[i]} (n = {n}) ===");
                    switch (exps[i])
                    {
                        case "base": Base(n); break;
                        case "mono": Mono(n); break;
                        case "core": Core(n); break;
                        case "spells": Spells(n); break;
                        case "hills": Hills(n); break;
                        case "campaign": CampaignBattles(n); break;
                        case "skirmish": Skirmish(n); break;
                        default: Out("unknown experiment"); break;
                    }
                }
            }
            catch (Exception ex) { Out("EXCEPTION " + ex); }
            finally { saved?.Invoke(); }
            Out($"\nelapsed {sw.Elapsed.TotalSeconds:F0}s");
            File.WriteAllText(outFile, log.ToString());
        }

        static void Out(string s)
        {
            log.AppendLine(s);
            Console.WriteLine(s);
        }

        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
        }

        /// <summary>Overrides def fields, e.g. "Crossbow.Attack=3;Transmute.Cost=4". Returns an undo action.</summary>
        static Action Apply(string spec)
        {
            var undo = new List<Action>();
            foreach (var part in spec.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=');
                var nf = kv[0].Trim().Split('.');
                object target = Enum.TryParse(nf[0], out UnitType ut) ? Defs.Unit(ut)
                    : Enum.TryParse(nf[0], out SpellType st) ? Defs.Spell(st)
                    : (object)Defs.Building((BuildingType)Enum.Parse(typeof(BuildingType), nf[0]));
                var field = target.GetType().GetField(nf[1]);
                var old = field.GetValue(target);
                field.SetValue(target, int.Parse(kv[1]));
                undo.Add(() => field.SetValue(target, old));
            }
            return () => undo.ForEach(u => u());
        }

        // ---------------------------------------------------------------- game loop

        public static Result Play(int size, int mapSeed, Cfg p, Cfg e, int aiSeed, Chapter ch = null, bool flat = false)
        {
            var m = new GameModel(size, size, mapSeed, ch?.Map);
            if (ch != null) m.TechPool = ch.TechPool;
            if (flat) foreach (var t in m.AllTiles()) t.Height = 0;
            foreach (var (f, c) in new[] { (Faction.Player, p), (Faction.Enemy, e) })
            {
                var s = m.Of(f);
                s.Gold = Mathf.Max(0, s.Gold + c.GoldBonus);
                s.IncomeBonus = c.IncomeBonus;
                var foeKeep = m.Of(GameModel.Other(f)).Keep.Pos;
                foreach (var t in c.Start)
                {
                    var tiles = m.RecruitTiles(f);
                    if (tiles.Count == 0) break;
                    m.Spawn(t, f, tiles.OrderBy(d => GameModel.Dist(d, foeKeep)).First(), t == UnitType.Minions ? Defs.MinionSquad : 0);
                }
            }
            m.UpdateFog();
            var rng = new System.Random(aiSeed);
            var ai = new[] { new SimAI(m, Faction.Player, p, rng, totals), new SimAI(m, Faction.Enemy, e, rng, totals) };

            // Same order as GameManager: player turn, enemy upkeep and turn, then the next round.
            while (m.Winner == null && m.Turn <= MaxTurns)
            {
                ai[0].Run();
                if (m.Winner != null) break;
                m.BeginTurn(Faction.Enemy);
                if (m.Winner != null) break;
                ai[1].Run();
                if (m.Winner != null) break;
                m.Turn++;
                if (ch != null && ch.Goal == Objective.Survive && m.Turn >= ch.SurviveTurns)
                {
                    m.Winner = Faction.Player;
                    break;
                }
                m.BeginTurn(Faction.Player);
            }

            var winner = m.Winner;
            if (winner == null)
            {
                float sp = Strength(m, Faction.Player), se = Strength(m, Faction.Enemy);
                if (sp > se * 1.1f) winner = Faction.Player;
                else if (se > sp * 1.1f) winner = Faction.Enemy;
            }
            var keep = m.Of(Faction.Player).Keep;
            return new Result { Winner = winner, Decided = m.Winner != null, Turns = m.Turn, PlayerKeep = keep.Dead ? 0 : keep.Hp };
        }

        /// <summary>Rough material count for games that hit the turn limit: Keep HP, army value, banked gold.</summary>
        static float Strength(GameModel m, Faction f)
        {
            float s = m.Of(f).Keep.Hp + m.Of(f).Gold * 0.5f;
            foreach (var u in m.Units)
                if (u.Faction == f) s += u.IsMinions ? u.Hp : (u.Def.Cost + 2) * u.Hp / (float)u.MaxHp;
            return s;
        }

        /// <summary>A's score against B over n mirrored maps, playing each map from both sides (win 1, close 0.5).</summary>
        static (float score, float unfinished, float turns, float first) Match(Cfg a, Cfg b, int n, int size = 12, bool flat = false)
        {
            float score = 0, unfinished = 0, turns = 0, first = 0;
            for (int i = 0; i < n; i++)
            {
                int seed = 1000 + i * 7919;
                for (int swap = 0; swap < 2; swap++)
                {
                    var r = swap == 0 ? Play(size, seed, a, b, seed + 1, null, flat) : Play(size, seed, b, a, seed + 1, null, flat);
                    turns += r.Turns;
                    if (!r.Decided) unfinished++;
                    if (r.Winner == null)
                    {
                        score += 0.5f;
                        first += 0.5f;
                        continue;
                    }
                    if (r.Winner == Faction.Player) first++;
                    if ((r.Winner == Faction.Player) == (swap == 0)) score++;
                }
            }
            return (score / (2 * n), unfinished / (2 * n), turns / (2 * n), first / (2 * n));
        }

        static Cfg Mix(Dictionary<UnitType, float> w, params SpellType[] spells) => new Cfg { Weights = w, Spells = spells };

        static void DumpTypeStats()
        {
            Out("squad        recruits  dmg/recruit  kills/recruit  lost/recruit  dmg/gold  attacks/recruit  dmg/attack");
            foreach (var kv in totals.Rows.OrderBy(k => k.Key))
            {
                var r = kv.Value;
                if (r[0] == 0) continue;
                float per = r[0], spent = r[0] * Mathf.Max(Defs.Unit(kv.Key).Cost, 1);
                Out($"{kv.Key,-11} {r[0],9}  {r[1] / per,11:F2}  {r[2] / per,13:F2}  {r[3] / per,12:F2}  {r[1] / spent,8:F2}  {r[4] / per,15:F2}  {(r[4] > 0 ? r[5] / (float)r[4] : 0),10:F2}");
            }
            totals.Rows.Clear();
        }

        // ---------------------------------------------------------------- experiments

        /// <summary>Identical armies: first-move advantage, game length, hill coverage.</summary>
        static void Base(int n)
        {
            foreach (var size in new[] { 10, 12, 15 })
            {
                var a = Mix(SimAI.DefaultWeights, DefaultSpells);
                var r = Match(a, a, n, size);
                Out($"mirror {size}x{size}: first player scores {r.first:P0}, unfinished by turn {MaxTurns} {r.unfinished:P0}, avg turns {r.turns:F1}");
            }
            DumpTypeStats();
            foreach (var size in new[] { 10, 12, 15 })
            {
                int raised = 0, total = 0;
                for (int i = 0; i < 30; i++)
                    foreach (var t in new GameModel(size, size, 500 + i).AllTiles())
                    {
                        total++;
                        if (t.Height > 0) raised++;
                    }
                Out($"hill tiles {size}x{size}: {raised / (float)total:P0}");
            }
        }

        static readonly UnitType[] Fighters =
        {
            UnitType.Shield, UnitType.Sword, UnitType.Bow, UnitType.Knight, UnitType.Spear, UnitType.Twinblade,
            UnitType.Crossbow, UnitType.Rider, UnitType.Pyromancer,
        };

        /// <summary>Armies of a single squad type against each other. Very rock-paper-scissors; read with "core".</summary>
        static void Mono(int n)
        {
            var score = Fighters.ToDictionary(t => t, t => 0f);
            var grid = new Dictionary<(UnitType, UnitType), float>();
            for (int i = 0; i < Fighters.Length; i++)
                for (int j = i + 1; j < Fighters.Length; j++)
                {
                    var r = Match(Mix(SimAI.Only(new[] { Fighters[i] })), Mix(SimAI.Only(new[] { Fighters[j] })), n);
                    grid[(Fighters[i], Fighters[j])] = r.score;
                    grid[(Fighters[j], Fighters[i])] = 1 - r.score;
                    score[Fighters[i]] += r.score;
                    score[Fighters[j]] += 1 - r.score;
                }
            Out("row vs column, % score" + new string(' ', 4) + string.Concat(Fighters.Select(t => t.ToString().Substring(0, Math.Min(5, t.ToString().Length)).PadLeft(6))));
            foreach (var a in Fighters)
                Out(a.ToString().PadRight(27) + string.Concat(Fighters.Select(b => a == b ? "     -" : (grid[(a, b)] * 100).ToString("F0").PadLeft(6))));
            Out("\nsingle-type ranking:");
            foreach (var kv in score.OrderByDescending(k => k.Value))
                Out($"  {kv.Key,-11} {kv.Value / (Fighters.Length - 1),4:P0}   cost {Defs.Unit(kv.Key).Cost}");
            totals.Rows.Clear();
        }

        static readonly UnitType[] CoreTypes = { UnitType.Sword, UnitType.Bow, UnitType.Spear, UnitType.Minions };

        /// <summary>A basic army that leans heavily on one squad type, against the basic army. ~50% is balanced.</summary>
        static void Core(int n)
        {
            var core = Mix(SimAI.Only(CoreTypes));
            foreach (var x in Defs.Roster)
            {
                var w = SimAI.Only(CoreTypes);
                w[x] = 6f;
                var r = Match(Mix(w), core, n);
                Out($"basic army + lots of {x,-11}: {r.score,4:P0}   (unfinished {r.unfinished:P0}, avg turns {r.turns:F1})");
            }
            DumpTypeStats();
        }

        /// <summary>Each spell alone, against no spells and against Bolt alone (~50% vs Bolt is on par).</summary>
        static void Spells(int n)
        {
            var none = Mix(SimAI.DefaultWeights);
            var bolt = Mix(SimAI.DefaultWeights, SpellType.Bolt);
            foreach (var s in Defs.AllSpells)
            {
                var r = Match(Mix(SimAI.DefaultWeights, s), none, n);
                var rb = Match(Mix(SimAI.DefaultWeights, s), bolt, n);
                Out($"{s,-10} alone: vs no spells {r.score,4:P0}   vs Bolt alone {rb.score,4:P0}");
            }
            var four = Mix(SimAI.DefaultWeights, DefaultSpells);
            foreach (var set in new[]
                     {
                         new[] { SpellType.Rally, SpellType.Transmute, SpellType.Bolt, SpellType.Meteor },
                         new[] { SpellType.Renew, SpellType.Stonewall, SpellType.Ward, SpellType.Bolt },
                         new[] { SpellType.Transmute, SpellType.Rally, SpellType.Haste, SpellType.Ward },
                     })
                Out($"{string.Join("/", set)} vs the default four: {Match(Mix(SimAI.DefaultWeights, set), four, n).score:P0}");
        }

        /// <summary>Ranged against melee armies, on normal maps and on the same maps flattened.</summary>
        static void Hills(int n)
        {
            var ranged = Mix(SimAI.Only(new[] { UnitType.Bow, UnitType.Crossbow, UnitType.Pyromancer, UnitType.Shield, UnitType.Minions }));
            var melee = Mix(SimAI.Only(new[] { UnitType.Sword, UnitType.Knight, UnitType.Twinblade, UnitType.Rider, UnitType.Minions }));
            Out($"ranged army vs melee army: with hills {Match(ranged, melee, n).score:P0}, flat {Match(ranged, melee, n, 12, true).score:P0}");
            var swords = Mix(SimAI.Only(new[] { UnitType.Sword, UnitType.Knight }));
            foreach (var t in new[] { UnitType.Bow, UnitType.Crossbow, UnitType.Pyromancer })
            {
                var a = Mix(SimAI.Only(new[] { t, UnitType.Sword }));
                Out($"{t} + Sword vs Sword + Knight: with hills {Match(a, swords, n).score:P0}, flat {Match(a, swords, n, 12, true).score:P0}");
            }
        }

        static Cfg PlayerCfgFor(Chapter c) => new Cfg { Weights = SimAI.Only(c.Roster), Spells = c.Spells, Start = c.PlayerStart };

        static Cfg EnemyCfgFor(Chapter c) => new Cfg
        {
            Weights = c.EnemyUnits == null ? SimAI.DefaultWeights : SimAI.Only(c.EnemyUnits),
            Spells = EnemySpells, GoldBonus = c.EnemyGold, IncomeBonus = c.EnemyIncome, Start = c.EnemyStart,
        };

        /// <summary>Each chapter with SimAI in the player's seat. A person should do better than this.</summary>
        static void CampaignBattles(int n)
        {
            foreach (var c in Campaign.Chapters)
            {
                float wins = 0, ahead = 0, turns = 0, keepLeft = 0;
                for (int k = 0; k < n; k++)
                {
                    var r = Play(c.Size, c.Seed, PlayerCfgFor(c), EnemyCfgFor(c), 9000 + k * 31, c);
                    keepLeft += r.PlayerKeep;
                    turns += r.Turns;
                    if (r.Winner != Faction.Player) continue;
                    if (r.Decided) wins++;
                    else ahead++;
                }
                Out($"{c.Numeral,-3} {c.Title,-22}: AI player wins {wins / n,4:P0}, ahead at turn {MaxTurns} {ahead / n,4:P0}, " +
                    $"avg turns {turns / n:F1}, own Keep HP left {keepLeft / n:F1}");
            }
            totals.Rows.Clear();
        }

        /// <summary>The default skirmish loadout against the enemy at each difficulty and map size.</summary>
        static void Skirmish(int n)
        {
            var loadout = new[] { UnitType.Shield, UnitType.Sword, UnitType.Bow, UnitType.Knight, UnitType.Healer, UnitType.Minions };
            foreach (var (name, gold, income) in new[] { ("Easy", -6, -1), ("Normal", 0, 0), ("Hard", 6, 1) })
                foreach (var size in new[] { 10, 12, 15 })
                {
                    float wins = 0, ahead = 0;
                    for (int k = 0; k < n; k++)
                    {
                        var e = new Cfg { Weights = SimAI.DefaultWeights, Spells = EnemySpells, GoldBonus = gold, IncomeBonus = income };
                        var r = Play(size, 3000 + k * 101, Mix(SimAI.Only(loadout), DefaultSpells), e, 77 + k);
                        if (r.Winner != Faction.Player) continue;
                        if (r.Decided) wins++;
                        else ahead++;
                    }
                    Out($"{name,-6} {size}x{size}: AI player wins {wins / n,4:P0}, ahead at turn {MaxTurns} {ahead / n,4:P0}");
                }
            totals.Rows.Clear();
        }
    }
}
