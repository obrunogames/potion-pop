# Potion Pop! — Design System ("Candy Boutique")

Personality: **glossy, bouncy, warm, a little magical**. Everything feels like candy, toys and glowing potions under
soft studio light. Deep plum ink for outlines/text, candy purple as brand color, sunny yellow/gold for rewards and stars,
lime green for primary actions, and each world brings its own accent. Same system as Shelf Pop! (same studio).
Implemented in code by `PotionPop.UI.DS` (tokens) and `PotionPop.UI.UIKit` (components). Reference canvas:
**1080 × 1920** (taller phones keep the width, tablets keep the height — see `ScreenManager`).

## 1. Color tokens (`DS.Colors`)

| Token | Hex | Use |
|---|---|---|
| `Brand` | `#7B4DFF` | brand purple, popup frames, hard levels, Worlds tab tint |
| `BrandDark` | `#5A2FD6` | pressed/borders, nav bar |
| `BrandLight` | `#B79BFF` | highlights, spinners |
| `Ink` | `#3B1F5C` | text on light surfaces, outlines, shades |
| `InkSoft` | `#7A6A92` | secondary text |
| `Cream` | `#FFF6E5` | popup inner, cards |
| `CreamDark` | `#F1DDBF` | inset slots, dividers |
| `White` | `#FFFFFF` | |
| `Primary` (green) | `#58CC02` | primary action (Play, Claim), current level node |
| `Secondary` (blue) | `#1CB0F6` | secondary action, progress fills, "Hidden colors!" tag |
| `Accent` (yellow) | `#FFC800` | rewards, coins, rings |
| `StarGold` / `StarGoldDark` | `#FFC23D` / `#C98A00` | 3-star rings, completed road, Luna's ring, magic avatars |
| `Orange` | `#FF9600` | warnings, streak flame, "Stone bottles!" tag |
| `Pink` | `#FF4B9A` | ribbons, "Current" / "Play" tags |
| `Danger` (red) | `#FF4B4B` | lose, close, badges |
| `Mint` | `#2ED6A1` | success, Profile tab tint |
| `Sky` | `#6FD3FF` | sky gradients |
| `Lavender` | `#B69CFF` | avatar rings, soft backgrounds |
| `Overlay` | `#1A0B33` @ 70% | popup dim |
| `Frost` | `#9EE7FF` | icy accents |
| `Gray` | `#B9B0C7` | disabled controls, locked worlds / nodes |

Palettes: `Candy` (confetti), `Gold` (star & coin bursts).

**World accents** (`DS.AreaAccent(id)`; the catalog `Resources/catalog.json` is the source of truth, these are the
fallbacks `DS.Colors.World*`): Enchanted Forest `#2ED6A1` · Crystal Caves `#4FB3FF` · Candy Kingdom `#FF7EB6` ·
Sky Castle `#A98BFF` · Coral Lagoon `#2EC9D6` · Moonlit Village `#FFA94D`. Helpers: `DS.Darken(c, k)` (3D lips),
`DS.Lighten(c, k)` (tints).

### Potion palette (`PotionPop.Liquids`)

Level data stores color ids 0..11; small levels only use the first 8 (the most distinguishable). Vivid candy tones that
read on every world backdrop. `Liquids.Dark(id)` (×0.78) shades the lower part of a layer, `Liquids.Light(id)`
(45% toward white) is the surface highlight; names are `liquid.<id>` in `Loc/core.csv`.

| id | Name | Hex | | id | Name | Hex |
|---|---|---|---|---|---|---|
| 0 | red | `#FF3B5C` | | 6 | pink | `#FF6FD0` |
| 1 | blue | `#2F6BFF` | | 7 | sky | `#38D4FF` |
| 2 | yellow | `#FFD12E` | | 8 | lime | `#B8F03C` |
| 3 | green | `#22C55E` | | 9 | brown | `#9A5A32` |
| 4 | purple | `#9B4DFF` | | 10 | white (pearl) | `#F4F0FF` |
| 5 | orange | `#FF8A1E` | | 11 | teal | `#14B8A6` |

