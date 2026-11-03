# Keep — turn-based strategy (Unity 6)

Built from *Strategy Game UI Mockup.pdf*. Open this folder in Unity 6000.6.5f1 (or newer 6.x), open `Assets/Scenes/Main.unity`, press Play.
If the scene is ever missing, use **Strategy → Rebuild Main Scene**.

Everything (map, units, sky, HUD, icons) is generated in code; there are no imported art assets.

## Look and feel
The board is a floating island in a dusk sky. Under it hangs a jagged root of earth, and loose stones drift beside it. The sky comes from a shader (`Resources/Shaders/Backdrop.shader`): a painted gradient with drifting clouds, twinkling stars and rising embers. Forests are pine groves, water sits below the land, roads are worn paths, and each capturable site has its own model: a mine, a library ruin, a crystal shrine, a palisade outpost and a spring. Fantasy squads (Ninja, Giant, Druid, Wyvern) have their own silhouettes, and the older squads gained capes and hoods.

**Using your own background art:** put an image at `Assets/Resources/Art/Backdrop.png` (any format Unity imports) and the game shows it instead of the shader sky. No code changes are needed.

## Campaign: *The Ashen Host*
Five chapters against the Hollow King. Each one has a fixed map, its own squads and spells, a briefing, lines of dialogue during the battle, and an ending. Winning a chapter unlocks the next. Progress is saved, and you can replay any chapter you've reached.

The research tree opens a branch at a time: each chapter adds new research, and later chapters keep everything earlier ones opened.

| | Chapter | Objective | New this chapter | New research |
|---|---|---|---|---|
| I | Embers at the Border | Capture the enemy Keep | Shield, Sword, Spear · Ward, Bolt | Tempered Blades, Padded Armour, Shield Wall |
| II | The Calder Heights | Capture the enemy Keep | Bow, Crossbow on hilly ground · Meteor | Fletching, Longbows, Pinning Shot |
| III | The Long Night | Survive until turn 10 | Healer, Druid · Stonewall, Renew | Arcana, Steel Plate |
| IV | Riders at the Ford | Capture the enemy Keep | Knight, Rider, Ninja; a river crossed only at fords · Haste, Rally | War Horses, Forced March, Piercing Shot |
| V | The Ember Crown | Capture the enemy Keep | Twinblade, Pyromancer, Giant, Wyvern · Transmute | Firestorm |

Chapter data (story text, maps, rosters, research, enemy bonuses, dialogue) is all in `Scripts/Core/Campaign.cs`.

## Skirmish
- **Squads tab:** pick up to 6 of the 14 squad types for your Recruit list. Minions always come.
- **Spells tab:** pick 4 of the 8 spells for the mana bar (keys 1–4 follow the order you picked them).
- **Map:** Small 10×10, Medium 12×12, Large 15×15. **Enemy:** Easy (−6 starting gold, −1 per turn), Normal, Hard (+6, +1).
- The whole research tree is open in skirmish.
- Choices are saved between runs. The pause menu and the game-over screen both lead back to the menu.

