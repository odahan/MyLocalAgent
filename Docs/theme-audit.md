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

## Stage 8 Web Search coverage

The Tools page Web Search card is placed immediately after Dropbox and reuses the audited
Material `Card`, `CheckBox`, `TextBox`, `PasswordBox`, `Button`, caption, warning, and status
text styles. The task editor adds one standard whitelist `CheckBox`; Tool tester reuses its
existing alias list, argument editor, and output viewer. No new control type, custom template,
or inline color was introduced.

The cloud label and custom-endpoint warning use the semantic `WarningBrush`; configured,
testing, healthy, and unhealthy states use existing status treatments. A Dark/Light visual
pass remains human-verifiable for long endpoints, an empty/replacement key, disabled/enabled
state, typed connection errors, busy state, the sixth task checkbox, and the optional
`maxResults` Tool tester hint.

## Control inventory

| Control or surface | Palette/template treatment | Final evidence |
|---|---|---|
| `Window`, shell, page backgrounds | `AppBackgroundBrush`, `SurfaceBrush`, `OnBackgroundBrush` | Dark startup and live Light switch inspected |
| Custom title bar and navigation rail | Palette background, divider, text, hover, selected, active/inactive outline | All routes, normal window, focus, hover, and close inspected |
| `TextBlock` | Shared on-background default plus page, section, and caption roles | All pages inspected |
| `Button` | Material outlined/raised/flat bases with palette primary, danger, hover, and text roles | Primary, secondary, danger, title-bar, and disabled contexts inspected |
| Material `Card` and `Snackbar` | Card/on-surface roles and semantic status text | Dashboard cards, Settings cards, save and incident snackbars inspected |
| `TextBox` and multiline editors | Material outlined base; surface, text, caret, divider, selection, validation-error, and disabled roles | Settings, Logs filters, task editor, prompt editor, tool tester, and playground inspected |
| `PasswordBox` | Outlined surface with palette caret, border, error, and disabled roles | OpenAI, Dropbox, and Web Search write-only credential fields; pre-Stage-8 fields inspected |
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

## Stage 11 authorized-location editor

The task editor adds an application-themed `ItemsControl` of authorized locations using the
existing surface, divider, caption, checkbox, and button styles. The folder chooser remains a
native Windows dialog behind `IDesktopInteractionService`. Verify long absolute and UNC paths,
read-only checkbox states, unavailable-location warnings, keyboard focus, and the volume-root
confirmation in both supported palettes.

## Stage 13 AI Workflow Helper modal

The reusable modal window adds application-themed multiline description/refinement inputs,
read-only streamed JSON/Markdown panes, indeterminate progress, validation and cancellation
states, explicit proposal checkboxes, warning text, Text/Rendered Markdown tabs, WebView2
surface, Apply/Retry/Copy/Close commands, and the missing-runtime fallback card. It uses only
palette resources and the existing implicit control styles.

Automated build and ViewModel tests cover state/command enablement, proposal refusal and
acceptance, cancellation, Text/HTML projection, raw-HTML disabling, and restricted-document
content. Human visual follow-up remains required for both palettes, keyboard traversal,
long descriptions/errors/paths, narrow minimum size, WebView2 initialization failure, and
actual rendered Markdown. No Stage 13 GUI or WebView2 runtime flow was manually exercised in
this implementation pass.

## Stage 14 hierarchical AI generation settings

Settings adds one standard Material card containing three outlined numeric text fields for
Temperature, Top P, and Context window. The fields reuse the existing caption, validation,
disabled, focus, surface, and text treatments; no new control type, custom template, or
inline color was introduced. Save is disabled while any generation value is invalid, and
the shared validation text uses the existing error semantic brush.

The Task editor adds two standard checkboxes, two outlined numeric text fields, and compact
effective-value/provenance captions. Each checkbox independently enables or disables its
override field. Prompt editor and AI playground add read-only policy captions to their
existing provider/model status area. Long values, invalid states, disabled inherited fields,
keyboard traversal, and both Dark/Light palettes remain human visual follow-ups.

This implementation pass did not launch the WPF application. Release compilation and
ViewModel tests verify bindings, command state, culture-safe parsing, dirty tracking, reload,
save, and displayed policy text; they are not claimed as pixel-level GUI evidence.

## Stage 15 AI Helper theme and rendered document

The AI Workflow Helper window now explicitly binds its background, foreground, font, cards,
inputs, tabs, captions, warnings, and buttons through the live application resource graph.
Restricted Markdown chooses Light or Dark local CSS from `IThemeService.CurrentTheme`; tests
verify both palettes. The Rendered Markdown control permits its own `NavigateToString`
navigation while continuing to block external navigation, pop-ups, downloads, scripts, and
remote resources.

The published WPF UI was not launched during this pass. Pixel-level confirmation of AI Helper
and AI Explain in both palettes, WebView2 rendering, focus states, keyboard traversal, and the
minimum-size layout remains a human visual check rather than claimed evidence.

## Post-release Tasks diagnostic controls

The Tasks Runner header adds two themed standard checkboxes: Debug (next run) and Large. They
reuse the existing Material checkbox, caption, focus, disabled, foreground, and background
resources. Large adjusts the current non-maximized window height without introducing a new
window surface. The WPF application was not launched for pixel-level verification; Dark/Light,
keyboard focus, narrow layout, maximized-window, and long diagnostic payload behavior remain
human visual checks.
