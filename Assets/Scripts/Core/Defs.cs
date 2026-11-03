using UnityEngine;

namespace Strategy
{
    public enum Faction { Player, Enemy }
    public enum UnitType { Shield, Sword, Bow, Knight, Spear, Healer, Twinblade, Minions, Crossbow, Rider, Pyromancer, Ninja, Giant, Druid, Wyvern }
    public enum BuildingType { Keep, Barracks, ManaWell, Watchtower, Wall, Forge, Academy }
    public enum SpellType { Meteor, Ward, Haste, Bolt, Renew, Rally, Stonewall, Transmute }
    public enum Terrain { Plain, Forest, Water, Rock }
    /// <summary>Capturable sites. A squad that steps onto one claims it for its side.</summary>
    public enum PoiType { None, GoldMine, Library, Shrine, Outpost, Spring }
    public enum AbilityType { None, Mend, Whirlwind, ShieldBash, ShieldWall, PinningShot, PiercingShot, Firestorm, Shadowstep, HurlBoulder, Overgrowth }
    /// <summary>What an ability is aimed at. Self abilities fire at once; Move abilities use the squad's move, not its action.</summary>
    public enum AbilityTarget { Self, Ally, Enemy, Tile, Move }
    public enum TechType { Blades, Fletching, Padding, WarHorses, Longbows, Plate, ShieldWall, PinningShot, Arcana, ForcedMarch, PiercingShot, Firestorm }

    public class UnitDef
    {
        public UnitType Type;
        public string Name, Role, Icon;
        public int Cost, Attack, Health, Defense, Move, Range = 1, Vision = 3;
        public string PassiveName, PassiveText;
        public AbilityType Ability;
        /// <summary>Flies over water, rock, forest and buildings (but can't stop on them).</summary>
        public bool Flying;
        public bool Melee => Range == 1;
    }

    public class BuildingDef
    {
        public BuildingType Type;
        public string Name, Role, Icon;
        public int Cost, Health, Defense, Turns, Vision = 2;
        public string PassiveName, PassiveText;
        public bool Researches => Type == BuildingType.Forge || Type == BuildingType.Academy;
    }

    public class SpellDef
    {
        public SpellType Type;
        public string Name, Icon, Desc, Hint;
        /// <summary>Mana cost, and the spell's main number (damage, heal, bonus or gold).</summary>
        public int Cost, Power;
        /// <summary>Cast straight away on click, with no target to pick.</summary>
        public bool Instant;
        public string Text => $"{Name} · {Cost} mana: {Desc}";
    }

    public class AbilityDef
    {
        public AbilityType Type;
        public string Name, Text, Hint;
        public AbilityTarget Target;
        /// <summary>Turns to wait after use; 2 means every other turn.</summary>
        public int Cooldown;
        /// <summary>Reach in tiles (0 = the squad's attack range).</summary>
        public int Range, Power;
        /// <summary>Research needed before squads can use it (null = known from the start).</summary>
        public TechType? Unlock;
    }

    public class TechDef
    {
        public TechType Type;
        public string Name, Icon, Desc;
        /// <summary>Which research building studies it.</summary>
        public BuildingType Building;
        public int Cost, Turns, Tier;
        public TechType? Requires;
    }

    public class PoiDef
    {
        public PoiType Type;
        public string Name, Icon, Desc;
    }

    /// <summary>All tuning numbers live here. Values from the mockup where it gives them, placeholders elsewhere.</summary>
    public static class Defs
    {
        public const int StartGold = 14, KeepIncome = 3, PickupGold = 3;
        public const int StartMana = 6, ManaPerTurn = 2, MaxMana = 10;
        public const int BaseSlots = 2, MinionReserve = 10, MinionSquad = 4;
        /// <summary>High ground: ranged squads on a hill get +range, and +AD against lower targets.</summary>
        public const int HillRange = 1, HighGroundAttack = 1;

        /// <summary>Movement is paid in thirds of a tile: open ground 3, road to road 2, into a forest 6.</summary>
        public const int StepCost = 3, RoadCost = 2, ForestCost = 6;
        public const int ForestDefense = 1;
        /// <summary>Research points: the Keep and each research building give 1 a turn, a Library 2.</summary>
        public const int StartResearch = 2, KeepResearch = 1, BuildingResearch = 1;
        public const int MineGold = 2, LibraryResearch = 2, ShrineMana = 1, SpringHeal = 2, OutpostVision = 3;
        /// <summary>Most damage a Giant takes from a single hit.</summary>
        public const int GiantHitCap = 4;
        public const int GuardDefense = 2;

