using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Strategy
{
    public enum Objective { DestroyKeep, Survive }

    /// <summary>A line of dialogue shown at the start of the player's turn.</summary>
    public class Beat
    {
        public int Turn;
        public string Speaker, Text;
        public Beat(int turn, string speaker, string text) { Turn = turn; Speaker = speaker; Text = text; }
    }

    /// <summary>One campaign battle: a fixed map, the forces on each side, and the story around it.</summary>
    public class Chapter
    {
        public string Numeral, Title, Place, Intro, Outro;
        public int Size = 14, Seed;
        public int EnemyGold, EnemyIncome;
        public Objective Goal = Objective.DestroyKeep;
        public int SurviveTurns;
        public UnitType[] Roster, EnemyUnits, PlayerStart = new UnitType[0], EnemyStart = new UnitType[0];
        public SpellType[] Spells;
        /// <summary>Research that opens up in this chapter; later chapters keep everything earlier ones opened.</summary>
        public TechType[] NewTechs = new TechType[0];
        public MapOptions Map = new MapOptions();
        public Beat[] Beats = new Beat[0];

        /// <summary>Everything researchable in this chapter: its own new techs plus those of every chapter before it.</summary>
        public HashSet<TechType> TechPool => new HashSet<TechType>(Campaign.Chapters.TakeWhile(c => c != this).Append(this).SelectMany(c => c.NewTechs));

        public string ObjectiveTitle => Goal == Objective.Survive ? "Survive" : "Win";
        public string ObjectiveText => Goal == Objective.Survive
            ? $"Hold your Keep until turn {SurviveTurns}"
            : "Capture the enemy Keep";
    }

    /// <summary>
    /// "The Ashen Host": five battles against the Hollow King. Each chapter hands the player a curated roster,
    /// so new squads and spells arrive as part of the story. Progress is saved in PlayerPrefs.
    /// </summary>
    public static class Campaign
    {
        public const string Name = "The Ashen Host";

        public static readonly Chapter[] Chapters =
        {
            new Chapter
            {
                Numeral = "I", Title = "Embers at the Border", Place = "Fort Brannoch, the northern march",
                Intro = "Smoke rose over the northern march at dawn. The Ashen Host has crossed the river and pitched a war camp " +
                        "within sight of Fort Brannoch. You hold the Warden's seal now, and with it, the fort.\n\n" +
                        "Captain Oren Hale has gathered what the garrison can spare: shields, swords and a few spears. " +
                        "It isn't much. It will have to do.",
                Outro = "The war camp burns. In the ashes, Oren finds a seal of black iron: the mark of the Hollow King. " +
                        "This was no raid. It was a scouting party.",
                Size = 10, Seed = 1201, EnemyGold = -4, EnemyIncome = -1,
                NewTechs = new[] { TechType.Blades, TechType.Padding, TechType.ShieldWall },
                Roster = new[] { UnitType.Shield, UnitType.Sword, UnitType.Spear, UnitType.Minions },
                Spells = new[] { SpellType.Ward, SpellType.Bolt },
                EnemyUnits = new[] { UnitType.Minions, UnitType.Spear, UnitType.Sword },
                Beats = new[]
                {
                    new Beat(1, "Oren", "Their camp lies to the north-east. Keep the shields in front and let them come to you."),
                    new Beat(2, "Oren", "Hover an enemy with a squad selected to see how the fight would go before you commit."),
                    new Beat(3, "Oren", "Raise a Forge anywhere we can see. Research makes every squad we field stronger."),
                    new Beat(4, "Oren", "Gold glints in the fields, and there's a mine and an old library out there. Step a squad onto them to claim them."),
                },
            },
            new Chapter
            {
                Numeral = "II", Title = "The Calder Heights", Place = "Calder, hill country",
                Intro = "The Host marches south along the Calder ridges. Whoever holds the heights holds the road.\n\n" +
                        "Tamsin, a scout from the hill villages, brings archers who know every slope. From high ground, " +
                        "their arrows fly further and strike harder.",
                Outro = "The ridges are ours. From the highest peak, Tamsin sees fires across the valley: a great army " +
                        "gathering at the Ford of Ilm.",
                Size = 12, Seed = 2207, EnemyGold = -1,
                NewTechs = new[] { TechType.Fletching, TechType.Longbows, TechType.PinningShot },
                Roster = new[] { UnitType.Shield, UnitType.Sword, UnitType.Bow, UnitType.Crossbow, UnitType.Minions },
                Spells = new[] { SpellType.Ward, SpellType.Bolt, SpellType.Meteor },
                EnemyUnits = new[] { UnitType.Minions, UnitType.Sword, UnitType.Bow, UnitType.Crossbow, UnitType.Spear },
                Beats = new[]
                {
                    new Beat(1, "Tamsin", "Get the bows onto the hills. Up there they reach one tile further and hit harder."),
                    new Beat(3, "Tamsin", "An Academy can teach the bows Pinning Shot. A pinned squad can't move next turn."),
                    new Beat(4, "Oren", "Their crossbows are climbing too. Don't let them have the high ground."),
                },
            },
            new Chapter
            {
                Numeral = "III", Title = "The Long Night", Place = "Greywater Keep",
                Intro = "The Host slipped past the hills and caught you at Greywater with half your army still on the road. " +
                        "Reinforcements ride at first light.\n\n" +
                        "Hold the Keep through the night. Raise walls, tend the wounded, and do not let the gate fall.",
                Outro = "Dawn breaks over Greywater as riders from the south thunder down the road. The Host pulls back, " +
                        "but the Hollow King's banner has been seen at the Ford.",
                Size = 10, Seed = 3301, EnemyGold = 3, EnemyIncome = 1,
                NewTechs = new[] { TechType.Arcana, TechType.Plate },
                Goal = Objective.Survive, SurviveTurns = 10,
                Roster = new[] { UnitType.Shield, UnitType.Sword, UnitType.Bow, UnitType.Healer, UnitType.Druid, UnitType.Minions },
                Spells = new[] { SpellType.Ward, SpellType.Bolt, SpellType.Stonewall, SpellType.Renew },
                EnemyUnits = new[] { UnitType.Minions, UnitType.Sword, UnitType.Spear, UnitType.Twinblade, UnitType.Bow, UnitType.Knight },
                PlayerStart = new[] { UnitType.Shield, UnitType.Bow },
                EnemyStart = new[] { UnitType.Sword, UnitType.Twinblade, UnitType.Sword },
                Beats = new[]
                {
                    new Beat(1, "Oren", "We only need to last until turn 10. Stonewall can plug a gap in a hurry."),
                    new Beat(2, "Tamsin", "The wood-druids came with us. They grow forests where we need cover; squads in a forest are hard to see and hit."),
                    new Beat(5, "Oren", "Halfway to dawn. Keep the healer close to the line."),
                    new Beat(9, "Tamsin", "Horns to the south! One more push and they're here."),
                },
            },
            new Chapter
            {
                Numeral = "IV", Title = "Riders at the Ford", Place = "The Ford of Ilm",
                Intro = "Lady Ceren's riders have joined the cause: swift horses and heavy knights. The Host holds the " +
                        "Ford of Ilm, the last crossing before the capital.\n\n" +
                        "Strike fast. Riders hit hardest when they charge.",
                Outro = "The Ford is ours. Across the river the sky glows red over the Ember Crown, the Hollow King's " +
                        "fortress. It ends there.",
                Size = 12, Seed = 4409, EnemyGold = 4,
                Map = new MapOptions { River = true },
                NewTechs = new[] { TechType.WarHorses, TechType.ForcedMarch, TechType.PiercingShot },
                Roster = new[] { UnitType.Sword, UnitType.Knight, UnitType.Rider, UnitType.Bow, UnitType.Healer, UnitType.Ninja, UnitType.Minions },
                Spells = new[] { SpellType.Ward, SpellType.Haste, SpellType.Rally, SpellType.Meteor },
                EnemyUnits = new[] { UnitType.Minions, UnitType.Shield, UnitType.Sword, UnitType.Spear, UnitType.Knight, UnitType.Crossbow, UnitType.Twinblade, UnitType.Giant },
                Beats = new[]
                {
                    new Beat(1, "Ceren", "Move the riders before they strike. A charge adds two to the blow."),
                    new Beat(2, "Ceren", "The river can only be crossed at the fords. A ninja can Shadowstep over it, though."),
                    new Beat(3, "Oren", "Rally a rider before it charges and little will stand against it."),
                },
            },
            new Chapter
            {
                Numeral = "V", Title = "The Ember Crown", Place = "The Hollow King's fortress",
                Intro = "The Hollow King's fortress squats on a hill of black glass. Its pyromancers are said to burn whole " +
                        "regiments.\n\n" +
                        "You bring everything that is left: shields, blades, riders, a hill giant, wyverns from the eyries, and fire of your own. Break the Ember " +
                        "Crown and the war is over.",
                Outro = "The Ember Crown falls, and with it the Hollow King. The Ashen Host scatters into the burnt north, " +
                        "and the Vale will remember the Warden who held the line. Thank you for playing.",
                Size = 14, Seed = 5501, EnemyGold = -3, EnemyIncome = -1,
                NewTechs = new[] { TechType.Firestorm },
                Roster = new[] { UnitType.Shield, UnitType.Twinblade, UnitType.Crossbow, UnitType.Rider, UnitType.Pyromancer, UnitType.Giant, UnitType.Wyvern, UnitType.Minions },
                Spells = new[] { SpellType.Meteor, SpellType.Bolt, SpellType.Rally, SpellType.Transmute },
                EnemyUnits = null,
                EnemyStart = new[] { UnitType.Pyromancer, UnitType.Knight },
                Beats = new[]
                {
                    new Beat(1, "Oren", "Their Keep is far. Spread out, or their fire will catch us bunched together."),
                    new Beat(3, "Ceren", "Wyverns fly over walls and water alike. Send them for the Keep."),
                    new Beat(6, "Tamsin", "Pyromancers burn everything next to their target. Don't stand shoulder to shoulder."),
                },
            },
        };

        const string Key = "Campaign.Cleared";

        /// <summary>How many chapters have been won, in order. Chapter i is playable once i chapters are cleared.</summary>
        public static int Cleared
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, Chapters.Length);
            set
            {
                PlayerPrefs.SetInt(Key, Mathf.Clamp(value, 0, Chapters.Length));
                PlayerPrefs.Save();
            }
        }

        public static bool Unlocked(int i) => i <= Cleared;
    }
}
