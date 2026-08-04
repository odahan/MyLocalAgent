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
surface, Apply/Retry/Copy/Close commands, and the missing-runtime fallback card. The tabs use
an application-owned `TabItem` template so the system or Material selected-state template
cannot replace the semantic background and foreground brushes with a white-on-white state.

Automated build and ViewModel tests cover state/command enablement, proposal refusal and
acceptance, cancellation, Text/HTML projection, raw-HTML disabling, and restricted-document
content. A local Release build was subsequently exercised in both palettes: the selected tab
kept readable contrast, the response surface filled its card before the first token, completed
Text preserved the streamed Markdown exactly, and the installed WebView2 runtime displayed the
same response as headings, lists, bold/code text, and separators. Keyboard traversal, long
errors/paths, narrow minimum size, and WebView2 initialization failure remain human follow-ups.

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
inputs, tabs, captions, warnings, and buttons through the live application resource graph. Its
application-owned tab template binds the selected header directly to the semantic action and
on-action brushes, while tab content is stretched independently from the centered header.
Restricted Markdown chooses Light or Dark local CSS from `IThemeService.CurrentTheme`; tests
verify both palettes. The Rendered Markdown control permits its own `NavigateToString`
navigation while continuing to block external navigation, pop-ups, downloads, scripts, and
remote resources.

A separate local Release UI was launched and AI Explain was inspected in both Dark and Light.
The selected Text and Rendered Markdown headers remained legible in each palette, the empty
response host occupied its full width and height immediately, and WebView2 rendered the
completed document in each palette. Focus states, keyboard traversal, the minimum-size layout,
and the missing-WebView2 fallback remain human visual checks.

## Post-release Tasks diagnostic controls

The Tasks Runner header adds three themed standard checkboxes: Debug (next run), Auto-scroll,
and Large. They reuse the existing Material checkbox, caption, focus, disabled, foreground, and
background resources. Large changes only the existing Runner card height by 300 pixels and does
not resize or reposition the application window. A local Release build completed two full
normal/large cycles (190 → 490 → 190 → 490 → 190 pixels); the application window stayed fixed
and the lower output remained reachable through page scrolling. Keyboard focus, narrow layout,
maximized-window, live auto-scroll with a long-running Runner, and long diagnostic payload
behavior remain human visual checks.

The Tasks page uses the existing themed vertical `ScrollViewer` treatment around its content.
The task grid keeps a bounded height, while Large changes Runner Output from 190 to 490 pixels;
the page can therefore scroll without introducing a new surface, palette color, control
template, or window resize.

## Stage 17 workflow authoring aids

The Tool catalog and the Workflow prompt editor are two new modal windows opened from the
Workflow JSON card. Both reuse the existing Material window chrome, `DynamicResource` palette
brushes, caption and body typography, button styles, list and text-box templates, and the
shared scroll treatment. Neither introduces a new palette color, control template, custom
brush, or hard-coded color, and neither resizes or repositions the application window. Both
windows resolve their palette from `IThemeService.CurrentTheme` in the same way as the
existing AI Helper and AI Explain windows, so switching Light/Dark applies to them through
the same mechanism.

The Tool catalog hosts a search box, an alias list, a read-only detail pane with the typed
argument table, a read-only snippet preview, and the Insert / Insert and close / Close /
Cancel commands. The Workflow prompt editor hosts the ordered `ai.prompt` list, a multiline
prompt text box, the `includeContext` checkbox, the read-only Safety summary, the duplication
warning banner, and the Apply / Revert / Cancel commands. Warning banners reuse the existing
warning callout resources rather than literal colors.

✅ **Verified in the GUI.** A manual operator session covered both windows: Light and Dark
rendering, legibility of the argument table and snippet preview, the warning banner contrast,
keyboard focus states and traversal order, the minimum-size and narrow layouts, the
maximized-window layout, the long-alias and long-prompt overflow behavior, and live theme
switching while either window is open. No palette, template, or window-sizing regression was
observed.

Three post-implementation corrections were applied and visually confirmed in the same session:
the Insert commands are hidden on the Placeholders tab so only Copy and Close remain, the
prompt text box wraps with the horizontal scroll bar disabled, and workflow validation raises
a short snackbar notification in addition to the detailed status line.

## Stage 20 Prompt editor placeholder help and warnings

Prompt editor adds one wrapping help line and one conditional warning callout above the existing
agent-prompt card. Help uses `MutedTextBrush`; the callout uses `SurfaceBrush` for its background
and `WarningBrush` for border and text. These are shared semantic dynamic resources present in
both Light and Dark palettes. No inline color, new brush key, template, page, modal, theme, or
switcher was added. The warning is non-blocking and does not alter the streamed-output surface.

Release XAML compilation and ViewModel tests verified resource names, bindings, warning lifecycle,
and command behavior. The application was not launched during the Stage 20 pass, so live contrast,
wrapping under narrow width, keyboard reading order, and high-contrast behavior in both Light and
Dark remain explicit manual visual checks.

## Stage 18A metadata-driven tool whitelist

The Task editor's six concrete CheckBox declarations were replaced by one `ItemsControl` whose
items come from the ordered Core task-whitelist catalog. Its `WrapPanel` preserves the existing
responsive layout, and each generated Material CheckBox uses the catalog display label and
description tooltip. No inline color, new brush, control template, page, modal, or theme resource
was introduced.

Release XAML compilation and ViewModel tests verify the generated item order, selection binding,
dirty state, and metadata-driven Authorized locations visibility. The application was not launched
for this pass, so wrapping at narrow widths, tooltip contrast, keyboard traversal, and focus visuals
in Light and Dark remain explicit manual checks.

## Stage 18C Recycle Bin Safety setting

Settings adds one standard Material checkbox, **Allow moving files to the Recycle Bin**, and one
caption in the existing Safety gates stack. It reuses the current checkbox, caption, spacing,
focus, disabled, and Light/Dark palette resources; no new brush, template, page, dialog, or theme
was introduced. Release XAML compilation and ViewModel tests cover the binding and persistence.
The application was not launched for Stage 18C, so live wrapping, keyboard traversal, and
Light/Dark focus and contrast remain explicit manual checks.