**Mystery** (hidden "?" unit): `Liquids.Mystery` `#8F88A8`, a misty lavender-gray.

## 2. Typography (`DS.Type`)

Font: **Lilita One** (SIL OFL) as a TMP SDF font asset, dynamic atlas (covers pt/es accents) with a Liberation Sans
fallback. Never draw stars or symbols as glyphs: use icons (`icon_star`...).

| Style | Size | Color | Outline | Underlay (shadow) | Use |
|---|---|---|---|---|---|
| `Display` | 120 | White | Ink 0.28 | Ink, offset (0,-4) | "LEVEL COMPLETE!", big combos |
| `H1` | 84 | White | Ink 0.25 | Ink (0,-3) | titles on ribbons, world names on cards |
| `H2` | 64 | White | Ink 0.22 | Ink (0,-3) | large buttons, level numbers on map nodes |
| `H3` | 50 | White | Ink 0.2 | Ink (0,-2) | medium buttons, tags, counters on dark |
| `Body` | 42 | Ink | none | none | text on cream/white |
| `BodyLight` | 42 | White | Ink 0.18 | none | text on dark/colored surfaces |
| `Small` | 34 | InkSoft | none | none | captions |
| `Badge` | 30 | White | Ink 0.25 | none | badges, plates, bar labels |

Text is always auto-sized down to fit (`enableAutoSizing`, min 60% of the style size).

## 3. Spacing & layout (`DS.Space`)

8-pt grid: `XXS 4 · XS 8 · S 16 · M 24 · L 40 · XL 64 · XXL 96`.
* Screen side margin 32. Top bar height 130 (+ safe area). Bottom nav height 210. Booster bar 230.
* Popups: width 920 (max), inner padding 56, ribbon overlaps the top edge by 60.
* Tap targets ≥ 110 px (`UIButton.ExpandHitArea`). Icons: 64 (inline), 96 (buttons), 140 (side event buttons).
* Backgrounds bleed under notches (`UIKit.Backdrop` / `FullBleed`, 40 px overscan for screen shakes); content stays in
  the safe area. Tab screens use `TabScreen` (ribbon under the top bar, Body between ribbon and nav, content width
  ≤ 1000 for tablets).

## 4. Elevation & surfaces

* `Surface.Popup` — `panel_popup` 9-slice (purple frame, cream inner) + soft drop shadow (black 35%, y -18).
* `Surface.Card` — `panel_card` 9-slice, white with lavender bottom edge.
* `Surface.Inset` — `panel_inset` 9-slice, recessed beige (goal line of Level Start, Star Chest contents).
* `Surface.Pill` — `pill_counter` 9-slice for top-bar counters.
* `Surface.Glass` — capsule white 18% + 35% border over ink 35% (info strips, "★ 37/60 · Levels 21–40").
* **Accent slab** — rounded rect in a world accent over a darker lip 12–14 px lower (`DS.Darken(accent, .45)`): world
  cards, album headers, the world art card of "New world!".

## 5. Motion (`DS.Motion`)

| Token | Value | Use |
|---|---|---|
| `Fast` | 0.12 s | press, hover |
| `Base` | 0.25 s | most transitions |
| `Slow` | 0.45 s | popups, reveals |
| `PopIn` | OutBack(1.4), scale 0.6→1 | popups, rewards, badges |
| `PopOut` | InBack, scale 1→0.7, fade | closing |
| `Press` | scale 0.92 in Fast, OutBack back to 1 | all buttons, cards, nodes |
| `Idle bob` | ±6 px, 2.2 s InOutSine yoyo | side buttons, Luna, album cards on the map |
| `Pulse` | scale 1→1.06, 0.9 s yoyo | primary CTA (Level button, Play), current world card (1.025) and node (1.08) |
| `Fly` | quadratic bezier 0.55–0.8 s InOutCubic, staggered 0.04 s | coins/stars to counters |
| `Stagger` | 0.05 s | lists, grid reveals; world cards / map nodes stagger by distance from the current one |

