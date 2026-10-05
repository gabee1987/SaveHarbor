---
name: dragonwilds-fantasy-theme
description: Visual design system for the RuneScape Dragonwilds mode of SaveHarbor - fantasy-but-minimal palette, typography, ornaments, motion, accessibility, and the WPF theme-switching mechanics. Use when building or changing any Dragonwilds-facing UI or the theme infrastructure.
---

# Dragonwilds Fantasy Theme

Direction: **"lantern-lit map table"** — warm, dark, parchment-and-gold, with a single ember accent.
Fantasy is carried by colour, one display typeface, and a few hairline ornaments. Layout, spacing,
and controls stay as clean as the Windrose theme. If an element is decorative and does not help
scanning, leave it out.

Do not use Jagex/RuneScape logos, artwork, or trademarked fonts. Use original shapes only.

## Palette (brush keys = the existing theme contract)

| Key | Hex | Use |
|---|---|---|
| `AppBackgroundBrush` | `#14110E` | window (subtle radial vignette allowed, see below) |
| `PanelBrush` | `#1E1A15` | cards |
| `PanelAltBrush` | `#191510` | nested areas |
| `InputBrush` | `#110E0B` | inputs/combos |
| `LineBrush` | `#3A3024` | panel borders |
| `LineSoftBrush` | `#2A231B` | dividers |
| `InkBrush` | `#F1E6D0` | primary text (parchment) |
| `MutedBrush` | `#B8A88C` | secondary text |
| `SubtleBrush` | `#7D6F5A` | hints, disabled |
| `AccentBrush` | `#C9A24A` | primary action, focus (antique gold) |
| `AccentHoverBrush` | `#DDB75E` | |
| `AccentPressedBrush` | `#A8853A` | |
| `DangerBrush` | `#E0614F` | errors (dragonfire) |
| `WarnBrush` | `#E89A3C` | warnings (ember) |
| `SuccessBrush` | `#7FBF6A` | success (moss) |
| `InfoBrush` | `#7FA8C9` | info (dusk sky) |

New, Dragonwilds-only optional keys (Windrose dictionary defines them as transparent/neutral so
shared XAML works): `OrnamentBrush` (`#5C4A30`), `PrimaryButtonForegroundBrush` (`#1A140C` on gold).

Measured contrast on `PanelBrush` (WCAG 2.2): Ink 14.0, Muted 7.4, Accent 7.2, Warn 7.5,
Success 7.9, Info 6.9, Danger 4.9, dark text on Accent 7.6. **Subtle is 3.5 → only for
non-essential hints/disabled, never for information the user needs.** Re-check whenever a hex changes.

## Typography

- Body: Segoe UI (unchanged) 13 px.
- Display (window title, section titles, primary button only): **Cinzel** (SIL OFL 1.1),
  embedded as a WPF `Resource` font under `Resources/Fonts/` with its licence file. Fallback: `Georgia`.
  Font file is a third-party asset → note the licence in the repo; no network font loading.
- Keep numerals, timestamps, versions in Segoe UI for legibility.

## Ornaments (all vector, all optional per element)

- Panel: same shape as Windrose (1 px hairline `LineBrush` border, radius 8), only warmer colours. A `Border` style cannot host an inner inset line, so no double rule.
- Section title: small diamond glyph (`Path`, 6×6) before the title in `OrnamentBrush`, shown only when `ShowOrnamentGlyphs` is `Visible`.
- Header divider: plain 1 px `LineSoftBrush` line, identical in both themes (no glyph).
- Primary Play button: gold fill, dark text (`PrimaryButtonForegroundBrush`), Cinzel via `AppDisplayFontFamily`. No highlight overlay (no hex literals outside theme dictionaries).
- Background: `RadialGradientBrush` centre `#1A1611` → edge `#100D0A`. No bitmap textures.

## Motion

- Theme switch: 150 ms cross-fade of the root content opacity. No other new animations.
- Respect `SystemParameters.ClientAreaAnimation == false` → no animations.

## Theme mechanics (WPF)

- Split today's `DarkTheme.xaml` into `Themes/Windrose.Colors.xaml` (brushes only),
  `Themes/Dragonwilds.Colors.xaml` (brushes only), and `Styles/Controls.xaml` (styles, brush-agnostic).
- `App.xaml` merges `Controls.xaml` + the active colors dictionary at index 0.
  `ThemeService.Apply(IGameDefinition)` replaces that merged dictionary.
- Styles and views must reference swappable brushes with **`DynamicResource`**
  (`StaticResource` freezes the value at load). Converting the existing references is part of plan 06.
- Fonts: `AppDisplayFontFamily` resource key (Windrose = Segoe UI Semibold, Dragonwilds = Cinzel).
- Icons per game: `GameIconGeometry` resource (simple original geometry: compass rose for Windrose,
  stylised wing/diamond for Dragonwilds).

## Review checklist

- [ ] All 16 contract brushes + optional keys defined in both dictionaries.
- [ ] No hex literals in views/styles.
- [ ] Contrast checked for text, buttons, toasts, tooltips, disabled states.
- [ ] Looks correct at min size 1040×660 and at 150 % DPI.
- [ ] Switching games at runtime updates every visible element (no stale StaticResource).
