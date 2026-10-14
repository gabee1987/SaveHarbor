# 04 — Sign-in and Groups UX

What a friend sees from first start to "I can see my group's worlds". Target: under one minute, one e-mail, no browser, nothing to paste except an invite code.

## 1. Principles

- One dialog, three steps, each with a single primary button: **E-mail → Code → Group**. Back is always possible; closing the dialog never leaves half state (a signed-in user without a group is a valid state and the Sharing view says what to do next).
- Every error is a sentence a friend can act on ("That code is wrong or expired. Request a new one."), never an HTTP status.
- Nothing is asked twice: the session is kept on the PC; the group is remembered per game; display name defaults to `PlayerIdentity.DisplayName`.
- Both games share the dialog; it is skinned with the active game's dictionary like `CloudFolderSetupWindow` is (`Resources.MergedDictionaries.Add(skinDictionary)`).

## 2. First-run and provider choice

`MainWindowViewModel.InitializeAsync` today calls `PromptCloudFolderSetupIfNeededAsync`. Replace with `PromptCloudSetupIfNeededAsync`:

1. Developer override active → nothing.
2. `AppSettings.CloudProvider` empty → **Provider choice dialog** (`Views/Dialogs/CloudProviderChoiceWindow`): two cards.
   - **SaveHarbor Cloud (Supabase)** — "Sign in with an e-mail code, join your group with an invite code. Fastest, sees who is playing live." Marked *Recommended*.
   - **Google Drive** — "Use a shared Google Drive folder. Needs a Google account that the group owner has allowed."
   - Choice is saved; "Decide later" closes and leaves the provider empty (the Sharing view then shows a "Choose how to share" button).
3. Provider = Supabase and not signed in, or signed in without a group for the active game → open the **Cloud Group Setup** dialog (§3).
4. Provider = Google Drive → existing folder prompt, unchanged.

The same dialog opens from the Sharing view button (**Set up sharing** / **Change group**) and from Settings.

## 3. Cloud Group Setup dialog (`Views/Dialogs/CloudGroupSetupWindow.xaml` + `.xaml.cs`, view model `ViewModels/CloudGroupSetupViewModel.cs`)

The dialog owns a small view model (unlike `CloudFolderSetupWindow`, which is code-behind only) because it has three states and async work. It receives `ICloudGroupService`, `IPlayerIdentity`, the active `GameId` and the skin.

**Step 1 — E-mail**
- Field: e-mail (validated with `MailAddress.TryCreate`), "Send code" button, hint "We only use this to sign you in. No password, no newsletter."
- On send: `RequestSignInCodeAsync`; success → step 2 with "Code sent to <email>. Check spam if it takes more than a minute." Rate-limit error → show the retry-after time.
- If a session already exists (dialog opened to change group), step 1 is skipped and the header shows "Signed in as <email> · Sign out".

**Step 2 — Code**
- Six-digit field (digits only, auto-advance, paste-friendly), "Sign in" button, "Resend code" link (disabled for 30 s after each send), "Use a different e-mail" link.
- `VerifySignInCodeAsync`; success → `ListGroupsAsync` → step 3. Wrong code → inline message, field cleared, focus kept.

**Step 3 — Group**
- If the user is already in groups: list them (name, members, role), "Use this group" per row. Checkbox "Use for both games" (checked by default when the other game has no group yet).
- "Join with an invite code": 10-character field (upper-cases as you type, allows dashes/spaces that are stripped), display-name field prefilled, "Join".
- "Create a new group": name + display name, "Create". After creation the dialog shows the invite code large, with **Copy** and the sentence "Send this to your friends. Anyone with the code can join."
- `SetActiveGroupAsync(game, groupId, applyToAllGames)` on "Use this group" / after join / after create → dialog closes → the view model refreshes status and overview, toast "Connected to <group>".

Dialog result: `CloudGroupSetupResult(string? GroupId, string? GroupName)`; `IDialogService` gets `ConfigureCloudGroup(skin, gameName, ...)` next to `ConfigureCloudFolder`.

