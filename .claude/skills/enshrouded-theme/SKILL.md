---
name: enshrouded-theme
description: Visual design system for the Enshrouded mode of SaveHarbor - cool slate glass panels with gold filigree corner frames, parchment text, serif small-caps headings, the shroud-mist ambient effect, palette with contrast values, and the WPF skin files. Use when building or changing any Enshrouded-facing UI, the companion page colours, or the theme infrastructure.
---

# Enshrouded Theme

Direction: **"lantern in the shroud"** — cool blue-grey glass, warm parchment text, thin antique-gold frames with small corner nodes, one amber highlight. It must read as Enshrouded next to Dragonwilds (which is warm brown/gold) and Windrose (neutral dark/red), while layout, spacing and controls stay as minimal as both.

Inspiration is the game's own inventory and crafting menus: translucent slate panels, hairline gold borders whose corners carry a short bracket and a small circle, gold selection frames, green durability bars, amber for the selected item. Reproduce the feel with **original vector shapes and colours only**. No Keen Games artwork, logos, textures, icons or fonts.

## Palette (brush keys = the theme contract)

| Key | Hex | Use |
|---|---|---|
| `AppBackgroundBrush` | radial `#1B2127` centre → `#111518` edge | window |
| `PanelBrush` | `#232B33` | cards (the "glass" slate) |
| `PanelAltBrush` | `#1C2229` | nested areas, selected row fill |
| `InputBrush` | `#151A1F` | inputs/combos |
| `LineBrush` | `#3C4650` | panel borders |
| `LineSoftBrush` | `#2C343C` | dividers |
| `InkBrush` | `#EDE4D0` | primary text (parchment) |
| `MutedBrush` | `#B3AE9F` | secondary text |
| `SubtleBrush` | `#7D7A6E` | hints, disabled |
| `AccentBrush` | `#CDAA5E` | primary action, focus, selection frame (antique gold) |
| `AccentHoverBrush` | `#DEBD72` | |
| `AccentPressedBrush` | `#A98A48` | |
| `DangerBrush` | `#E8705F` | errors |
| `WarnBrush` | `#E39A46` | warnings (the game's amber highlight) |
| `SuccessBrush` | `#86C66C` | success (durability green) |
| `InfoBrush` | `#8DB4D6` | info (shroud blue) |
| `OrnamentBrush` | `#8F7A48` | filigree corners (dimmer gold) |
| `PrimaryButtonForegroundBrush` | `#1A1710` | dark text on gold |

Measured contrast on `PanelBrush` (WCAG 2.2): Ink 11.3, Muted 6.5, Accent 6.5, Warn 6.1, Success 7.0, Info 6.6, Danger 4.7, dark text on Accent 8.1, Ink on background 13.7. **Subtle is 3.3 → hints and disabled only, never information the user needs.** Re-check whenever a hex changes.

Companion page (`web/src/styles.css`, `data-game='enshrouded'`): dark values above (`--bg #111518`, `--bg-top #1B2127`, `--accent-ink #1A1710`); light variant keeps the hues at ≥ 4.5:1 on its panel: `--bg #E6E9EC`, `--bg-top #F3F5F7`, `--panel #FFFFFF`, `--panel-alt #F3F5F7`, `--line #C7CDD3`, `--line-soft #DDE2E6`, `--ink #1A1F24`, `--muted #4F5760`, `--accent #7A5F1E`, `--accent-ink #FFFFFF`, `--danger #A8321F`, `--warn #875310`, `--success #2F6B26`, `--info #2A5E86`, `--display Georgia, 'Times New Roman', serif`.

## Typography

- Body: Segoe UI 13 px (unchanged).
- Display (window title, section titles, primary button): **Cinzel**, already embedded for Dragonwilds (OFL 1.1); no new font. Cinzel is an all-caps design, so the game's small-caps look comes for free: use it at 13 px for titles and 14 px for the Play button. Do not add letter-spacing or `Typography.Capitals` tricks; WPF renders them poorly at 150 % DPI. The skins differ by palette and frame, not by typeface.
- Numbers, timestamps, versions: Segoe UI.

## Ornaments (vector, decorative, hidden in High Contrast)

The shape language comes from the game's announcement art: **diamonds (gems)**, small ring nodes, "»  TITLE  «" chevrons and a flame. All shapes are original; corner marks, crests and caps sit under `Visibility={DynamicResource ShowOrnamentGlyphs}` and ignore input.

- **Frame** (`SkinFrame`): metal-gradient hairline (`SkinFrameMetalBrush`), inner `SkinFrameInnerBrush` line, a faint warm "lantern" light on the top 90 px (`SkinLanternGlowBrush`). Each corner (`SkinCornerTemplate`) has a gold gem on the corner, an outer and an inner bracket and a ring node at the end of each arm; a crest (ring, gem, ring) sits on the top edge. Featured panels (hero, Settings) set `BorderBrush` to `SkinGoldBrush`.
- **Section title**: a small circle before the title, rule ending in "o o o".
- **World rows** (`SkinWorldItem`): a gem badge with a flame; the selected row's flame is lit (`SkinFlameBrush`) inside a gold frame.
- **Primary Play button**: gold plate, engraved inner line, dark text, a gold gem at each end. **Secondary buttons**: dark slate plate (`SkinButtonPlateBrush`), metal edge, a small gem at each end that hangs 4 px outside the plate (so the button is as wide as in the other skins); hover lights the gems and the top sheen.
- **Wordmark** (`SkinWordmarkTemplate`): a gem with the flame, "ENSHROUDED" with a raised initial, and "» SAVEHARBOR «" between fading rules. Our own composition in Cinzel, not the game's logo.
- **Game switch** (`SkinGameSwitchFrame` + `SkinGameSwitch`): a dark band with gold hairlines between "»" and "«"; a gem before each name, the active one lit gold.
- **Flame** (`SkinFlameGeometry`, `SkinFlameCoreGeometry`): one original flame on a 24 x 32 grid, used by the wordmark, the world rows and the loader. **Game icon** (`GameIconGeometry`): a small flame above a short line (14 px).
- **Background**: radial gradient plus the ambient effect below; no bitmaps, no parchment, no blur.

## Motion

- Theme switch: the shared 150 ms cross-fade.
- **Loader** (`SkinArcaneLoaderTemplate`, the "flame altar"): a flickering flame in a gold gem (two flicker rates that do not divide evenly), a rune ring with ticks turning once in 24 s, violet/blue shroud spores circling just outside the flame's light, sparks rising. Exists only while busy.
- **Ambient effect** (`ShroudMistView`): three rune circles (rings, ticks, nodes, eight-point star; 12–16 % opacity in `SkinRuneColor`) turning once in 150–240 s, two blue mist glows and a violet shroud bank (`SkinShroudColor`) drifting and breathing, 26 soft spores rising and fading. Circles and glows are cached bitmaps that only move; no effects on the spores; at most 20 frames a second. Respects the ambient-effects setting and `SystemParameters.ClientAreaAnimation` (static when off).

## Theme mechanics (WPF)

- `Resources/Themes/Enshrouded.Colors.xaml` (brushes, font, icon, `ShowOrnamentGlyphs`) is swapped at merged-dictionary index 0 by `ThemeService`.
- `Resources/Themes/Enshrouded/Enshrouded.Theme.xaml` merges `Styles`, `Controls`, `Inputs`, `Effects`, `Sync` (same split as Dragonwilds; copy and change only what differs).
- All brush references are `DynamicResource`. No hex literals outside `Resources/Themes/`.

## Review checklist

- [ ] All 16 contract brushes + optional keys defined.
- [ ] Windrose and Dragonwilds screenshots unchanged.
- [ ] Corner marks crisp at 100 % and 150 % DPI; hidden when `ShowOrnamentGlyphs` is Collapsed.
- [ ] Contrast checked for text, buttons, chips, toasts, tooltips, disabled states.
- [ ] 1100×720 minimum size: three-game switch fits in the header.
