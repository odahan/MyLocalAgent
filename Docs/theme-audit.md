# Theme audit

This running audit records the dark and light controls exercised by the Stage 4 UI
foundation. `App.xaml` boots with the dark `BundledTheme`; `ThemeService` then applies the
persisted Material base theme and swaps the matching role dictionary
(`Themes/DarkPalette.xaml` or `Themes/LightPalette.xaml`) before the shell is displayed.
`Themes/ControlStyles.xaml` derives both themes from the same semantic resource keys.

## Stage 4 controls verified

| Control or surface | Theme treatment | Verification |
|---|---|---|
| `Window` and page background | Theme-specific `AppBackgroundBrush` / `OnBackgroundBrush` | Dark and Light running shells inspected |
| Window chrome | Theme-owned title bar, palette system buttons, and one-pixel active/inactive outline | Dark and Light normal states inspected; Dark normal/maximized commands exercised |
| Navigation `ListBox` / `ListBoxItem` | Surface rail, card selection, primary selected foreground | All seven routes inspected |
| `Button` | Forty-pixel centered outlined base; theme-specific high-contrast primary action and palette danger variants | Dashboard and Settings inspected in Dark and Light |
| `MaterialDesignThemes.Wpf.Card` | Palette card surface, foreground, padding, and corner radius | Dashboard and Settings inspected |
| `TextBlock` | Palette foreground plus page, section, and caption variants | All Stage 4 pages inspected |
| `TextBox` | Material outlined base with palette surface, text, caret, and border | Logs and Settings inspected |
| `PasswordBox` | Material outlined base with the same palette roles; API-key value stays empty on load | Settings inspected |
| `ComboBox` | Material outlined base with palette surface, text, and border | Logs and Settings inspected |
| `ProgressBar` | Material linear base with primary and divider brushes | Dashboard and Settings exercised |
| `Snackbar` | Palette card surface and on-surface text | Incident/configuration notification exercised |
| `ToolTip` | Palette card, text, and divider border | Dashboard and diagnostics controls exercised |
| `RadioButton` / `CheckBox` | Material defaults synchronized to the selected base theme | Provider and safety controls inspected in Dark and Light |
| `ListView` / `Expander` | Material defaults over palette-backed containing surfaces | Incident list inspected in Dark and Light |
| `ScrollViewer` / `ScrollBar` | Material defaults synchronized to the selected base theme | Long Settings page inspected in Dark and Light |

No page defines an inline color. Custom colors come only from the two role-compatible palette
dictionaries.

## Pending Stage 6 audit

- `DataGrid`, column headers, row selection, editing controls, and validation chrome.
- `DatePicker`, `Calendar`, and their pop-ups.
- `ComboBox` pop-up item states under every enabled/disabled/validation combination.
- `ScrollBar` hover, pressed, disabled, horizontal, and nested-list states.
- `Menu`, `ContextMenu`, `TabControl`, `TreeView`, `RichTextBox`, and selection colors.
- Native and Material dialog surfaces, including confirmation, file-dialog handoff, and focus
  visuals.
- Disabled, keyboard-focus, high-DPI, long-text, and empty-state passes for every Stage 5
  control.
