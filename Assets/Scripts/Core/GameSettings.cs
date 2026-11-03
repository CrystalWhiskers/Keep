using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Strategy
{
    public enum MapSize { Small, Medium, Large }
    public enum Difficulty { Easy, Normal, Hard }

    /// <summary>Choices made on the main menu, saved between runs.</summary>
    public class GameSettings
    {
        /// <summary>How many paid squad types the player can bring. Minions always come along.</summary>
        public const int MaxSquads = 6;

        public readonly List<UnitType> Loadout = new List<UnitType>();
        /// <summary>The four spells on the mana bar, in slot order (keys 1–4).</summary>
        public readonly List<SpellType> Spells = new List<SpellType>();
        public MapSize Map = MapSize.Medium;
        public Difficulty Difficulty = Difficulty.Normal;
        /// <summary>Ask before ending a turn while squads can still act.</summary>
        public bool WarnEndTurn = true;

        static readonly UnitType[] DefaultLoadout = { UnitType.Shield, UnitType.Sword, UnitType.Bow, UnitType.Knight, UnitType.Healer, UnitType.Ninja };

        /// <summary>Squad types that can go in a loadout (everything except Minions).</summary>
        public static UnitType[] Choices => Defs.Roster.Where(t => t != UnitType.Minions).ToArray();

        /// <summary>The recruit list shown in game: the loadout in roster order, then Minions.</summary>
        public UnitType[] Roster => Defs.Roster.Where(t => t == UnitType.Minions || Loadout.Contains(t)).ToArray();

        public int MapTiles => Map == MapSize.Small ? 10 : Map == MapSize.Large ? 15 : 12;

        /// <summary>Applied to the enemy: (extra starting gold, extra gold per turn).</summary>
        public (int gold, int income) EnemyBonus =>
            Difficulty == Difficulty.Easy ? (-6, -1) : Difficulty == Difficulty.Hard ? (6, 1) : (0, 0);

        public bool Toggle(UnitType t)
        {
            if (Loadout.Remove(t)) return true;
            if (Loadout.Count >= MaxSquads) return false;
            Loadout.Add(t);
            return true;
        }

        public bool Toggle(SpellType s)
        {
            if (Spells.Remove(s)) return true;
            if (Spells.Count >= Defs.SpellSlots) return false;
            Spells.Add(s);
            return true;
        }

        public bool Ready => Loadout.Count > 0 && Spells.Count == Defs.SpellSlots;

        const string Key = "Settings.";

        public static GameSettings Load()
        {
            var s = new GameSettings
            {
                Map = (MapSize)Mathf.Clamp(PlayerPrefs.GetInt(Key + "Map", (int)MapSize.Medium), 0, 2),
                Difficulty = (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt(Key + "Difficulty", (int)Difficulty.Normal), 0, 2),
                WarnEndTurn = PlayerPrefs.GetInt(Key + "WarnEndTurn", 1) == 1,
            };
            var saved = PlayerPrefs.GetString(Key + "Loadout", "");
            foreach (var name in saved.Split(','))
                if (System.Enum.TryParse(name, out UnitType t) && t != UnitType.Minions && !s.Loadout.Contains(t) && s.Loadout.Count < MaxSquads)
                    s.Loadout.Add(t);
            if (s.Loadout.Count == 0) s.Loadout.AddRange(DefaultLoadout);
            foreach (var name in PlayerPrefs.GetString(Key + "Spells", "").Split(','))
                if (System.Enum.TryParse(name, out SpellType sp) && !s.Spells.Contains(sp) && s.Spells.Count < Defs.SpellSlots)
                    s.Spells.Add(sp);
            if (s.Spells.Count != Defs.SpellSlots)
            {
                s.Spells.Clear();
                s.Spells.AddRange(Defs.AllSpells.Take(Defs.SpellSlots));
            }
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetInt(Key + "Map", (int)Map);
            PlayerPrefs.SetInt(Key + "Difficulty", (int)Difficulty);
            PlayerPrefs.SetInt(Key + "WarnEndTurn", WarnEndTurn ? 1 : 0);
            PlayerPrefs.SetString(Key + "Loadout", string.Join(",", Loadout));
            PlayerPrefs.SetString(Key + "Spells", string.Join(",", Spells));
            PlayerPrefs.Save();
        }
    }
}
