# Potion Pop! — Game Design Document

Casual **water-sort** puzzle (sort colored potions into bottles), portrait mobile, Unity 6 (6000.6.3f1), uGUI built in
code. Reference: *Magic Sort* (com.grandgames.magicsort). Our version: glossy magical worlds, a mascot (**Luna**, the
little witch kitten), juicy pouring with a real liquid surface, and the full meta loop of Shelf Pop! (same studio, same
design system "Candy Boutique", same code architecture).

Languages: English, Portuguese (pt-BR), Spanish. Follows the device language, changeable in Settings.

---

## 1. Core loop

Home → (Level Start popup: pick pre-boosters; a heart is only lost if you fail/quit) → Level → Win (1–3 stars + coins +
card) / Fail (heart lost, streak reset) → Home. Meta systems feed back boosters/coins: Daily reward, Lucky spin, Star
chest, Daily quests, Card collection (one album per world), Worlds map (replay levels for 3 stars), Win streak,
Leaderboards (weekly + global), Shop, Profile.

## 2. Board rules (authoritative — implemented in `Scripts/Levels/BoardState.cs`)

* A level has N **bottles**, each holding up to **4 units** of liquid stacked bottom → top. Every color has exactly 4
  units. Usually 2 bottles start empty.
* **Pour:** tap a bottle (it lifts), tap another: the same-color run at the top of the first moves into the second if
  the second is empty or its top has the same color. As many units as fit move (partial pours allowed).
* **Completed bottle:** full of one color → a cork pops in, sparkles; it is inert for the rest of the level.
* **Win:** every bottle is empty or completed.
* **Stuck:** no legal pour left → "No more moves!" popup: Undo / Extra Bottle / Shuffle (owned, or buy), continue
  with +1 empty bottle (300 / 600 / 900 coins, or once via rewarded ad), or give up (lose a heart).
* **Hidden colors ("?")** — from level 15 on some levels: units under the top of some bottles are hidden (misty
  lavender with a "?"). When the units above leave, the new top reveals. Liquid is liquid: hidden units of the poured
  color right under the top pour out too and reveal on the way.
* **Stone bottles** — from level 25 on some levels: a bottle wrapped in stone with a counter N. Every completed bottle
  (and every color removed by the wand) ticks every stone down; at 0 the stone shatters. Can't be poured from/into
  while wrapped. A rewarded ad breaks one immediately (AD badge on the stone).
* **Combo:** completing bottles on consecutive pours (at most 2 pours apart) builds a chain: x2 "Good!", x3 "Great!",
  x4 "Amazing!", x5+ "Fantastic!" (floating text, rising pitch). Only juice + quests.
* **Moves & stars:** the HUD counts pours (undo takes one back). Stars at the win: 3 ★ if moves ≤ `movesFor3Stars`,
  2 ★ if ≤ `movesFor2Stars`, else 1 ★ (thresholds from the generator's solution length `par`:
  ceil(par × 1.3) + 2 and ceil(par × 1.75) + 4). The HUD shows the 3 stars dimming as thresholds pass.
* No timer (relaxing). Levels can always be restarted from Pause (costs a heart if any pour was made).

## 3. Level generation & difficulty (`Scripts/Levels/`)

Generated procedurally and deterministically from the level number (seed = level × 7919 + 17) and **verified
solvable** by the solver (weighted A* with an admissible heuristic) before use; among several solvable candidates the
one with the longest solution is kept. Measured: levels 1–400 all valid, median 5 ms, p90 ~105 ms, max ~360 ms
(desktop). The next level is generated on a worker thread while Home / the win popup is shown (LevelPrefetch).

| Levels | Colors | Empty | Notes |
|---|---|---|---|
| 1 | 2 | 1 | tutorial (fixed board, 3 pours, hand pointer) |
| 2 | 3 | 2 | |
| 3–5 / 6–9 | 4 / 5 | 2 | runs of up to 3 equal units |
| 10–14 / 15–19 | 6 / 7 | 2 | runs ≤ 2; hidden colors from 15 |
| 20–29 / 30–39 | 7→8 / 8→9 | 2 | stones from 25 |
| 40–59 / 60–99 | 9→10 / 10→11 | 2 | hidden deeper (2, then 3 units) |
| 100+ | 10–12 (noise) | 2 | plateau |

* **Hard levels** (skull, purple level button/HUD): `n % 10 == 0` from 10, and `n % 10 == 5` from 45. +1 color, no two
  equal units adjacent, more hidden units, double coins.
* Liquid palette: 12 colors (`Scripts/Core/Liquids.cs`); levels with few colors draw from the 8 most distinct.
* Bottles on screen: up to 14 at start (+2 extra) → laid out in up to 3 rows (max 6–7 per row), scaled to fit.

## 4. Worlds (level groups)

Every **20 levels** form a world with its own theme (home backdrop, game backdrop, accent color, album of 9 cards).
1 Enchanted Forest (1–20), 2 Crystal Caves (21–40), 3 Candy Kingdom (41–60), 4 Sky Castle (61–80), 5 Coral Lagoon
(81–100), 6 Moonlit Village (101–120); then the themes cycle (world 7 = Forest again, numbering continues).