        static readonly UnitDef[] units =
        {
            new UnitDef { Type = UnitType.Shield, Name = "Shield", Role = "Frontline · tank", Icon = "⛨",
                Cost = 2, Attack = 1, Health = 6, Defense = 3, Move = 2,
                PassiveName = "Bulwark", PassiveText = "Adjacent allies gain +1 DEF.", Ability = AbilityType.ShieldWall },
            new UnitDef { Type = UnitType.Sword, Name = "Sword", Role = "Frontline · melee", Icon = "⚔",
                Cost = 3, Attack = 3, Health = 5, Defense = 1, Move = 3,
                PassiveName = "Riposte", PassiveText = "Counterattacks deal +1 damage." },
            new UnitDef { Type = UnitType.Bow, Name = "Bow", Role = "Backline · ranged", Icon = "➶",
                Cost = 3, Attack = 2, Health = 3, Defense = 0, Move = 2, Range = 3,
                PassiveName = "Volley", PassiveText = "Attacks enemies up to 3 tiles away.", Ability = AbilityType.PinningShot },
            new UnitDef { Type = UnitType.Knight, Name = "Knight", Role = "Frontline · melee", Icon = "♞",
                Cost = 5, Attack = 3, Health = 6, Defense = 2, Move = 3,
                PassiveName = "Stand Firm", PassiveText = "+1 DEF if it did not move this turn.", Ability = AbilityType.ShieldBash },
            new UnitDef { Type = UnitType.Spear, Name = "Spear", Role = "Skirmisher · melee", Icon = "↗",
                Cost = 1, Attack = 2, Health = 4, Defense = 0, Move = 2,
                PassiveName = "Brace", PassiveText = "+2 AD when counterattacking." },
            new UnitDef { Type = UnitType.Healer, Name = "Healer", Role = "Support · ranged", Icon = "✚",
                Cost = 3, Attack = 1, Health = 4, Defense = 1, Move = 3,
                PassiveName = "Soothing Aura", PassiveText = "Heals adjacent allies 1 HP at the start of your turn.", Ability = AbilityType.Mend },
            new UnitDef { Type = UnitType.Twinblade, Name = "Twinblade", Role = "Skirmisher · melee", Icon = "‡",
                Cost = 4, Attack = 3, Health = 5, Defense = 1, Move = 3,
                PassiveName = "Flurry", PassiveText = "Strikes twice when it attacks.", Ability = AbilityType.Whirlwind },
            new UnitDef { Type = UnitType.Minions, Name = "Minions", Role = "Swarm · melee", Icon = "⁂",
                Cost = 0, Attack = 1, Health = 1, Defense = 0, Move = 2,
                PassiveName = "Swarm", PassiveText = "AD and HP equal the number of minions in the squad." },
            new UnitDef { Type = UnitType.Crossbow, Name = "Crossbow", Role = "Backline · ranged", Icon = "⊕",
                Cost = 5, Attack = 3, Health = 4, Defense = 1, Move = 2, Range = 2,
                PassiveName = "Steady Aim", PassiveText = "+1 AD if it did not move this turn.", Ability = AbilityType.PiercingShot },
            new UnitDef { Type = UnitType.Rider, Name = "Rider", Role = "Flanker · melee", Icon = "♘",
                Cost = 5, Attack = 3, Health = 5, Defense = 1, Move = 5,
                PassiveName = "Charge", PassiveText = "+2 AD when it attacks after moving." },
            new UnitDef { Type = UnitType.Pyromancer, Name = "Pyromancer", Role = "Backline · ranged", Icon = "♨",
                Cost = 4, Attack = 3, Health = 4, Defense = 0, Move = 2, Range = 2,
                PassiveName = "Scorch", PassiveText = "Attacks also burn enemies next to the target for 1.", Ability = AbilityType.Firestorm },
            new UnitDef { Type = UnitType.Ninja, Name = "Ninja", Role = "Assassin · melee", Icon = "✶",
                Cost = 4, Attack = 3, Health = 4, Defense = 0, Move = 4,
                PassiveName = "Unseen", PassiveText = "Its targets can't strike back.", Ability = AbilityType.Shadowstep },
            new UnitDef { Type = UnitType.Giant, Name = "Giant", Role = "Siege · melee", Icon = "☗",
                Cost = 8, Attack = 4, Health = 9, Defense = 1, Move = 2, Vision = 4,
                PassiveName = "Thick Hide", PassiveText = "No single hit deals it more than 4 damage.", Ability = AbilityType.HurlBoulder },
            new UnitDef { Type = UnitType.Druid, Name = "Druid", Role = "Support · ranged", Icon = "❦",
                Cost = 3, Attack = 3, Health = 4, Defense = 0, Move = 2, Range = 2,
                PassiveName = "Forest Kin", PassiveText = "+1 AD and +1 DEF while standing in a forest.", Ability = AbilityType.Overgrowth },
            new UnitDef { Type = UnitType.Wyvern, Name = "Wyvern", Role = "Flyer · melee", Icon = "⋎",
                Cost = 5, Attack = 4, Health = 5, Defense = 0, Move = 4, Vision = 4, Flying = true,
                PassiveName = "Wings", PassiveText = "Flies over water, rock, forest and walls." },
        };

