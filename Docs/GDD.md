# Potion Pop! — Game Design Document

Casual "goods sort" puzzle (match-3-identical on shelves), portrait mobile, Unity 6 (6000.6.3f1), uGUI.
Reference: *Goods Sort* (closet.match.pair.matching.games). Our version: livelier, glossy 3D-rendered cute products,
modern boutique shelves, a mascot (Mimi the kitten shopkeeper), juicy animations and a full meta loop.

Languages: English, Portuguese (pt-BR), Spanish. Follows the device language, changeable in Settings.

---

## 1. Core loop

Home → (Level Start popup: pick pre-boosters, costs 1 heart only if you lose) → Level → Win (stars + coins + card) / Lose
(heart lost, streak reset) → Home. Meta systems feed back boosters/coins: Daily reward, Lucky spin, Star chest, Daily
quests, Card collection, Areas (themed stores), Win streak, Leaderboards.

## 2. Board rules (authoritative)

* The board is a grid of **compartments** (shelf cubbies).
  * **Triple** compartment: 3 slots in its *front layer*. Can receive items.
  * **Single** compartment ("pedestal"): 1 slot, it is a **dispenser** — you can take its visible item but never drop
    into it. It shows a badge with the number of items left in its stack. When empty it disappears with a poof.
* Each compartment holds a stack of **layers**. Layer 0 is the front (interactable). Layer 1 is drawn right behind
  the front, smaller, darkened (a "preview"). Deeper layers are hidden (a small badge shows how many layers remain).
* **Move:** drag any front item of an unlocked compartment and drop it into any empty front slot of an unlocked
  Triple compartment (moving inside the same compartment is allowed). Invalid drop → item flies back.
* **Match:** when the 3 front slots of a Triple hold the same product, they pop (match). Stars are earned.
* **Layer advance:** whenever a compartment's front layer becomes completely empty (after a match or after the
  player takes the last item), the next layer slides forward. Empty compartments (no layers left) stay as empty
  cubbies (Triples still accept drops; Singles vanish).
* **Locked compartments:** covered with chains + padlock showing a number N. Each match anywhere decrements N; at 0
  the chains break. The player can also unlock instantly by watching a rewarded ad (AD badge). Locked compartments
  can't be taken from or dropped into, but their front items are visible (dimmed).
* **Win:** all items cleared. **Lose:** timer reaches 0 (offer continue) or player gives up.
* **Stuck:** no empty droppable slot left and no match possible → "No space!" popup offers Shuffle / Hammer
  (or buy), else give up. Shuffle is only usable when it can build a match that frees a slot (otherwise shown as
  "Won't help"); when Shuffle can't help the Wand is offered instead if it would free room; when the only room is
  behind a lock, a free rewarded-ad "Unlock a shelf" button is shown too.
* **Combo:** a match within **8 s** of the previous one increases the combo (x2, x3, …). A thin combo bar under the
  timer shows the remaining window. Stars per match = combo multiplier (x1 = 1 star … capped at x10). Combo floating
  text: "Good!" (x2), "Great!" (x3), "Amazing!" (x4), "Fantastic!" (x5+), plus "Combo xN".

## 3. Level generation & difficulty

Levels are generated procedurally and deterministically from the level number (seed = levelNumber * 7919 + 17)
and **verified solvable** by an internal solver before being used (re-seed on failure).

| Levels | Product types | Triple compartments | Layers (max) | Singles | Locks | Notes |
|---|---|---|---|---|---|---|
| 1 | 2 | 3 | 1 | 0 | 0 | tutorial, 6 items, hand pointer |
| 2 | 3 | 4 | 2 | 0 | 0 | tutorial: layers |
| 3–9 | 3→6 | 4→6 | 2 | 0 | 0 | |
| 10–19 | 5→8 | 6→8 | 2→3 | 0→2 | 0→1 | locks from 12, singles from 15 |
| 20–49 | 7→12 | 8→10 | 3 | 0→3 | 0→2 | |
| 50+ | 10→18 | 9→12 | 3→4 | 0→4 | 0→2 | plateau with noise |

