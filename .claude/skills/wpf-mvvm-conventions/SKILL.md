---
name: wpf-mvvm-conventions
description: How SaveHarbor writes WPF views and CommunityToolkit.Mvvm view models - commands, busy handling, toasts/activity, UiText localisation, resource keys. Use when adding or changing UI, commands, or bindings.
---

# WPF + MVVM Conventions (SaveHarbor)

## View model

- One main VM: `MainWindowViewModel` (`ObservableObject`), split into partial files by concern:
  `.LocalCommands`, `.CloudCommands`, `.Operations`, `.ComputedProperties`,
  `.PropertyChangeHandlers`, `.GameMonitor`, `.Toasts`. Add a new partial for a new concern
  (e.g. `.GameSelection.cs`) rather than growing an existing one past ~300 lines.
- Observable state: `[ObservableProperty] private T field;` plus
  `[NotifyCanExecuteChangedFor(nameof(XCommand))]` where it drives command availability.
- Commands: `[RelayCommand(CanExecute = nameof(Predicate))] private async Task XAsync()`.
  Generated name is `XCommand`. **Register every new command in `NotifyCommandStates()`.**
- Long operations go through `RunBusyAsync(busyText, action)`: sets `IsBusy`, updates
  `StatusText`, catches exceptions → `IAppErrorHandler` → activity + toast + dialog, always
  refreshes game status and command states. Do not hand-roll try/catch for UI operations.
- Feedback triple for user-visible outcomes, in this order:
  `StatusText = msg; AddActivity(level, msg); _toastService.Success|Warning|Info|Error(title, msg);`
  Use `_dialogService.ShowError` only for blocking problems the user must read.
- Confirm destructive operations with `_dialogService.Confirm(title, message)` and explain
  the safety net (automatic backup) in the message.
- Re-check `UpdateGameStatus()` immediately before any save-file mutation; block if running.

## Views

- Sections are `UserControl`s in `Views/Controls` bound to the inherited main VM DataContext.
  `MainWindow.xaml` only lays them out.
- No code-behind logic except `InitializeComponent` and pure visual behaviour.
- Text: `{ui:UiText Some.Key}` from `Resources/Texts/ui-text.en.json`. Keys are
  `Section.Name` / `Section.NameTooltip`. Every interactive control gets a tooltip key.
- Colours/styles: only `{StaticResource|DynamicResource BrushKey}` from the theme dictionary.
  **Never hardcode hex colours in views** (`MainWindow.xaml` `Background="#0D1114"` is legacy;
  remove it when theming work touches that file). For runtime theme switching, brushes that
  change per game must be referenced with `DynamicResource` (see `dragonwilds-fantasy-theme`).
- Existing style keys: `Panel`, `HeaderChip`, `HeaderChipLabel`, `HeaderChipValue`, `MutedText`,
  `MicroLabel`, `SectionTitle`, plus brushes `AppBackgroundBrush`, `PanelBrush`, `PanelAltBrush`,
  `InputBrush`, `LineBrush`, `LineSoftBrush`, `InkBrush`, `MutedBrush`, `SubtleBrush`,
  `AccentBrush`, `AccentHoverBrush`, `AccentPressedBrush`, `DangerBrush`, `WarnBrush`,
  `SuccessBrush`, `InfoBrush`. New themes must define **every** one of these keys.
- Busy indication: `IndeterminateProgressLineView` bound to `IsBusy`; toasts via `ToastHostView`.

## Dialogs

`IDialogService` (`WpfDialogService`) owns all windows. Dialogs use `AppDialogWindow` styling.
VMs never instantiate windows.

## UX rules

- One obvious primary action (Play). Secondary actions grouped and visually quieter.
- Disabled buttons must explain why via tooltip or adjacent status text.
- Never block the UI thread with IO; all IO is `async`.
- Keep the window usable at its minimum size (1040×660).