        static readonly BuildingDef[] buildings =
        {
            new BuildingDef { Type = BuildingType.Keep, Name = "Keep", Role = "Stronghold", Icon = "♜",
                Health = 20, Defense = 2, Vision = 3,
                PassiveName = "Seat of Power", PassiveText = "+3 gold and +1 research each turn. If it falls, the war is lost." },
            new BuildingDef { Type = BuildingType.Barracks, Name = "Barracks", Role = "Military · 1 tile", Icon = "⌂",
                Cost = 6, Health = 12, Defense = 2, Turns = 2,
                PassiveName = "Drill Yard", PassiveText = "+1 recruit slot. Units can be recruited next to it." },
            new BuildingDef { Type = BuildingType.ManaWell, Name = "Mana Well", Role = "Economy · 1 tile", Icon = "◊",
                Cost = 5, Health = 8, Defense = 1, Turns = 2,
                PassiveName = "Siphon", PassiveText = "+1 mana at the start of each of your turns." },
            new BuildingDef { Type = BuildingType.Watchtower, Name = "Watchtower", Role = "Defense · 1 tile", Icon = "♖",
                Cost = 4, Health = 6, Defense = 1, Turns = 1, Vision = 5,
                PassiveName = "Lookout", PassiveText = "Sees 5 tiles. Hits an enemy within 2 tiles for 1 at the start of your turn." },
            new BuildingDef { Type = BuildingType.Wall, Name = "Wall", Role = "Defense · 1 tile", Icon = "▦",
                Cost = 1, Health = 10, Defense = 3, Turns = 1, Vision = 1,
                PassiveName = "Rampart", PassiveText = "Blocks movement. Cheap and sturdy." },
            new BuildingDef { Type = BuildingType.Forge, Name = "Forge", Role = "Research · 1 tile", Icon = "⚒",
                Cost = 5, Health = 10, Defense = 2, Turns = 1,
                PassiveName = "Smithy", PassiveText = "Researches weapons and armour. +1 research each turn." },
            new BuildingDef { Type = BuildingType.Academy, Name = "Academy", Role = "Research · 1 tile", Icon = "✎",
                Cost = 5, Health = 8, Defense = 1, Turns = 1,
                PassiveName = "Lecture Hall", PassiveText = "Researches drills and abilities. +1 research each turn." },
        };