* **Hard levels** (skull icon, purple timer/level button): every level where `n % 10 == 0` from 10 on, and `n % 10 == 5`
  from 45 on. +1 layer, ~15% less time per item, double coin reward.
* Time limit: `clamp(round(items * secondsPerItem + 20), 60, 600)` with secondsPerItem 4.2 (normal) / 3.6 (hard),
  rounded up to a multiple of 5 s. Level 1–2 have 300 s.
* Front layers start with free space: total empty front slots ≥ 3 (≥ 4 on hard levels).
* **Variety (play-test feedback "too many repeated products"):** the table's product count is only a minimum; the
  board uses `max(types, triples × uniqueness)` distinct products — uniqueness 0.55→0.75 (levels 3–9), 0.8→0.95
  (10–19) and **1.0 from level 20 and on every hard level** (every triple is a different product). When the area's
  12 products are not enough, products of earlier areas and then of every area fill in (area products first).
* **Opening view:** at most 2 (levels 3–9) / 1 (from 10) products may have all 3 copies visible at the start; the
  3 copies of a product are spread across layers (window proportional to the board, capped at 20 positions).
* The next board is generated on a worker thread while Home / the win popup is shown (LevelPrefetch), and its
  verified solution seeds the hint system.
* Layout: columns of Triples (2 or 3 columns) optionally with one middle column of Singles; up to 6 rows.
* **Areas:** every 20 levels the store changes theme (products, shelf color, backgrounds, home facade):
  1 Mini Market (1–20), 2 Sweet Bakery (21–40), 3 Toy Store (41–60), 4 Beauty Boutique (61–80), 5 Fruit Market
  (81–100). After 100 the areas cycle and the product pool mixes all unlocked areas.
  A level uses mostly products of its area; from level 25 on, ~30% of types may come from earlier areas.

## 4. Boosters

In-game (bottom bar, unlock progressively — locked ones show a padlock and "Level N"):

| Booster | Unlock | Effect | Price |
|---|---|---|---|
| Hammer | lvl 3 | Targeting mode: tap an item → that item and 2 more of the same product (front first) are smashed (counts as a match). | 3 for 300 coins |
| Magic Wand | lvl 5 | Automatically finds the triple that is closest to completion and makes the 3 items fly together and pop. | 3 for 350 coins |
| Freeze | lvl 7 | Freezes the timer for 15 s (ice frame overlay, timer turns blue). | 3 for 200 coins |
| Shuffle | lvl 9 | Shuffles all items of unlocked front layers (keeps empty slots count), animated. | 3 for 200 coins |

Pre-level (Level Start popup, toggle on/off, consumed when the level starts):

| Booster | Unlock | Effect | Price |
|---|---|---|---|
| +30s Time | lvl 6 | +30 s on the timer. | 3 for 250 coins |
| Bomb | lvl 11 | Clears 2 random triples at the start (explosion FX). | 3 for 300 coins |

Win streak rewards free pre-boosters for the next level: streak ≥ 3 → free Time; ≥ 6 → free Time + Bomb.

## 5. Economy (start values & rewards)

* Start: 300 coins, 5 hearts, boosters: Hammer 2, Wand 1, Freeze 2, Shuffle 2, Time 1, Bomb 1.
* Level win: 20 coins (hard: 40) + 1 collection card. Rewarded ad doubles coins (button on win popup).
* Continue on time out: +60 s for 300 / 600 / 900 coins (escalates per level), or once per level via rewarded ad.
* Hearts: max 5, lose 1 on fail/quit, +1 every 30 min. Refill all for 500 coins, or +1 via rewarded ad.
  "Infinite hearts" timer can be granted by rewards (no heart loss while active).
* Star chest: every 1000 stars → chest with 100 coins + 2 random boosters.
* Daily reward (7-day cycle, missing a day resets to day 1): 50 coins · Hammer · 100 coins · Freeze + Shuffle ·
  150 coins · Wand x2 · Chest (300 coins + 1 of each in-game booster).
* Lucky spin: 1 free per day, +2 per day via rewarded ad. Segments (weight): 25 coins (30), Hammer (12), 50 coins (22),
  Freeze (12), 100 coins (10), Shuffle (8), Wand (5), 250 coins (1).
