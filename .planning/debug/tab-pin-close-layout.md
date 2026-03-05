---
status: diagnosed
trigger: "Tab pin/close button layout doesn't match revised design - hover on pinned tab pin icon shows red X instead of crossed-out pin; delete icon too small; no hover color changes on pin/delete icons"
created: 2026-03-05T00:00:00Z
updated: 2026-03-05T00:00:00Z
---

## Current Focus

hypothesis: CONFIRMED - three distinct issues in CreateTabListItem()
test: Code review complete
expecting: n/a
next_action: Return diagnosis

## Symptoms

expected: Pinned hover shows crossed-out pin icon (not red X) on pin button; delete icon should be bigger; pin+delete icons should change color on hover
actual: Pinned tab hover shows red X on pin icon; delete icon is small; unpinned pin button has no hover color
errors: none (visual design mismatch)
reproduction: Hover over pinned tab, observe pin icon behavior
started: Current implementation

## Eliminated

## Evidence

- timestamp: 2026-03-05
  checked: Lines 326-348 in MainWindow.xaml.cs — pinned tab pin button hover behavior
  found: On MouseEnter, pin icon swaps to "\u00D7" (multiplication sign X) in Segoe UI at FontSize 14 with red foreground (#e74c3c). On MouseLeave, restores "\uE718" (pin icon) in Segoe Fluent Icons at FontSize 12.
  implication: This is the core bug — user wants "\uE77A" (Unpin glyph, the crossed-out pin) NOT an X character

- timestamp: 2026-03-05
  checked: Lines 2195-2205 — toolbar UpdateToolbarState()
  found: Toolbar already uses "\uE77A" for unpin icon (Segoe Fluent Icons). This is the reference glyph the user wants in the tab button too.
  implication: The correct glyph is known and used elsewhere; tab hover just uses the wrong one

- timestamp: 2026-03-05
  checked: Lines 415-421 — unpinned close button icon
  found: Close icon uses "\u00D7" (multiplication sign) at FontSize 14 in default font. The 22x22 Border is the hit target but the visual glyph is small.
  implication: User wants a bigger visual icon — either larger FontSize, a different glyph (e.g., Segoe Fluent Icons \uE711 which is a proper X), or both

- timestamp: 2026-03-05
  checked: Lines 350-355 — unpinned tab pin button
  found: No hover color change on pin button for unpinned tabs. Pin icon stays c-text-muted. Only the close button has hover color change (red on enter, muted on leave).
  implication: Both pin and close icons need hover color effects

- timestamp: 2026-03-05
  checked: Lines 424-428 — unpinned close button hover
  found: Close button has red hover (#e74c3c on enter, c-text-muted on leave). This is the only button with a hover color.
  implication: Pin button (unpinned) and pin button (pinned, after fixing to unpin glyph) both need hover color changes too

## Resolution

root_cause: Three issues in CreateTabListItem(): (1) pinned tab pin hover swaps to X character instead of unpin glyph \uE77A, (2) close icon too small, (3) unpinned pin button lacks hover color change
fix:
verification:
files_changed: []