## How to play
- **Goal:** destroy the enemy Keep (20 HP), or in survival chapters keep yours standing. Lose your Keep and it's over.
- **Squads:** click one, then a white tile to move or a red target to attack (it walks into range automatically). Each squad moves once and attacks once per turn.
- **Damage:** a hit deals AD minus the target's DEF, but armour can at most halve it (an AD 4 blow always deals at least 2, and every hit deals at least 1). A target that survives and can reach the attacker hits back.
- **Abilities:** many squads have an active ability (button under the selection panel, or **Q**). Using one takes the squad's action, then it cools down for a few turns. Some abilities are known from the start, and others are learned through research. Shadowstep is the exception: it uses the Ninja's move, so the Ninja can still attack afterwards.
- **Undo:** ↶ (or Z / Backspace) takes back moves, newest first. It is cleared by any other action (attack, ability, spell, recruit, research, end turn), and a move that spots new enemies can't be undone.
- **End-turn warning:** ending the turn while squads can still act asks first. "Go to ready squad" jumps to one; "don't ask again" (or the toggle on the main menu) turns it off.
- **Recruit:** click a unit in the left roster, pick a cyan tile next to your Keep, a Barracks or an Outpost you hold, then press Recruit. Recruits act next turn.
- **Minions** are free and come from a reserve of 10 (+1 per turn). On the Minions card choose **×1 single** or **×4 squad** per recruit.
- **Build anywhere you can see:** open Build (it swaps with Recruit), pick a building, then any lit, open tile: not water, rock, forest, a site or gold. Forward Barracks and Watchtowers are fair game. Buildings take 1–2 turns to finish.
- **Spells:** the top bar spends shared mana (+2 per turn, +1 per Mana Well, +1 per Shrine). Keys 1–4. Transmute is instant; the rest need a target.
- **Fog of war:** black = unexplored, dark = explored, lit = currently seen.
- **Attack forecast:** with a squad selected, hover a red target. The tag over the target shows the damage you'd deal ("KILL" if it dies); a red tag over your squad shows the counterattack ("LOST" if your squad would die). The hint bar spells it out.
- **Threat zones:** hover or select an enemy squad to see every tile it could hit next turn.
- **Danger marker:** a red **!** next to one of your squads means a visible enemy can reach it next turn.
- **Hover hints:** hovering a site, forest, water or road says what it does.

### Terrain
| Ground | Effect |
|---|---|
| Hills | A **ranged** squad on a hill gets **+1 range**, and **+1 AD** against lower targets. Any squad on a hill sees 1 tile further. |
| Forest | Costs 2 moves to enter. **+1 DEF** inside, and squads in a forest are only seen from the next tile, so it hides ambushes. Druids grow forests and Firestorm burns them away. |
| Water | Impassable except to flyers. Chapter IV's river can be crossed only at its fords. |
| Road | Moving from road tile to road tile costs ⅔ of a step, so squads go about half again as far. A road always links the two Keeps. |
| Rock | Impassable except to flyers. |

### Sites to claim
Step any squad onto a site to claim it for your side. It stays yours until an enemy squad steps on it. Every map has one of each type on each half.

