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

## Stage 5 controls verified

| Control or surface | Theme treatment | Verification |
|---|---|---|
| `DataGrid` | Explicit card background, on-surface foreground, divider grid lines, alternating surface rows, full-row selection | Empty Tasks grid inspected in Dark |
| `DataGridColumnHeader` | Explicit surface background, muted text, divider borders, and padded semibold labels | All seven task columns inspected in Dark |
| `DataGridRow` / `DataGridCell` | Palette row states, transparent cells, primary keyboard-focus border, centered editing content | Empty and focus-ready states inspected; populated row states remain in Stage 6 |
| `DataGridCheckBoxColumn` / template `CheckBox` | Material checkbox over palette-backed grid cells | Enabled and registered columns rendered in Dark |
| Multiline editor `TextBox` | Shared outlined palette style plus primary selection brush, wrapping or horizontal scrolling selected per editor | Task workflow, task prompt, runner output, tool args/result, and playground prompt/output inspected |
| Grouped `ComboBox` pop-up / `ComboBoxItem` | Explicit card item surface, on-surface text, primary action highlight/selection, primary group headings | Tool Map alias pop-up inspected across Calendar, Dropbox, and File system groups |
| `Separator` | Explicit one-pixel divider brush | Connector test rows inspected |
| `PasswordBox` credential rows | Existing palette style with write-only bindings and no secret read-back | Dropbox access-token, refresh-token, and app-key inputs inspected |
| Status `Ellipse` and badge `Border` | Success, warning, error, muted, card, and divider roles only | Cron validity, connector status, provider health, and model badge inspected |
| Busy and cancellation states | Existing palette `ProgressBar`, primary action, and danger button styles | Playground health/generation and one completed streamed response exercised |
| `WrapPanel` tool whitelist | Material checkboxes over the card role | All five tool groups inspected in the task editor |

The Stage 5 pages introduce no page-local color values. Their colors resolve through semantic
palette resources, and the same control styles remain compatible with the Stage 4 Light
palette deviation.

## Pending Stage 6 audit

- Populated `DataGrid` row selection, checkbox interaction, keyboard navigation, validation
  chrome, internal header grippers, and disabled states.
- `DatePicker`, `Calendar`, and their pop-ups.
- `ComboBox` pop-up item states under disabled and validation combinations; the enabled grouped
  alias pop-up is covered above.
- `ScrollBar` hover, pressed, disabled, horizontal, and nested-list states.
- `Menu`, `ContextMenu`, `TabControl`, `TreeView`, `RichTextBox`, and selection colors.
- Native and Material dialog surfaces, including confirmation, file-dialog handoff, and focus
  visuals.
- Default-template internals still supplied by Material/WPF: `DataGrid` resize grippers,
  `ScrollBar` arrow buttons/thumbs, the `ComboBox` pop-up frame, and native confirmation
  dialogs.
- Disabled, keyboard-focus, high-DPI, long-text, and empty-state passes for every Stage 5
  control.