## 4. Sharing view (`Views/Shell/ShellSharingView.xaml`) states for Supabase

| State | Hero line | Button |
|---|---|---|
| Provider not chosen | "Choose how to share your worlds." | Choose |
| Not signed in | "Sign in to share with your group." | Sign in |
| Signed in, no group for this game | "Join or create a group for <game>." | Set up group |
| Connected | "<group> · <n> members · signed in as <email>" | Change |
| Cloud unreachable / paused | the mapped message from plan 03 §3 | Retry |
| Update required (plan 06 §5) | "This version of SaveHarbor is too old for your group's cloud. Update to continue sharing." | Update |

The existing per-world status strip (`CloudStatus.Title/Detail`) and the overview list (`CloudWorldOverview`) are unchanged in shape; Supabase fills them from the snapshot. New per-world line: "Lock expires in 14 min" while a foreign lock is live, "<player>'s session expired at 21:40 — you can take over" when expired (replaces today's silent ignore; also applies to Drive).

## 5. Settings view additions (`ShellSettingsView.xaml`, `SettingsViewModel.cs`)

Section **SHARING**:
- Provider: segmented control `SaveHarbor Cloud | Google Drive` bound to `ICloudProviderSelector`. Switching shows a confirm: "Switch to <provider>? Your worlds' sync state is kept separately per provider; nothing is deleted." Hidden when the developer override is active.
- When Supabase: "Signed in as <email>" + **Sign out**; "Group for <game>: <name>" + **Change**; **Invite code** row (owner only: shows code with Copy and **Rotate**; members: "Ask the group owner for the code"); "Storage used: 312 MB of 1 GB" + **Clean up unused files** (plan 03 §7); **Leave group**.
- When Google Drive: the existing shared-folder rows.
- Common: "Your name for friends" (existing), new "This PC's name for friends" (`DeviceLabel`), "Keep shared versions" (existing; hint text per provider).

## 6. Texts (`ui-text.en.json`, new keys)

`CloudProviderChoice.Title/Supabase*/GoogleDrive*/Later`, `CloudGroup.Title`, `CloudGroup.Email*`, `CloudGroup.Code*`, `CloudGroup.Group*`, `CloudGroup.Invite*`, `CloudGroup.Create*`, `Settings.Sharing`, `Settings.Provider*`, `Settings.SignedInAs`, `Settings.SignOut`, `Settings.Group*`, `Settings.InviteCode*`, `Settings.StorageUsed*`, `Settings.CleanUp*`, `Settings.LeaveGroup*`, `Settings.DeviceLabel*`, `Sharing.State.*` (one per row of §4). Formal, short, no exclamation marks; provider names spelled "SaveHarbor Cloud" in UI and "Supabase" only in Settings' provider description.

## 7. Error texts (`SupabaseErrorMapper`)

| Code | Text |
|---|---|
| `invite_invalid` | "No group has this invite code. Check it with the group owner." |
| `version_conflict` | "Someone shared a newer version while you were uploading. Download it first." |
| `lock_lost` | "Your session lock expired and another player took over." |
| `not_owner` | "Only the group owner can do this." |
| `owner_must_stay` | "The owner cannot leave while others are in the group." |
| `ArchiveTooLarge` | "This world's archive is {size}; the group's cloud allows up to 45 MB." |
| `RateLimited` | "Too many attempts. Try again in {seconds} seconds." |
| `NotSignedIn` | "Your sign-in expired. Sign in again to continue." |
| `CloudUnreachable` | "SaveHarbor cannot reach the group's cloud. Check your connection." (+ pause hint) |

## 8. Accessibility and keyboard

Enter advances each step; Escape closes; the code field announces "6-digit code"; all buttons have `AutomationProperties.Name`; contrast follows the existing theme tokens (`dragonwilds-fantasy-theme` skill for the Dragonwilds skin). No new colours.