* Daily quests (3 per day, picked deterministically from the date): Win 3 levels (80 coins), Make 40 matches
  (60 coins), Reach combo x5 (Freeze), Use 2 boosters (60 coins), Collect 150 stars (Hammer), Win a hard level
  (100 coins), Win 2 levels in a row (Shuffle), Clear a level with 30 s left (Wand). Claimable when complete.
* Collection: 5 albums (one per area) × 12 cards (one per product). Each win gives 1 card from unlocked areas
  (70% chance it's a missing one while any is missing). Duplicate = +10 coins. Completing an album: 500 coins +
  1 of each booster.
* Shop: coin packs for watching ads (+60 coins, 5/day), booster bundles for coins, heart refill, daily free gift
  (50 coins once per day). (No real-money IAP in v1; layout leaves room for it.)

## 6. Ads (Google AdMob)

* Banner (adaptive, bottom) during gameplay only; the board layout reserves the banner height.
* Interstitial after a level ends (win or lose) from level 6 on, at most every 2 levels and ≥ 90 s apart; never
  right after a rewarded ad.
* Rewarded: continue (+60 s), double coins, +1 heart, extra spin, shop coins, unlock locked compartment.
* UMP consent form (GDPR) before initializing ads. No tracking: the app never shows the ATT prompt and does not
  declare `NSUserTrackingUsageDescription`; ads on iOS are served without the IDFA.
* Editor: Google's plugin shows placeholder ads; a "simulated ads" toggle exists for offline testing.

## 7. Accounts & cloud save (Firebase)

* Guest by default (local save). Login with **Google** (Android + iOS) or **Apple** (iOS; also shown in editor mock)
  from Profile / Settings / a nudge popup after level 10.
* Firebase Auth via REST (`accounts:signInWithIdp`) with native ID tokens (Android Credential Manager, iOS Google
  Sign-In SDK, Sign in with Apple). Save stored in Firestore `players/{uid}`; global leaderboard reads top 50 by stars.
* Conflict rule on login: keep the save with the higher level (tie → more stars → most recent). Toast informs.
* Sync: after each level, on app pause and every 60 s while dirty. Sign out keeps local save. Delete account
  (required by Apple) removes the Firestore doc and the Firebase user.
* Without Firebase config (empty API key) the game works fully offline; login UI explains that cloud is disabled.
  In the editor a mock provider simulates login + cloud.

## 8. Screens

* **Home:** store facade of the current area (parallax clouds), Mimi idle/waving, top bar (avatar → Profile,
  hearts pill + timer, coins pill +, stars pill), area banner "Mini Market · 7/20", left side buttons (Star chest
  with progress, Daily reward, Quests) and right side (Lucky spin, Collection progress), big pulsing **LEVEL N**
  button (purple with skull if hard) with WIN STREAK badge, bottom nav.
* **Bottom nav:** Shop · Ranking · Home (center, raised) · Collection · Profile. Selected tab is raised, colored,
  icon bounces; badges show claimables.
* **Game:** HUD top (level pill, timer pill — purple with skull when hard, star counter, pause), combo bar, the board,
  booster bar, banner ad area. Tutorial hand on levels 1–2 and on first use of each booster.
* **Popups:** Level Start, Pause, Level Complete, Out of Time, No Space, Level Failed, Out of Lives, Buy Booster,
  Settings (music/sound/vibration/language/account/privacy/version + player ID), Daily Reward, Lucky Spin, Star Chest,
  Quests, Area Unlocked, Reward reveal, New Card, Login, Confirm.
* **Shop / Ranking (Weekly contest + Global) / Collection (albums) / Profile (avatar, name, stats, login).**

## 9. Juice checklist

Item pick-up lift + shadow, drop squash, match: items jump together, flash white, sparkle burst, stars fly to the HUD
counter, camera-less screen shake on big combos, layer advance slide with dust, chains shatter, confetti + mascot on
win, coins fly to the counter with count-up, buttons squash on press, idle bobbing on Home buttons, pulsing Level
button, timer heartbeat in last 10 s, sounds + light haptics for everything.