        static readonly SpellDef[] spells =
        {
            new SpellDef { Type = SpellType.Meteor, Name = "Meteor", Icon = "☄", Cost = 5, Power = 3,
                Desc = "3 damage on a tile, 1 to enemies around it",
                Hint = "Pick a visible tile for Meteor" },
            new SpellDef { Type = SpellType.Ward, Name = "Ward", Icon = "◈", Cost = 2, Power = 2,
                Desc = "+2 DEF to a squad until your next turn",
                Hint = "Pick one of your squads to Ward" },
            new SpellDef { Type = SpellType.Haste, Name = "Haste", Icon = "»", Cost = 3,
                Desc = "a squad can move and attack again, +1 MOVE",
                Hint = "Pick one of your squads to Haste" },
            new SpellDef { Type = SpellType.Bolt, Name = "Bolt", Icon = "ϟ", Cost = 4, Power = 3,
                Desc = "3 damage to one visible enemy",
                Hint = "Pick a visible enemy for Bolt" },
            new SpellDef { Type = SpellType.Renew, Name = "Renew", Icon = "❀", Cost = 3, Power = 2,
                Desc = "heal 2 HP to your squads on a tile and the 8 around it",
                Hint = "Pick one of your squads to Renew around" },
            new SpellDef { Type = SpellType.Rally, Name = "Rally", Icon = "⚑", Cost = 2, Power = 2,
                Desc = "+2 AD to a squad until your next turn",
                Hint = "Pick one of your squads to Rally" },
            new SpellDef { Type = SpellType.Stonewall, Name = "Stonewall", Icon = "▥", Cost = 2,
                Desc = "raise a finished Wall on an empty visible tile",
                Hint = "Pick an empty visible tile for the wall" },
            new SpellDef { Type = SpellType.Transmute, Name = "Transmute", Icon = "✦", Cost = 5, Power = 3, Instant = true,
                Desc = "turn mana into 3 gold, right away",
                Hint = "Mana becomes gold" },
        };

        static readonly AbilityDef[] abilities =
        {
            null,
            new AbilityDef { Type = AbilityType.Mend, Name = "Mend", Target = AbilityTarget.Ally, Cooldown = 2, Range = 2, Power = 3,
                Text = "Restore 3 HP to an ally within 2 tiles.", Hint = "Pick an ally within 2 tiles to Mend" },
            new AbilityDef { Type = AbilityType.Whirlwind, Name = "Whirlwind", Target = AbilityTarget.Self, Cooldown = 4,
                Text = "Strike every adjacent enemy once. No counterattacks.", Hint = "Whirlwind hits every enemy next to it" },
            new AbilityDef { Type = AbilityType.ShieldBash, Name = "Shield Bash", Target = AbilityTarget.Enemy, Cooldown = 3, Range = 1,
                Text = "Hit an adjacent enemy squad and shove it back a tile (1 more damage if it can't move).",
                Hint = "Pick an adjacent enemy squad to bash" },
            new AbilityDef { Type = AbilityType.ShieldWall, Name = "Shield Wall", Target = AbilityTarget.Self, Cooldown = 3, Power = 2,
                Text = "+2 DEF to this squad and the allies beside it until your next turn.",
                Hint = "Shield Wall guards this squad and its neighbours", Unlock = TechType.ShieldWall },
            new AbilityDef { Type = AbilityType.PinningShot, Name = "Pinning Shot", Target = AbilityTarget.Enemy, Cooldown = 3,
                Text = "Shoot an enemy squad in range. It can't move on its next turn.",
                Hint = "Pick an enemy squad in range to pin", Unlock = TechType.PinningShot },
            new AbilityDef { Type = AbilityType.PiercingShot, Name = "Piercing Shot", Target = AbilityTarget.Enemy, Cooldown = 2,
                Text = "Shoot an enemy in range, ignoring its DEF. No counterattack.",
                Hint = "Pick an enemy in range for Piercing Shot", Unlock = TechType.PiercingShot },
            new AbilityDef { Type = AbilityType.Firestorm, Name = "Firestorm", Target = AbilityTarget.Tile, Cooldown = 3, Range = 2, Power = 2,
                Text = "Set a tile within 2 ablaze: 2 damage there, 1 to enemies beside it. Forests burn away.",
                Hint = "Pick a visible tile within 2 for Firestorm", Unlock = TechType.Firestorm },
            new AbilityDef { Type = AbilityType.Shadowstep, Name = "Shadowstep", Target = AbilityTarget.Move, Cooldown = 2, Range = 3,
                Text = "Vanish and reappear on an empty tile within 3, over anything. Can still attack.",
                Hint = "Pick an empty tile within 3 to Shadowstep to" },
            new AbilityDef { Type = AbilityType.HurlBoulder, Name = "Hurl Boulder", Target = AbilityTarget.Enemy, Cooldown = 3, Range = 3, Power = 3,
                Text = "Throw a boulder at an enemy within 3 tiles for 3 damage.", Hint = "Pick an enemy within 3 to hit with a boulder" },
            new AbilityDef { Type = AbilityType.Overgrowth, Name = "Overgrowth", Target = AbilityTarget.Tile, Cooldown = 2, Range = 2,
                Text = "Grow a forest on an empty tile within 2. Forests hide squads and give +1 DEF.",
                Hint = "Pick an empty tile within 2 to grow a forest" },
        };

