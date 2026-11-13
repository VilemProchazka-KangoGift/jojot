# Visual refresh experiment

Branch: `features/avalonia-visual-refresh`, based on stable main `c5d7f77051ff1ff7b6a1aa85fbd0e44422b149c1`. The native-caption, rename and Find refinements are uncommitted.

Open [the side-by-side gallery](index.html). Run the isolated NativeAOT preview with `P:\projects\JoJot\.local\avalonia-visual-refresh\chrome-preview\Try-Visual-Refresh.cmd`.

## Design

Warm paper for writing, cool slate navigation, restrained teal accents. Menu, Search and New share one compact row, matching the stable layout while retaining the refreshed controls. The redundant JoJot heading is removed. Selected notes use a rounded surface, accent edge and stronger title weight. Shared controls have consistent rounding and theme-aware selected-state text. The editor gets more breathing room while preserving the existing Consolas/font-size preferences and plain-text content.

The default sidebar is 220 DIP (existing saved widths still apply). At narrow window sizes its maximum yields enough room for the editor; the toolbar wraps instead of clipping actions. No schema, storage, file format, shortcuts or workflow handlers changed.

## Actual visual verification

The gallery compares stable managed and refreshed NativeAOT client-area renders at 1000 × 680 DIP, Windows 150% scaling, same six synthetic notes, same font preferences. Stable includes only the off-screen/nonactivation capture guard; its visual styles/layout are unchanged. The old 180-DIP and refreshed 220-DIP sidebar defaults are intentional design differences. Native title-bar chrome is outside these captures.

The initial inline-layout native probe produced 13 images: light, dark, Find with two real matches, preferences, dark preferences, Help, Cleanup, populated Recovery, Czech main/preferences, and 720/510/320-DIP widths. Images were inspected, including selected-note treatment, text wrapping, theme changes and panel labels. Every capture asserts that visible toolbar buttons remain within the toolbar and do not overlap. The updated layout test asserts inline Menu/Search/New centers and spacing, real search-to-list clearance and toolbar-to-editor adjacency. Native captures also verify sidebar controls remain inline and unclipped at every captured width.

The probe runs off-screen at (-12000, -12000), does not activate or appear in the taskbar, and bypasses saved window placement. It refuses empty note fixtures. All capture processes exited normally; no foreground automation was used. RenderTargetBitmap captures actual Avalonia controls, not a design mockup. This does not substitute for human testing of native window chrome or every pointer interaction.

The first fixture used notes assigned to another desktop and produced an empty main window; those captures were excluded and the probe now rejects that precondition. A synthetic fixture was then seeded explicitly. Source scripts are included; run from the repository root. `Capture.ps1` takes `-Executable` and a fresh `-Name`, creates a fresh disposable DB copy, captures, and exits. `Seed-Fixture.ps1` requires the existing local benchmark fixture as a schema source and refuses an existing destination directory.

## Checks and artifacts

### Native caption, rename and Find follow-up

`WindowChromeTheme` sets DWM caption/text colors from the existing `c-win-bg` and `c-text-primary` tokens and updates after `ActualThemeVariantChanged` and window open. Native window buttons, hit testing, system menu and resize behavior remain Windows-owned. Unknown theme resources use `DWMWA_COLOR_DEFAULT`; non-Windows handles are ignored and unsupported attribute HRESULTs leave native fallback behavior intact. The caption/text attributes are documented for Windows 11; older-Windows fallback was not exercised on this workstation. See [Microsoft's DWM attribute documentation](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute).

The previous client-only capture could not verify a native title bar. The opt-in visual probe now also uses `PrintWindow` on its own off-screen HWND. Full-window light → dark → light captures show the caption, title and native controls. The probe counts exact expected background and text colors within the nonclient caption region; all three transitions pass. Windows on this machine returns `E_INVALIDARG` when asked to *read* the caption/text attributes, which are documented as set attributes; `.attributes.txt` preserves that limitation and `.pixels.txt` records the actual rendered-color evidence. These are nonactivated-window captures, so native caption buttons have Windows' inactive appearance.

Rename overflow reproduced red: the default TextBox was 32 DIP tall inside a 22-DIP title row. `inlineRename` now sets a 22-DIP height, removes the default minimum size, and uses rounded theme-aware borders/background/text. The regression test checks long text after shrinking to a 320-DIP window and Escape cancellation. Existing save-on-Enter coverage remains. Light/dark and long-name narrow captures show the field staying inside the note row.

Find now places previous/next arrows left and Aa, underlined **ab** whole-word control, and match count right on one row directly beneath the query. Localized tooltip/accessibility names and checkbox semantics remain. The integration test checks geometric ordering, whole-word/case filtering, and English labels; language-switch coverage checks the Czech whole-word label. The gallery includes the normal light Find panel and dark checked options.

The final probe produces 21 images, including native frames, inline rename and checked Find options in addition to the original layout/panel matrix. This extends the original client-only evidence described above.

- Release build: zero warnings/errors (`final-build.log`).
- Full integration suite: **93 passed, zero failures/errors/skips** (`final-tests.xml`). The layout contract includes the restored inline sidebar controls.
- NativeAOT publish succeeded; dependency audit reported no vulnerable packages (`dependency-audit.json`). The first sandbox restore could not reach NuGet metadata; final unrestricted publish/audit completed. The two existing Avalonia MicroCom Boolean interop diagnostics remain unchanged.
- Final native capture exited 0 and preserves every original note ID/content; the temporary recovery note is removed.
- All **65** publish source inputs match the current runtime tree (`source.sha256`). Executable SHA-256: `2f056f1ea3827f94f636e744351facbbbde9270221e6b517228d65a73d75aeb7`.
- `git diff --check` passed. Preexisting `experiments/README.md`, `.serena/project.yml`, and `.claude/worktrees/` changes were preserved.

The launcher creates a fresh `chrome-preview/sessions/<guid>` copy of the synthetic fixture each time. It does not open the installed application's database. The preview is an experiment for review, not an installed release. No startup or scrolling speed claim is made for this visual change.