Screen transitions: 0.25 s cross-fade + 40 px slide in the direction of the tab (non-tab screens such as Worlds and Game
only cross-fade). The level map opens over the world list with a fade + 1.06→1 zoom (0.3 s) and auto-scrolls to the
current level (0.8 s OutCubic); dragging cancels any auto-scroll.

## 6. Components (`UIKit` and friends)

* **Button** — `UIKit.Button(parent, label, ButtonColor, ButtonSize, onClick, icon)`: 9-sliced `btn_<color>`;
  sizes `Small 260×110`, `Medium 420×140`, `Large 600×170`, `Wide 820×170`. Label H2/H3. Press animation, click SFX,
  light haptic. Disabled = gray + 60% alpha. Optional price tag (coin icon + amount) or AD badge.
* **IconButton** — round/square icon with press animation (pause, close, settings, back arrow on `btn_square`).
* **CounterPill** — top bar counter: icon overlapping the left end, value (count-up animation), optional green `+`.
* **ProgressBar** — track + fill + optional label/icon; fill animates; `SetFillColor` tints (blue = in progress,
  art green = done).
* **Badge** — red circle (Danger) with white number or "!" — idle pulse.
* **Toggle** — pill switch (green on / gray off) with knob slide.
* **TabBar** — bottom nav: Shop · Ranking · Home · Collection · Profile; selected tab raised (+30 px), lighter
  background, label shown only when selected. The Worlds screen keeps the Home tab selected.
* **Popup** — overlay fade + panel PopIn; title ribbon; close button (top-right) optional; stackable;
  back button/ESC closes the top popup.
* **Ribbon** — title banner (`ribbon_title`) with H1 text.
* **Toast** — slides from the top under the top bar, auto-hides after 2.2 s.
* **RewardItem** (`MetaUI.RewardItem`) — icon + "x3"/amount; card rewards sit in a `card_frame`.
* **SideEventButton** (`HomeSideButton`) — 150 px icon on a candy disc + caption plate or mini progress bar + badge +
  gold glow/rays when claimable. Home: left Star Chest (x/30) · Daily · Quests, right Lucky Spin · Cards (x/54) · Map.
* **Tag** (`MetaUI.Tag`) — glossy capsule with an optional leading icon: "Hard level!" (Brand + skull), "Free!"
  (Orange + flame), "Current" / "Play" (Pink), feature tags.
* **StarRow** (`MetaUI.StarRow / SetStars / PopStars`) — 3 stars, filled ones golden, empty slots ink @ 42%; optional
  arc (middle star raised, outer ones tilted ±12°); stars pop in one by one with a rising "star" sound.
* **CheckStamp** (`MetaUI.CheckStamp`) — green disc + white check, stamped in with a thump (claimed, completed world).
* **Collection cards** (`CommonUI.CardFront / CardBack / MiniCard`) — gold `card_frame`, world-tinted window, the item
  art `card_<id>`, name band `card.<id>`; missing = `card_back` dimmed with the item's plum silhouette and a "?" disc.
  `MiniCard` drops the name (album strips, map decorations).
* **Avatar** (`CommonUI.Avatar`) — colored ring, cream disc, face `avatar_<id>`. 12 avatars; the 4 magic ones (Luna,
  owl, dragon, unicorn) get a gold ring and a twinkling sparkle in the picker. New players are Luna.

### Worlds screen (`UI/Screens/Worlds`)

* **World card** — 1000×400 accent slab (radius 54, lip 14); the world's home art covers a rounded window (stencil
  `Mask` on `ui_rounded`) focused on the building (46% from the top) with a 12% parallax against the scroll; ink shades
  at the bottom (text) and top (tags); "World N" plate (top-left), name in H1 + "Levels 21–40" (bottom-left), stars
  pill "★ 37/60" (+ crown at 60/60), levels-won bar "12/20 levels". States: **completed** gold check stamp ·
  **current** pink "Current" tag, Luna's avatar, gold glow and a 1.025 pulse · **locked** gray frame, dimmed art,
  padlock + "Reach level N" plate, no stars/bar.