        static readonly TechDef[] techs =
        {
            new TechDef { Type = TechType.Blades, Name = "Tempered Blades", Icon = "⚔", Building = BuildingType.Forge, Tier = 1, Cost = 4, Turns = 2,
                Desc = "+1 AD for melee squads (not Minions)." },
            new TechDef { Type = TechType.Fletching, Name = "Fletching", Icon = "➶", Building = BuildingType.Forge, Tier = 1, Cost = 4, Turns = 2,
                Desc = "+1 AD for ranged squads." },
            new TechDef { Type = TechType.Padding, Name = "Padded Armour", Icon = "♥", Building = BuildingType.Forge, Tier = 1, Cost = 4, Turns = 2,
                Desc = "+2 HP for every squad except Minions." },
            new TechDef { Type = TechType.WarHorses, Name = "War Horses", Icon = "♘", Building = BuildingType.Forge, Tier = 2, Cost = 6, Turns = 2,
                Desc = "+1 MOVE for Knights and Riders.", Requires = TechType.Blades },
            new TechDef { Type = TechType.Longbows, Name = "Longbows", Icon = "↣", Building = BuildingType.Forge, Tier = 2, Cost = 6, Turns = 2,
                Desc = "+1 range for Bows and Crossbows.", Requires = TechType.Fletching },
            new TechDef { Type = TechType.Plate, Name = "Steel Plate", Icon = "⛨", Building = BuildingType.Forge, Tier = 2, Cost = 7, Turns = 3,
                Desc = "+1 DEF for every squad except Minions.", Requires = TechType.Padding },
            new TechDef { Type = TechType.ShieldWall, Name = "Shield Wall", Icon = "▤", Building = BuildingType.Academy, Tier = 1, Cost = 3, Turns = 2,
                Desc = "Shields learn Shield Wall: +2 DEF to themselves and their neighbours." },
            new TechDef { Type = TechType.PinningShot, Name = "Pinning Shot", Icon = "↧", Building = BuildingType.Academy, Tier = 1, Cost = 3, Turns = 2,
                Desc = "Bows learn Pinning Shot: the target can't move next turn." },
            new TechDef { Type = TechType.Arcana, Name = "Arcana", Icon = "✧", Building = BuildingType.Academy, Tier = 1, Cost = 3, Turns = 2,
                Desc = "Mend heals 2 more and Soothing Aura heals 2. +1 mana each turn." },
            new TechDef { Type = TechType.ForcedMarch, Name = "Forced March", Icon = "»", Building = BuildingType.Academy, Tier = 2, Cost = 7, Turns = 3,
                Desc = "+1 MOVE for every squad.", Requires = TechType.ShieldWall },
            new TechDef { Type = TechType.PiercingShot, Name = "Piercing Shot", Icon = "⊕", Building = BuildingType.Academy, Tier = 2, Cost = 5, Turns = 2,
                Desc = "Crossbows learn Piercing Shot: a bolt that ignores DEF.", Requires = TechType.PinningShot },
            new TechDef { Type = TechType.Firestorm, Name = "Firestorm", Icon = "♨", Building = BuildingType.Academy, Tier = 2, Cost = 5, Turns = 2,
                Desc = "Pyromancers learn Firestorm: burn a tile and everything beside it.", Requires = TechType.Arcana },
        };

        static readonly PoiDef[] pois =
        {
            null,
            new PoiDef { Type = PoiType.GoldMine, Name = "Gold Mine", Icon = "⛏", Desc = "+2 gold each turn to whoever holds it" },
            new PoiDef { Type = PoiType.Library, Name = "Library", Icon = "✎", Desc = "+2 research each turn, and research runs twice as fast" },
            new PoiDef { Type = PoiType.Shrine, Name = "Shrine", Icon = "✧", Desc = "+1 mana each turn to whoever holds it" },
            new PoiDef { Type = PoiType.Outpost, Name = "Outpost", Icon = "⚑", Desc = "Sees 3 tiles; its holder can recruit beside it" },
            new PoiDef { Type = PoiType.Spring, Name = "Spring", Icon = "❂", Desc = "Heals the squad standing on it 2 HP each turn" },
        };

