# Potion Pop! — Design System ("Candy Boutique")

Personality: **glossy, bouncy, warm**. Everything feels like candy and toys under soft studio light. Deep plum ink for
outlines/text, candy purple as brand color, sunny yellow for rewards, lime green for primary actions.
Implemented in code by `PotionPop.DS` (tokens) and `PotionPop.UIKit` (components). Reference canvas: **1080 × 1920**.

## 1. Color tokens (`DS.Colors`)

| Token | Hex | Use |
|---|---|---|
| `Brand` | `#7B4DFF` | brand purple, popup frames, hard levels |
| `BrandDark` | `#5A2FD6` | pressed/borders, nav bar |
| `BrandLight` | `#B79BFF` | highlights |
| `Ink` | `#3B1F5C` | text on light surfaces, outlines |
| `InkSoft` | `#7A6A92` | secondary text |
| `Cream` | `#FFF6E5` | popup inner, cards |
| `CreamDark` | `#F1DDBF` | inset slots, dividers |
| `White` | `#FFFFFF` | |
| `Primary` (green) | `#58CC02` | primary action (Play, Continue) |
| `Secondary` (blue) | `#1CB0F6` | secondary action |
| `Accent` (yellow) | `#FFC800` | rewards, stars, coins |
| `Orange` | `#FF9600` | timers, warnings |
| `Pink` | `#FF4B9A` | ribbons, hearts accents |
| `Danger` (red) | `#FF4B4B` | lose, close, hearts |
| `Mint` | `#2ED6A1` | success, grocery area |
| `Sky` | `#6FD3FF` | home sky top |
| `Lavender` | `#B69CFF` | home sky bottom |
| `Overlay` | `#1A0B33` @ 70% | popup dim |
| `Frost` | `#9EE7FF` | freeze state |

Area accents: grocery `#2ED6A1`, sweets `#FF7EB6`, toys `#4FB3FF`, beauty `#A98BFF`, fresh `#FFA94D`.

## 2. Typography (`DS.Type`)

Font: **Lilita One** (SIL OFL) as a TMP SDF font asset, dynamic atlas (covers pt/es accents).

| Style | Size | Color | Outline | Underlay (shadow) | Use |
|---|---|---|---|---|---|
| `Display` | 120 | White | Ink 0.28 | Ink, offset (0,-4) | "LEVEL COMPLETE!", big combos |
| `H1` | 84 | White | Ink 0.25 | Ink (0,-3) | screen/popup titles on ribbons |
| `H2` | 64 | White | Ink 0.22 | Ink (0,-3) | buttons (large), section headers |
| `H3` | 50 | White | Ink 0.2 | Ink (0,-2) | buttons (medium), counters on dark |
| `Body` | 42 | Ink | none | none | text on cream/white |
| `BodyLight` | 42 | White | Ink 0.18 | none | text on dark/colored surfaces |
| `Small` | 34 | InkSoft | none | none | captions |
| `Badge` | 30 | White | Ink 0.25 | none | badges, counts on icons |

Text is always auto-sized down to fit (`enableAutoSizing`, min 60% of the style size).

## 3. Spacing & layout (`DS.Space`)

8-pt grid: `XXS 4 · XS 8 · S 16 · M 24 · L 40 · XL 64 · XXL 96`.
* Screen side margin 32. Top bar height 130 (+ safe area). Bottom nav height 210. Booster bar 230.
* Popups: width 920 (max), inner padding 56, ribbon overlaps the top edge by 60.
* Tap targets ≥ 110 px. Icons: 64 (inline), 96 (buttons), 140 (side event buttons).

## 4. Elevation & surfaces

* `Surface.Popup` — `panel_popup` 9-slice (purple frame, cream inner) + soft drop shadow (black 35%, y -18, blur via
  `soft_shadow` sprite).
