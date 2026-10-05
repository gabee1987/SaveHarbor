# Plan 06 — Per-Game Theming and the Dragonwilds Fantasy Theme

**Branch:** `feature/dragonwilds-support`
**Depends on:** 02 (active-game context). Can run in parallel with 03, 04 and 05.
**Skills:** `dragonwilds-fantasy-theme`, `wpf-mvvm-conventions`, `multi-game-separation`.

## 1. Objective

Give Dragonwilds a warm, fantasy-flavoured look while keeping the interface as clean and minimal as today, and switch themes instantly with the game switch. Windrose keeps its current look exactly.

## 2. Resource restructuring

| New file | Content | Source |
|---|---|---|
| `Resources/Themes/Windrose.Colors.xaml` | The 16 contract brushes with today's values, plus the optional keys (`OrnamentBrush` = `LineSoftBrush` colour, `PrimaryButtonForegroundBrush` = current button text colour), plus `AppDisplayFontFamily` = Segoe UI and `GameIconGeometry` | Extracted from `DarkTheme.xaml` |
| `Resources/Themes/Dragonwilds.Colors.xaml` | Palette from the `dragonwilds-fantasy-theme` skill, `AppDisplayFontFamily` = Cinzel, plus the radial `AppBackgroundBrush` | New |
| `Resources/Styles/Controls.xaml` | All styles from `DarkTheme.xaml`, with brush references converted to `DynamicResource` | Renamed from `DarkTheme.xaml` |
| `Resources/Fonts/Cinzel-*.ttf` + `OFL.txt` | Display font (OFL 1.1) | Third-party asset, licence included |

`App.xaml` merged dictionaries: `[0] Themes/Windrose.Colors.xaml` (replaced at runtime), `[1] Styles/Controls.xaml`.

> Note: setters inside a `Style` that use `{DynamicResource}` are valid in WPF and are re-evaluated when the dictionary changes. `ControlTemplate` triggers that use `StaticResource` brushes must also be converted.

## 3. Theme service

`Services/IThemeService.cs`, `Infrastructure/ThemeService.cs` (about 40 lines):
```csharp
public sealed class ThemeService : IThemeService
{
    public void Apply(IGameDefinition game)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries[0] = new ResourceDictionary { Source = game.ThemeDictionary };
    }
}
```
- Called once at startup (before `MainWindow.Show()`, to avoid a flash) and on `ActiveGameChanged`.
- Cross-fade: `MainWindow` animates the root `Grid.Opacity` 1 → 0.85 → 1 over 150 ms around the swap, and skips it when `SystemParameters.ClientAreaAnimation` is false.
- Index 0 is a deliberate contract. Add a guard comment in `App.xaml` ("index 0 = game colours, swapped by ThemeService").

## 4. Mechanical conversion

1. Replace `{StaticResource <Brush>}` with `{DynamicResource <Brush>}` for the 16 contract brushes and the optional keys in `Controls.xaml` and every view under `Views/`. Leave non-brush resources (converters, styles) as `StaticResource`.
2. Remove the hardcoded `Background="#0D1114"` from `MainWindow.xaml`; use `AppBackgroundBrush`.
3. `rg -n "#[0-9A-Fa-f]{6}" SaveHarbor.App/Views SaveHarbor.App/MainWindow.xaml` must return nothing afterwards. Hex values may live only in `Themes/*.xaml` and in `IndeterminateProgressLineView` gradient stops, which should be converted to theme brushes too.

## 5. Dragonwilds visual details

- **Header**: the app title in `AppDisplayFontFamily`, a game switch (segmented control: two `RadioButton`s with a custom template, `GameIconGeometry` 14 px plus name), and status chips unchanged in structure.
- **Panels**: same `Panel` style and shape in both themes (1 px hairline `LineBrush`, radius 8); Dragonwilds differs only by colour. An inner inset line was dropped because a `Border` style cannot host one.
- **Section titles**: an optional diamond `Path` before the title, `Visibility` bound to a theme resource `ShowOrnamentGlyphs` (`Visibility.Visible` in Dragonwilds, `Collapsed` in Windrose).
- **Primary Play button**: `PrimaryButton` style with `AccentBrush` background, `PrimaryButtonForegroundBrush` text, `AppDisplayFontFamily`. Hover and pressed states use the accent brushes. No highlight overlay.
- **Toasts and tooltips**: brushes only; no shape change.
- **No** bitmap textures, parchment images, glow effects or drop shadows beyond the existing ones. Minimal is a hard requirement.

## 6. Accessibility

- Contrast values are in the skill. Verify the Dragonwilds focus visual (`FocusVisualStyle` with `AccentBrush`, 2 px) on every interactive control.
- Test at 100 %, 150 % and 200 % DPI, and with Windows High Contrast. In High Contrast mode, skip theme dictionaries and fall back to system brushes: `SystemParameters.HighContrast` → apply a minimal `HighContrast.Colors.xaml` that maps contract keys to `SystemColors`.

## 7. Step order

1. Split `DarkTheme.xaml` into Windrose colours plus controls, wire up `ThemeService`. Windrose must look pixel-identical (compare before and after screenshots).
2. DynamicResource conversion and hex cleanup.
3. Dragonwilds colours dictionary, font embedding, ornaments, primary button style.
4. Game switch control in the header and the cross-fade.
5. High Contrast fallback.

## 8. Acceptance criteria

- [ ] Windrose screenshots before and after step 1 are visually identical.
- [ ] Switching game at runtime recolours every element (header, panels, buttons, combo dropdowns, tooltips, toasts, dialogs opened afterwards, progress line).
- [ ] No hex literals outside `Resources/Themes/`.
- [ ] The Cinzel font renders from the embedded resource on a machine without it installed.
- [ ] Contrast checks pass per the skill table.

## 9. Owner decisions

- Font choice: Cinzel (recommended, OFL) or no custom font (Georgia fallback only). The custom font adds about 100 KB and a licence file.
- Whether the Windrose theme gets any refresh: **not planned**. Unchanged by default.