        public static UnitDef Unit(UnitType t) => units[(int)t];
        public static BuildingDef Building(BuildingType t) => buildings[(int)t];
        public static SpellDef Spell(SpellType t) => spells[(int)t];
        public static AbilityDef Ability(AbilityType t) => abilities[(int)t];
        public static TechDef Tech(TechType t) => techs[(int)t];
        public static PoiDef Poi(PoiType t) => pois[(int)t];
        public static AbilityDef AbilityOf(UnitType t) => Ability(Unit(t).Ability);

        public static readonly UnitType[] Roster =
        {
            UnitType.Shield, UnitType.Sword, UnitType.Bow, UnitType.Knight,
            UnitType.Spear, UnitType.Healer, UnitType.Twinblade, UnitType.Crossbow,
            UnitType.Rider, UnitType.Pyromancer, UnitType.Ninja, UnitType.Giant,
            UnitType.Druid, UnitType.Wyvern, UnitType.Minions,
        };
        public static readonly BuildingType[] Buildable =
        {
            BuildingType.Barracks, BuildingType.ManaWell, BuildingType.Watchtower, BuildingType.Wall,
            BuildingType.Forge, BuildingType.Academy,
        };
        /// <summary>Every spell, in loadout order. The enemy always uses the first four.</summary>
        public static readonly SpellType[] AllSpells =
        {
            SpellType.Meteor, SpellType.Ward, SpellType.Haste, SpellType.Bolt,
            SpellType.Renew, SpellType.Rally, SpellType.Stonewall, SpellType.Transmute,
        };
        public static readonly TechType[] AllTechs = (TechType[])System.Enum.GetValues(typeof(TechType));
        public const int SpellSlots = 4;
    }

    /// <summary>Five meaning-carrying colours plus neutrals, from the design notes, and the fantasy board's terrain tones.</summary>
    public static class Palette
    {
        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static readonly Color Background = Hex("#10161a");
        public static readonly Color Panel = Hex("#141b20");
        public static readonly Color PanelStroke = Hex("#2a343b");
        public static readonly Color Row = Hex("#1a2228");
        public static readonly Color RowHover = Hex("#222c33");
        public static readonly Color RowStroke = Hex("#2e3840");
        public static readonly Color IconBox = Hex("#0f1418");
        public static readonly Color Text = Hex("#e9eef0");
        public static readonly Color Muted = Hex("#8b979d");
        public static readonly Color Dim = Hex("#55616a");
        public static readonly Color White = Hex("#f4f7f8");
        public static readonly Color Cyan = Hex("#46b8f2");
        public static readonly Color Red = Hex("#e5484d");
        public static readonly Color Gold = Hex("#f2c14e");
        public static readonly Color Lilac = Hex("#a993f5");
        /// <summary>Research points.</summary>
        public static readonly Color Jade = Hex("#5fd3a2");
        public static readonly Color OrbFill = Hex("#2a2447");
        public static readonly Color Grey = Hex("#7a858b");

        // Map
        public static readonly Color Slab = Hex("#0b1013");
        public static readonly Color Unexplored = Hex("#0d1215");
        public static readonly Color Explored = Hex("#1c2a2c");
        public static readonly Color VisibleA = Hex("#31474a");
        public static readonly Color VisibleB = Hex("#2d4245");
        public static readonly Color MoveFill = Hex("#566a6d");
        public static readonly Color PlaceFill = Hex("#2a5a6b");
        public static readonly Color PlaceChosen = Hex("#3f8099");
        public static readonly Color AttackFill = Hex("#5e353a");
        public static readonly Color SpellFill = Hex("#4a4270");
        public static readonly Color Rock = Hex("#4f6266");
        public static readonly Color Water = Hex("#1f4f6e");
        public static readonly Color WaterDeep = Hex("#173a52");
        public static readonly Color ForestFloor = Hex("#27443a");
        public static readonly Color Tree = Hex("#2f6b4f");
        public static readonly Color Road = Hex("#5a5a4c");
        public static readonly Color Earth = Hex("#3a2f2a");
        public static readonly Color Neutral = Hex("#9aa3a8");

        public static Color Of(Faction f) => f == Faction.Player ? Cyan : Red;
    }
}