* `Surface.Card` — `panel_card` 9-slice, white with lavender bottom edge.
* `Surface.Inset` — `panel_inset` 9-slice, recessed beige.
* `Surface.Pill` — `pill_counter` 9-slice for top-bar counters.
* `Surface.Glass` — rounded rect (procedural) white @ 18% with 2px white @ 35% border — used over backgrounds.

## 5. Motion (`DS.Motion`)

| Token | Value | Use |
|---|---|---|
| `Fast` | 0.12 s | press, hover |
| `Base` | 0.25 s | most transitions |
| `Slow` | 0.45 s | popups, reveals |
| `PopIn` | OutBack(1.4), scale 0.6→1 | popups, rewards, badges |
| `PopOut` | InBack, scale 1→0.7, fade | closing |
| `Press` | scale 0.92 in Fast, OutBack back to 1 | all buttons |
| `Idle bob` | ±6 px, 2.2 s InOutSine yoyo | home side buttons, mascot |
| `Pulse` | scale 1→1.06, 0.9 s yoyo | primary CTA (Level button) |
| `Fly` | quadratic bezier 0.55–0.8 s InOutCubic, staggered 0.04 s | coins/stars to counters |
| `Stagger` | 0.05 s | lists, grid reveals |

Screen transitions: 0.25 s cross-fade + 40 px slide in the direction of the tab.

## 6. Components (`UIKit`)

* **Button** — `UIKit.Button(parent, label, ButtonColor, ButtonSize, onClick, icon)`: 9-sliced `btn_<color>`;
  sizes `Small 260×110`, `Medium 420×140`, `Large 600×170`, `Wide 820×170`. Label H2/H3. Press animation, click SFX,
  light haptic. Disabled = gray + 60% alpha. Optional price tag (coin icon + amount) or AD badge.
* **IconButton** — round/square icon with press animation (e.g. pause, close, settings).
* **CounterPill** — top bar counter: icon overlapping the left end, value (count-up animation), optional green `+`.
* **ProgressBar** — track + fill + optional label/icon; fill animates.
* **Badge** — red circle (Danger) with white number or "!" — idle pulse.
* **Toggle** — pill switch (green on / gray off) with knob slide.
* **TabBar** — bottom nav: 5 tabs, selected tab raised (+30 px), lighter background, label shown only when selected.
* **Popup** — overlay fade + panel PopIn; title ribbon; close button (top-right) optional; stackable;
  back button/ESC closes the top popup.
* **Ribbon** — title banner (`ribbon_title`) with H1 text.
* **Toast** — slides from the top under the top bar, auto-hides after 2.2 s.
* **RewardItem** — icon + "x3"/amount, used in reward popups and calendars.
* **SideEventButton** — 140 px icon + small caption plate + optional progress/timer/badge (Home side columns).

## 7. Game-board visuals

* Cubby sprites per area (`cubby3_<area>`, `cubby1_<area>`); inner rect from `art_index.json`.
* Items bottom-aligned on the shelf floor, fit inside the slot box (92% width, 88% height).
* Next layer: same slot, scale 0.8, raised 6% of slot height, tinted `#5C4A70` @ 85% alpha (silhouette-like).
* Selected/dragged item: scale 1.18, soft shadow under, sortable above everything.
* Match: items gather to the middle slot (0.15 s), flash white, burst of sparkles + stars flying to the HUD.
* Locked: `lock_chains` overlay, padlock number Badge, AD button below.
* Hard level: purple timer pill with skull, darker vignette.

## 8. Sound & haptics

Every interaction has a sound (see `AudioManager.Sfx`). Haptics: Light (pick/drop/buttons), Medium (match,
booster), Success (win), Warning (lose, invalid). All toggleable in Settings.

## 9. Iconography

Glossy 3D icons with deep-plum outline (`icon_*`), boosters (`booster_*`), rewards (`chest_*`, `coins_*`,
`gift`, `hearts_refill`). Never draw text inside images — all text is TMP so it can be translated.