| Site | Gives its holder |
|---|---|
| Gold Mine | +2 gold each turn |
| Library | +2 research each turn, and all your research runs twice as fast |
| Shrine | +1 mana each turn |
| Outpost | Sees 3 tiles; you can recruit on the tiles around it |
| Spring | Heals whichever squad stands on it 2 HP a turn (it can't be claimed) |

### Research
Build a **Forge** (weapons and armour) or an **Academy** (drills and abilities) anywhere you can see. Select the finished building to open its research list next to the roster. Each building studies one thing at a time. Research costs **research points (RP)**, shown in jade next to your gold, and takes 2–3 turns. You earn research points every turn:

- +1 from your Keep
- +1 from each Forge or Academy
- +2 from each Library you hold

Tier 2 research needs the tier 1 research named on it. Upgrades apply at once to squads already on the field.

| Building | Research | RP · turns | Effect |
|---|---|---|---|
| Forge | Tempered Blades | 4 · 2 | +1 AD for melee squads (not Minions) |
| Forge | Fletching | 4 · 2 | +1 AD for ranged squads |
| Forge | Padded Armour | 4 · 2 | +2 HP for every squad except Minions |
| Forge | War Horses (needs Blades) | 6 · 2 | +1 MOVE for Knights and Riders |
| Forge | Longbows (needs Fletching) | 6 · 2 | +1 range for Bows and Crossbows |
| Forge | Steel Plate (needs Padding) | 7 · 3 | +1 DEF for every squad except Minions |
| Academy | Shield Wall | 3 · 2 | Shields learn Shield Wall |
| Academy | Pinning Shot | 3 · 2 | Bows learn Pinning Shot |
| Academy | Arcana | 3 · 2 | Mend heals 2 more, Soothing Aura heals 2, +1 mana a turn |
| Academy | Forced March (needs Shield Wall) | 7 · 3 | +1 MOVE for every squad |
| Academy | Piercing Shot (needs Pinning) | 5 · 2 | Crossbows learn Piercing Shot |
| Academy | Firestorm (needs Arcana) | 5 · 2 | Pyromancers learn Firestorm |

### Abilities
| Squad | Ability | Cooldown | Effect |
|---|---|---|---|
| Healer | Mend | 2 | Heal an ally within 2 tiles for 3 HP |
| Knight | Shield Bash | 3 | Hit an adjacent enemy squad and shove it back a tile (+1 damage if it can't move) |
| Twinblade | Whirlwind | 4 | Strike every adjacent enemy once |
| Ninja | Shadowstep | 2 | Blink to an empty tile within 3, over anything; can still attack |
| Giant | Hurl Boulder | 3 | 3 damage to an enemy within 3 tiles |
| Druid | Overgrowth | 2 | Grow a forest on an empty tile within 2 |
| Shield | Shield Wall *(research)* | 3 | +2 DEF to itself and the allies beside it until your next turn |
| Bow | Pinning Shot *(research)* | 3 | Shoot an enemy squad; it can't move on its next turn |
| Crossbow | Piercing Shot *(research)* | 2 | Shoot an enemy, ignoring its DEF |
| Pyromancer | Firestorm *(research)* | 3 | 2 damage on a tile within 2, 1 to enemies around it; burns forests |

Abilities never draw a counterattack.

### Squads added after the mockup
| Squad | Cost | AD/HP/DEF/MOVE | Passive |
|---|---|---|---|
| Crossbow | 5 | 3/4/1/2, range 2 | Steady Aim: +1 AD if it didn't move this turn |
| Rider | 5 | 3/5/1/5 | Charge: +2 AD when it attacks after moving |
| Pyromancer | 4 | 3/4/0/2, range 2 | Scorch: attacks also burn enemies next to the target for 1 |
| Ninja | 4 | 3/4/0/4 | Unseen: its targets can't strike back |
| Giant | 8 | 4/9/1/2 | Thick Hide: no single hit deals it more than 4 damage |
| Druid | 3 | 3/4/0/2, range 2 | Forest Kin: +1 AD and +1 DEF in a forest |
| Wyvern | 5 | 4/5/0/4 | Wings: flies over water, rock, forest and walls |

### Spells added after the mockup
| Spell | Mana | Effect |
|---|---|---|
| Renew | 3 | Heal 2 HP to your squads on a tile and the 8 around it |
| Rally | 2 | +2 AD to a squad until your next turn |
| Stonewall | 2 | Raise a finished Wall on an empty visible tile |
| Transmute | 5 | Instantly turn mana into 3 gold |

| Input | Action |
|---|---|
| Left click | Select / act |
| Right click, Esc | Cancel (Esc with nothing selected opens the menu) |
| WASD / arrows, right- or middle-drag | Pan |
| Scroll | Zoom |
| Space / Enter | End turn (confirms the warning too) |
| Z / Backspace or ↶ | Undo move |
| Q | Selected squad's ability |
| Tab or ▸\| | Jump to next ready squad |
| 1–4 | Spells |
| Minimap click | Move camera |
| M or ♪ button | Music on / off (starts off) |

## Balance
The numbers were tuned with a simulator that plays the enemy AI against itself thousands of times on the real rules (no rendering). The latest results are in `BalanceReport.txt`.

The first pass found and fixed:

- **Fights that never ended.** With plain AD − DEF, most hits did 1 damage and games stalled. Armour now halves a hit at most, and stacked Healers no longer heal one squad several times.
- **An enemy that sniped through fog.** Enemy spells and attacks now need sight, by the same rules as yours.
- **Squad and spell numbers.** Crossbow and Transmute were far ahead of everything else and were cut back. The weakest squads were raised.

The second pass (terrain, sites, research, abilities, new squads):

- **Smaller maps and roads** fixed the slow middle game. Before, AI-vs-AI games often reached turn 40. Now about 10% of medium mirror matches do, and most end around turn 18–25.
- **Hills.** Smaller maps let melee squads close in before high ground mattered: ranged armies won 20% against melee armies with hills or without. Maps now have more hills, and ranged armies win 28% with hills against 18% on the same maps flattened. A Pyromancer with Swords wins 58% against 48%. Pure ranged armies remain weak against the strongest melee mix, so pair them with a front line.
- **New squads:**
  - The Giant started too strong. Its cost went from 7 to 8 and its HP from 10 to 9, and its hide now caps a hit at 4 instead of 3.
  - The Wyvern started too weak. Its cost went from 6 to 5 and its AD from 3 to 4.
  - The Ninja gained 1 HP, and the Druid gained 1 AD.
- **Knight and Rider** cost 5 again. Shield Bash has a 3-turn cooldown and Whirlwind a 4-turn one.
- **Campaign:** enemy bonuses and Chapter III's starting army were re-evened. In the simulator, the AI playing your side wins chapters I–V 80%, 73%, 57%, 63% and 60% of the time (Chapter III is a survival chapter, so it isn't directly comparable).

**Re-running it:** in the editor, **Strategy → Run Balance Report** takes about a minute and writes `BalanceReport.txt` next to `Assets`. It lists win rates for each squad type, each spell, hills vs flat ground, each campaign chapter and each skirmish difficulty. You can also try numbers from the command line without editing code (see the comment at the top of `Scripts/Editor/BalanceSim.cs`), e.g. `-simTune "Bow.Health=4;Transmute.Cost=4"`.

**Caveats:**
- **The simulator is only as smart as the AI.** It plays support squads and defensive tools (Healer, Druid, Shield, Ward, Haste, Renew, Stonewall) poorly, so those score lower than they will in your hands.
- **Heavy melee squads score high.** In mixed armies, most squad types win 45–70%. The Twinblade and Giant sit at the top, around 75%, and Healer, Druid, Spear and Shield sit at the bottom, 25–35%.
- **Nobody has played this pass by hand yet.**

## Temporary soundtrack
`Assets/Resources/TempOST/` holds placeholder music copied from the Limbus Company soundtrack: four battle themes shuffled during play (`Gameplay/`), "Battle Victory" on a win, "Depressed" on a loss. It is someone else's copyrighted music, so it's fine for local testing but must not ship. To swap it, drop your own clips into the same three folders. To remove it, delete `TempOST`; the game runs silent with no code changes. An editor import rule (`Scripts/Editor/TempOstImport.cs`) streams and Vorbis-compresses anything placed there.

## Code map
- `Scripts/Core/Defs.cs` — every number: squads, abilities, buildings, spells, research, sites, terrain costs, economy, colours.
- `Scripts/Core/GameModel.cs` — rules only (map generation with terrain, roads and sites; movement costs; combat; abilities; spells; research; fog; turns).
- `Scripts/Core/Campaign.cs` — the five chapters: story, maps, rosters, research, dialogue.
- `Scripts/Core/AiTactics.cs` — shared AI decisions: marching, abilities, building, research.
- `Scripts/Core/EnemyAI.cs` — the opponent's turn, built on AiTactics.
- `Scripts/Core/GameSettings.cs` — skirmish loadout (squads and spells), map size, difficulty, end-turn warning; saved in PlayerPrefs.
- `Scripts/UI/MainMenu.cs` — the title screen (campaign and skirmish).
- `Scripts/Audio/Music.cs` — playlist, crossfades, victory/defeat music, mute.
- `Scripts/GameManager.cs` — input, selection modes, undo, abilities, research orders, animation, turn flow, campaign flow.
- `Scripts/UI/Hud.cs` — the HUD, laid out on a 1600×900 canvas using the mockup's coordinates, plus the research panel and ability button.
- `Scripts/View/*` — procedural meshes, the floating-island board (terrain, groves, sites), units and buildings, the sky backdrop, camera.
- `Resources/Shaders/Backdrop.shader` — the painted sky.
- `Scripts/Editor/BalanceSim.cs`, `SimAI.cs` — the balance simulator (editor only, not in builds).

Every number lives in `Defs.cs` or `Campaign.cs` (per-chapter enemy bonuses and research). The mockup's squad stats are all kept. The Healer's Mend now works on a cooldown instead of costing the squad's own mana.