**Worlds screen** (from the world banner on Home): vertical list of world cards (art, name, levels 21–40, stars
earned / 60, progress bar, lock with "Reach level N" for future worlds, "Current" tag). Tapping an unlocked world opens
its **level map**: 20 level nodes on a winding path over the world art; won levels show their best stars (tap to
replay — Level Start popup in replay mode), the current level pulses (tap = play), future levels are locked. Replays
reward only newly earned stars (+10 coins per new star), no card, no streak change; losing a replay costs a heart.

## 5. Boosters

In-game (bottom bar, unlock progressively — locked ones show a padlock and "Level N"):

| Booster | Unlock | Effect | Pack |
|---|---|---|---|
| Undo | lvl 3 | Takes back the last pour (repeatable while history lasts). | 3 for 200 coins |
| Shuffle | lvl 6 | Shuffles the units of all open bottles (counts kept), verified still solvable. | 3 for 250 |
| Extra Bottle | lvl 8 | Adds an empty bottle (max 2 per level, continues included). | 3 for 350 |
| Magic Wand | lvl 12 | Removes one whole color (the most buried one) from every bottle. | 3 for 400 |

Pre-level (Level Start popup, toggle on/off, consumed when the level starts):

| Booster | Unlock | Effect | Pack |
|---|---|---|---|
| Rainbow Potion | lvl 10 | One whole color is removed when the level starts. | 3 for 300 |
| Crystal Ball | lvl 15 | Reveals every hidden color of the level (disabled on levels without hidden colors). | 3 for 250 |

Win streak rewards free pre-boosters for the next level: streak ≥ 3 → free Rainbow; ≥ 6 → Rainbow + Crystal.

## 6. Economy

* Start: 300 coins, 5 hearts, boosters: Undo 3, Wand 1, Extra Bottle 1, Shuffle 2, Crystal 1, Rainbow 1.
* First win of a level: 20 coins (hard: 40) + 1 collection card + stars. Rewarded ad doubles the coins.
* Hearts: max 5, lose 1 on fail/quit/restart-after-moves, +1 every 30 min. Refill for 500 coins or +1 by ad.
* Star chest: every 30 stars → 100 coins + 2 random boosters.
* Daily reward (7-day cycle), Lucky spin (1 free/day + 2 by ad), Daily quests (3/day): win 3 levels (80 coins),
  complete 25 bottles (60), reach a combo x3 (Extra Bottle), use 2 boosters (60), collect 9 stars (Undo), win a hard
  level (100), win 2 in a row (Shuffle), win 2 levels with 3 stars (Wand).
* Collection: 6 albums × 9 cards (one per world). First wins give a card from unlocked worlds (70% a missing one).
  Duplicate = +10 coins. Album complete: 500 coins + 1 of each booster.
* Shop: coins for ads (+60, 5/day), booster packs and bundle for coins, heart refill, daily free gift. No IAP in v1.

## 7. Ads (Google AdMob) — same rules as Shelf Pop!

Banner in the level only; interstitial after a level from level 6 (≤ every 2 levels, ≥ 90 s apart, never right after a
rewarded ad); rewarded: continue (+1 bottle), double coins, +1 heart, extra spin, shop coins, break a stone. UMP
consent before ads; no ATT prompt.

## 8. Accounts & cloud (Firebase, REST) — same as Shelf Pop!

Guest by default; Google (Android/iOS) and Apple (iOS) login; save in Firestore `players/{uid}` (private) and the
public ranking row `leaderboard/{uid}`; reports in `reports/`. Conflict rule: higher level wins (tie → more stars →
newest). Firebase project: see `Docs/Firebase-Setup.md`.

## 9. Screens

* **Home:** world backdrop (parallax clouds/sparkles), Luna idle/waving, top bar (avatar → Profile, hearts, coins,
  stars), world banner "Enchanted Forest · 7/20" → Worlds screen, side buttons (Star chest, Daily reward, Quests |
  Lucky spin, Collection, Worlds map), big pulsing **LEVEL N** button (purple + skull when hard) with win streak
  badge, bottom nav (Shop · Ranking · Home · Collection · Profile).
* **Game:** HUD (level pill, moves counter with the 3 star thresholds, pause), the bottles, booster bar, banner area.
  Tutorial: hand pointer on level 1–2, tips on first hidden colors (15) / first stone (25) / each booster unlock.
* **Popups:** Level Start, Pause (resume / restart / quit), Level Complete (stars fill one by one, coins, card, x2 ad),
  No Moves, Level Failed, Out of Lives, Buy Booster, Booster Intro, Settings, Daily Reward, Lucky Spin, Star Chest,
  Quests, World Unlocked, Reward, New Card, Login, Confirm.

## 10. Juice checklist

Bottle lift with a little tilt; pour arc with the bottle tilting while the liquid surface stays level and a stream
falls into the target; splash + ripple on landing; cork pops in with a "thup", star sparkles and a bounce; hidden layer
flips with a shimmer; stones crack per tick and shatter into chunks; wand pulls the color out as glowing orbs; shuffle
swirl; level-win wave of bouncing bottles + confetti + Luna cheering; coins/stars fly to counters; buttons squash;
pulsing Level button; sounds + light haptics everywhere.