* **Level map** — full-screen panel over the list: the world's blurred `gamebg_<id>` + ink/accent tint + vignette +
  floating motes; header = back button + world-name ribbon + glass info strip. The road is a sine path (amplitude
  ≤ 300, 210 px per level) drawn by `TrailGraphic` (one anti-aliased mesh, 78 px white @ 22%), with a 30 px gold
  ribbon over the part already travelled and white dots beyond. Album cards (`MiniCard`, ±8° tilt, bobbing) decorate
  the roadside opposite the curve; the next world waits at the end of the road (art card + "Next: Crystal Caves",
  padlock until reached). Top and bottom edges fade (`RectMask2D.softness` 48).
* **Map node** — 150 px candy badge: white ring (gold when 3★), darker lip 9 px lower, face color by state (won = world
  accent, current = Primary, hard = Brand, locked = Gray), top gloss, level number in H2; skull on hard levels, small
  padlock on locked ones; best stars (arc) under won nodes; the current node pulses (1.08) inside a gold glow and a slowly
  spinning sunburst, with a pink "Play" tag and Luna's avatar bobbing on the inner side of the curve.

## 7. Bottles & liquids (board, `Scripts/Game/Board`)

Glass and liquid share one parametric silhouette (`Resources/bottle_shape.json`: inner width 1, body 2.62, shoulder,
neck 0.56, lip 0.82, glass 0.075, fill height 3.22 = the mouth for 4 units); `Tools/process_art.py` draws the glass
sprites from it, the game builds the liquid mesh from it, so they always line up. A full bottle is brim-full: every unit
has the same volume, so the top one fills the shoulders and the neck up to the lip (and the pour tilts at least 60°).

* **Layer stack** (per bottle): contact `bottle_shadow` → selection/hint `bottle_glow` (tinted) → `bottle_back`
  (faint cool interior, back-wall highlight) → liquid mesh → "?" marks → inner FX (bubbles, splash) → pour stream →
  `bottle_front` (walls with a dark outer edge, specular streaks, bottom crescent) → cork → stone.
* **Liquid** — stacked bands, each unit a band of its color with `Liquids.Dark` at the bottom and a `Liquids.Light`
  surface line; thin feathered seams between units; the surface stays horizontal while the bottle tilts and wobbles
  (~2 Hz, decaying) after a pour; a diagonal shine over the glass.
* **Selection** — the lifted bottle rises 12% of its height, scales 1.04 and shows the glow in warm gold
  (`#FFDE66`-ish); hints pulse a white glow on source and target.
* **Cork** — `cork` sprite (warm light-brown with a golden star seal) pops in with a "thup", sparkles and a bounce when a
  bottle is completed; completed bottles are inert.
* **Mystery units** — `Liquids.Mystery` bands with an upright "?" (TMP); they flip to their color with a shimmer when
  revealed (pour, Crystal Ball).
* **Stone** — `stone_wrap` (pale cream limestone cocoon with moss and purple crystals) + counter badge (+ AD badge);
  cracks per completed bottle and shatters into `stone_chunk` debris at 0.
* **FX** — `fx_bubble`, `fx_splash` (white, tinted with the poured color), `fx_sparkle`, `fx_poof`.

## 8. Sound & haptics

Every interaction has a sound (see `AudioManager.Sfx`). Haptics: Light (pick/drop/buttons), Medium (pour landing,
booster), Success (win, rewards), Warning (lose, invalid, locked). All toggleable in Settings.

## 9. Iconography & art

Glossy 3D icons with a deep-plum outline (`icon_*`, incl. `icon_map`, `icon_bottle`, `icon_restart`, `icon_pour`),
boosters (`booster_undo`, `booster_shuffle`, `booster_bottle`, `booster_wand`, `booster_rainbow`, `booster_crystal`,
`booster_pack`), rewards (`chest_*`, `coins_*`, `icon_gift`, `hearts_refill`), Luna (`mascot_wave/cheer/sad/point/sleep`),
worlds (`home_<id>`, `gamebg_<id>`), cards (`card_<id>`, `card_frame`, `card_back`), `store_sign` (the world sign on
Home), `logo`. Never draw text inside images — all text is TMP so it can be translated.
