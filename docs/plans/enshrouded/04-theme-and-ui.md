# Plan 04 — Enshrouded Theme and UI

**Branch:** `feature/enshrouded-support`
**Depends on:** the game definition from plan 02 (for `ThemeDictionary` / `SkinDictionary`). The dictionaries themselves can be written first.
**Skills:** `enshrouded-theme`, `wpf-mvvm-conventions`, `multi-game-separation`.

## 1. Objective

Give Enshrouded its own skin that reads at a glance as "Enshrouded" (cool slate, translucent panels, thin gold filigree frames, serif small-caps headings) while staying as clean and minimal as the Windrose and Dragonwilds skins. The other two skins stay pixel-identical.

Direction, from the game's own menus (inventory, crafting): dark blue-grey glass panels, hairline gold borders with small circular corner nodes and a short bracket on each corner, warm parchment text, gold selection, green progress bars, an orange-amber highlight for the selected item. We reproduce the *feel* with original vector shapes and colours only. No Keen Games assets.

## 2. Resources

| New file | Content |
|---|---|
| `Resources/Themes/Enshrouded.Colors.xaml` | The 16 contract brushes plus the optional keys (`OrnamentBrush`, `PrimaryButtonForegroundBrush`), `AppDisplayFontFamily`, `GameIconGeometry`, `ShowOrnamentGlyphs`, radial `AppBackgroundBrush`. Values in the skill |
| `Resources/Themes/Enshrouded/Enshrouded.Theme.xaml` | Entry point, merges the five below (same split as Dragonwilds) |
| `Enshrouded.Styles.xaml` | Panel style with the **filigree frame**: a `ControlTemplate` for the panel `Border` that overlays four small corner `Path`s (6 px bracket + 2 px node) in `OrnamentBrush`. Vector, no bitmaps |
| `Enshrouded.Controls.xaml` | Buttons (gold outline primary, slate secondary), combo, list item (selected = `AccentBrush` hairline + `PanelAltBrush` fill, the game's gold selection), progress bar (`SuccessBrush` fill, like the game's durability bars) |
| `Enshrouded.Inputs.xaml` | Text boxes and checkboxes on `InputBrush` |
| `Enshrouded.Effects.xaml` | The ambient effect: a slow drifting **shroud mist** (two large, very low-opacity radial gradients moving over 40–60 s). Off when `SystemParameters.ClientAreaAnimation` is false or the user disabled ambient effects |
| `Enshrouded.Sync.xaml` | Sync-state chips and the indeterminate progress line in theme brushes |
| `Resources/Fonts/` | Display font: **Cinzel** is already embedded for Dragonwilds. Enshrouded uses the same family at a different weight/tracking (no new font; the two skins differ by palette, frame and the small-caps treatment). Owner may choose a second OFL serif (e.g. Cormorant SC) later |

## 3. Views

- `Views/Enshrouded/EnshroudedShellView.xaml(.cs)` wrapping the shared `Views/Shell/GameShellView.xaml`, as the other two do.
- `MainWindow.xaml`: after the plan 02 leak fix, no per-game edit. Until then, add the third `DataTemplate` + `DataTrigger`.
- Header game switch (`ShellHeaderView.xaml`) is an `ItemsControl` over the registry: it shows the third game automatically. Check the width at the 1100 px minimum; if three options overflow, shorten `DisplayName` in the switch to an icon + tooltip at narrow widths (shared change, all games).
- `GameIconGeometry`: a simple original mark, a small flame over a horizontal line (the "flame altar" idea without copying the game's altar art).

## 4. Text

New keys in `ui-text.en.json` with the `Enshrouded.` prefix: `Enshrouded.JoinHint`, `Enshrouded.SaveRootsHint`, `Inspector.PrivacyNote.Enshrouded` ("Only the world file is shared. Characters, settings and the Adventure Sharing list stay on this PC and are never read."). Update `Settings.AmbientEffectsHint` to name all three effects.

## 5. Accessibility

- Contrast table in the skill; `SubtleBrush` is again hint-only.
- Filigree corners are decorative: no information is carried by them; they hide under `ShowOrnamentGlyphs = Collapsed` for the High Contrast fallback.
- Check at 100 %, 150 %, 200 % DPI and at 1100×720.

## 6. Step order

1. Colours dictionary + shell view + registration (the switch shows Enshrouded with the right palette; frames not yet).
2. Styles, controls, inputs, sync dictionaries.
3. Filigree frame template on panels; verify it does not change layout metrics (same padding and radius as the other skins).
4. Ambient mist effect, off by default on battery (reuse the existing ambient toggle).
5. Screenshot comparison of Windrose and Dragonwilds before and after: identical.

## 7. Acceptance criteria

- [ ] Every contract brush and optional key is defined; no hex literal outside `Resources/Themes/`.
- [ ] Switching between all three games at runtime recolours every element, including dialogs opened afterwards.
- [ ] Windrose and Dragonwilds screenshots are unchanged.
- [ ] The frame ornaments render crisply at 150 % DPI (use `SnapsToDevicePixels` / `UseLayoutRounding` on the corner paths).
