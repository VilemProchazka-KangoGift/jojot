---
status: diagnosed
trigger: "Tab hover icons change vertical tab size"
created: 2026-03-05T00:00:00Z
updated: 2026-03-05T00:00:00Z
---

## Current Focus

hypothesis: CONFIRMED - see Root Cause below
test: n/a
expecting: n/a
next_action: Apply fix (code change not yet made per instructions)

## Symptoms

expected: Hovering over an unpinned tab should reveal pin and close icons without changing the tab's vertical size.
actual: When hovering over an unpinned tab, the tab grows vertically as the pin and close icon containers (22px tall) are taller than the title text (~17-18px).
errors: none (cosmetic)
reproduction: Hover over any unpinned tab -- observe the tab height increase.
started: After plan 15-08 redesigned tab hover layout with 22x22 hit-target Borders.

## Eliminated

(none -- root cause identified on first hypothesis)

## Evidence

- timestamp: 2026-03-05
  checked: CreateTabListItem() in MainWindow.xaml.cs lines 303-567
  found: |
    Pin button (Border): Width=22, Height=22 (line 331)
    Close button (Border): Width=22, Height=22 (line 436)
    Title label (TextBlock): FontSize=13, no explicit Height (line 403-404)
    Row 0 grid: RowDefinition Height=Auto (line 316) -- sizes to tallest child
  implication: Row 0 height is driven by the tallest visible child in that row.

- timestamp: 2026-03-05
  checked: Default state of pin and close buttons for unpinned tabs
  found: |
    Both pinBtn and closeBtn start with:
      Opacity = 0
      Visibility = Visibility.Collapsed  (lines 338-339, 443-444)
    On hover (lines 510-518):
      pinBtn.Visibility = Visibility.Visible  (then animate opacity 0->1)
      closeBtn.Visibility = Visibility.Visible (then animate opacity 0->1)
  implication: |
    When Collapsed, the 22px buttons don't participate in layout.
    Row 0 height = title TextBlock natural height (~17-18px at FontSize 13).
    When Visible, the 22px buttons DO participate in layout.
    Row 0 height = max(~17-18px title, 22px button) = 22px.
    This 4-5px jump is the vertical growth the user sees.

- timestamp: 2026-03-05
  checked: Pinned tab behavior for comparison
  found: |
    For pinned tabs, pinBtn starts Visibility.Visible, Opacity=1 (lines 356-357).
    The 22px pin button is ALWAYS in layout, so the row is always 22px.
    The close button appearing on hover doesn't change height (already 22px from pin).
  implication: Pinned tabs do NOT exhibit this bug because the pin button is always visible and already forces the row to 22px.

- timestamp: 2026-03-05
  checked: ListBoxItem template in MainWindow.xaml lines 17-30
  found: |
    Template is bare ContentPresenter with no height constraints.
    No MinHeight set on ListBoxItem or any container.
  implication: Nothing prevents the tab from shrinking/growing as content changes.

## Resolution

root_cause: |
  The pin and close icon containers are 22x22px Borders, but for unpinned tabs they
  start as Visibility.Collapsed. The title TextBlock at FontSize 13 renders at ~17-18px
  natural height. Row 0 of the tab's inner Grid uses Height=Auto, so it sizes to its
  tallest visible child.

  - NOT hovered: tallest child = title TextBlock (~17-18px) -> row height ~17-18px
  - Hovered: tallest child = 22px icon Border -> row height 22px

  This 4-5px jump causes the visible "tab grows on hover" effect.

  Pinned tabs are unaffected because their pin button is always visible at 22px,
  keeping the row height constant regardless of hover state.

fix_direction: |
  Two possible approaches (either would work):

  **Option A (recommended): Set MinHeight on row0 Grid**
  Set row0.MinHeight = 22 so the row is always 22px tall, even when the icon
  Borders are Collapsed. This is the simplest change (one line) and keeps the
  icon Borders at their current 22px hit-target size.

  Location: after line 323 (var row0 = new Grid();), add:
    row0.MinHeight = 22;

  **Option B: Use Opacity-only toggling instead of Visibility**
  Keep buttons Visible but at Opacity=0 (and IsHitTestVisible=false) in the
  default state, so they always participate in layout. This is a larger change
  and risks accidental clicks on invisible buttons if IsHitTestVisible isn't
  managed perfectly.

  Option A is strongly preferred for simplicity and minimal risk.

verification: not yet applied
files_changed: []
