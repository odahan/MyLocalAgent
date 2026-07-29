# Theme audit

## Final Stage 6 result

OD.MyLocalAgent uses semantic palette resources for every application-owned color. Dark is
the default and the primary hardening target. Light remains fully supported by explicit
product decision; it was not removed during Stage 6.

`ThemeService` applies the selected Material base theme and swaps
`Themes/DarkPalette.xaml` or `Themes/LightPalette.xaml`. Both dictionaries expose the same
role keys, and `Themes/ControlStyles.xaml` consumes those roles through dynamic resources.
No page XAML contains an inline color.

The final self-contained `win-x64` publication was inspected at
`%LOCALAPPDATA%\Programs\OD.MyLocalAgent\UI\OD.MyLocalAgent.UI.exe`. The audit covered the
Dashboard, Tasks, task editor, prompt editor, Logs, Tools, Tool tester, AI playground, and
Settings routes. Dark was exercised throughout. Light was selected live in Settings,
visually inspected on the provider and General surfaces, and then changed back to the saved
Dark default.

## Stage 7 Trust filter coverage

The Trust filter route reuses already-audited `TextBox`, multiline read-only editor,
`CheckBox`, `Button`, `ProgressBar`, Material `Card`, `Border` badge, `ListBox`, and
`ScrollViewer` treatments. Settings Guardrails fields reuse the same text, checkbox,
separator, warning, and card styles. No new stock control type or custom template was
introduced. A Dark/Light visual pass of the new route, threshold badge states, long Base64
preview, indicator list, busy/cancel state, and expanded Settings card remains a Stage-7
human follow-up.

## Control inventory

| Control or surface | Palette/template treatment | Final evidence |
|---|---|---|
| `Window`, shell, page backgrounds | `AppBackgroundBrush`, `SurfaceBrush`, `OnBackgroundBrush` | Dark startup and live Light switch inspected |
| Custom title bar and navigation rail | Palette background, divider, text, hover, selected, active/inactive outline | All routes, normal window, focus, hover, and close inspected |
| `TextBlock` | Shared on-background default plus page, section, and caption roles | All pages inspected |
| `Button` | Material outlined/raised/flat bases with palette primary, danger, hover, and text roles | Primary, secondary, danger, title-bar, and disabled contexts inspected |
| Material `Card` and `Snackbar` | Card/on-surface roles and semantic status text | Dashboard cards, Settings cards, save and incident snackbars inspected |
| `TextBox` and multiline editors | Material outlined base; surface, text, caret, divider, selection, validation-error, and disabled roles | Settings, Logs filters, task editor, prompt editor, tool tester, and playground inspected |
| `PasswordBox` | Outlined surface with palette caret, border, error, and disabled roles | OpenAI and Dropbox write-only credential fields inspected |
| `ComboBox` and `ComboBoxItem` | Outlined control plus explicit palette item surface, highlight, selection, and disabled states | Tool Map groups, logging levels, and appearance list inspected in Dark and Light |
| `DataGrid`, headers, rows, cells | Material base; explicit card/surface alternation, divider lines, full-row selection, focus border, and disabled roles | Populated Tasks grid, selected row, headers, checkbox columns, and empty-ready state inspected |
| `CheckBox` and `RadioButton` | Material bases with palette text, border, and disabled states | Tool whitelist, safety gates, S4U, and provider selection inspected |
| `ListBox` / `ListBoxItem` | Material base with palette surface, selection, text, and disabled states | Navigation and selector lists inspected |
| `ListView` / `ListViewItem` | Palette list surface; item style derives from the compatible Material list-box item template | Logs and durable incident lists populated and inspected after the final style correction |
| `Expander` | Material base with card, text, and divider roles | Incident rows and details inspected |
| `ScrollViewer` / `ScrollBar` | Material scrollbar template with palette track, thumb foreground, hover, and disabled roles | Vertical Settings/Tools and horizontal Logs/DataGrid scrolling inspected |
| `ProgressBar` | Material linear template with primary and divider roles | Busy/provider states inspected |
| `Separator`, status dots, badges | Divider plus success, warning, error, muted, card, and primary semantic roles | Connector rows, provider health, validation states, and task status inspected |
| `ToolTip` | Material base with card, text, and divider roles | Title-bar and action tooltips inspected |

## Template boundaries

Material-owned template internals remain intentional for `DataGrid` resize grippers,
checkbox/radio glyphs, the `ComboBox` popup frame, scrollbar arrow buttons and thumbs, focus
ripples, and progress animation. The application controls their semantic foreground,
background, border, selection, error, and disabled inputs rather than forking third-party
templates.

The first Stage 6 interactive pass exposed unavailable
`MaterialDesignComboBoxItem` and `MaterialDesignListViewItem` resource keys. The final styles
no longer reference either key: combo items use the application palette directly, and list
view items derive from the available compatible Material list-box item style. A republished
clean pass opened both populated controls without a new incident.

Windows file dialogs and confirmation message boxes are operating-system surfaces. Their
chrome follows Windows rather than the application resource dictionary; the application
owns the surrounding explanatory text and destructive-action warning.

## Not applicable controls

The current product contains no `DatePicker`, `Calendar`, `Menu`, `ContextMenu`, `ToolBar`,
`TabControl`, `TreeView`, `GroupBox`, `Slider`, `StatusBar`, or `RichTextBox`. They are
therefore not part of the Stage 6 visual gate. Adding one later requires a new entry here and
Dark/Light interaction evidence before release.

## Remaining human visual checks

- Confirm native Save and confirmation dialogs under any Windows high-contrast theme that
  the deployment environment requires.
- Repeat the nine-page pass at non-default display scaling if the target machine uses
  scaling other than 100%.
