---
status: diagnosed
trigger: "Tab hover glyphs and layout don't match design spec after gap closure fix (15-06)"
created: 2026-03-05T00:00:00Z
updated: 2026-03-05T12:00:00Z
---

## Current Focus

hypothesis: CONFIRMED - Five distinct issues remain in CreateTabListItem() after 15-06 partial fix
test: Line-by-line code review against design spec complete
expecting: n/a
next_action: Return diagnosis with specific line numbers and code diffs

## Symptoms

expected: |
  - Unpinned normal: title only
  - Unpinned hover: title | pin icon | delete icon (right-aligned, title shrinks, delete icon bigger, pin+delete change color on hover)
  - Pinned normal: pin icon | title
  - Pinned hover: pin icon | title | delete icon (pin icon changes to crossed-out pin glyph on hover)
actual: |
  - Close icon uses E711 ChromeClose at FontSize 10 (too small per user - wants "bigger")
  - Pinned tabs have NO close/delete button at all on hover (only pin-to-unpin swap exists)
  - Comment on line 329 still says "swap to X" (stale comment, but glyph is correct E77A)
errors: none (visual design mismatch)
reproduction: Hover over pinned and unpinned tabs, observe button layout and icon sizes
started: Current implementation after 15-06 fix

## Eliminated

- hypothesis: Pinned hover uses wrong glyph (multiplication sign instead of E77A unpin)
  evidence: 15-06 fix correctly changed this. Line 338 now shows "\uE77A" with Segoe Fluent Icons font. RESOLVED.
  timestamp: 2026-03-05

- hypothesis: Close icon uses multiplication sign character
  evidence: 15-06 fix correctly changed this. Line 427 now shows "\uE711" (ChromeClose) with Segoe Fluent Icons font. RESOLVED.
  timestamp: 2026-03-05

- hypothesis: Unpinned pin button has no hover color change
  evidence: 15-06 fix correctly added this. Lines 356-363 now have MouseEnter (accent) and MouseLeave (muted) handlers. RESOLVED.
  timestamp: 2026-03-05

## Evidence

- timestamp: 2026-03-05T12:00:00Z
  checked: Lines 407-450 — close button is ONLY created for unpinned tabs
  found: Line 409 has `if (!tab.Pinned)` guard. No close button is ever created for pinned tabs. The design spec says pinned hover should show "pin icon | title | delete icon" but there is NO delete icon for pinned tabs.
  implication: MAJOR GAP - Pinned tabs cannot be deleted via hover icon. The only way to delete a pinned tab is via context menu or middle-click.

- timestamp: 2026-03-05T12:00:00Z
  checked: Lines 476-514 — outer hover show/hide logic for pinned tabs
  found: The outerBorder.MouseEnter handler only shows closeBtn for unpinned tabs (line 490: `if (closeBtn != null)`). For pinned tabs, closeBtn is null, so nothing is shown in Col 2 on hover. There is no mechanism to show a delete button on pinned tab hover.
  implication: Confirms the missing pinned-tab delete button. The hover logic would need to be extended to show a close button for pinned tabs too.

- timestamp: 2026-03-05T12:00:00Z
  checked: Line 429 — close icon FontSize
  found: Close icon uses FontSize 10 for ChromeClose glyph. User says "delete icon should be bigger." The pin icon at line 321 uses FontSize 12. The ChromeClose glyph at 10pt is noticeably smaller than the pin glyph at 12pt.
  implication: User wants the delete icon to be visually larger. The 15-06 plan chose 10pt claiming it "matches pin icon visual weight" but user disagrees. Need to increase to 12pt or possibly 14pt.

- timestamp: 2026-03-05T12:00:00Z
  checked: Line 302 — Column 2 definition comment
  found: Comment says "Col 2: close X (unpinned only)" — this is accurate for current code but does NOT match design spec which wants close on pinned hover too.
  implication: The entire column 2 design assumed close was unpinned-only. Needs rethinking for pinned tabs.

- timestamp: 2026-03-05T12:00:00Z
  checked: Line 329 — stale comment
  found: Comment reads "Pinned tabs: show pin icon always, on hover swap to X (click to unpin)" but the actual code swaps to unpin glyph E77A, not X. Comment is misleading.
  implication: Minor — comment should say "swap to unpin glyph" not "swap to X"

## Resolution

root_cause: |
  The 15-06 fix resolved three of the original issues (glyph, font, unpinned hover color) but
  missed two remaining problems that still cause the implementation to not match the design spec:

  **Issue 1 (MAJOR): Pinned tabs have no delete button on hover (lines 407-450, 489-494)**
  The design spec says pinned hover should show "pin icon | title | delete icon" but the code
  only creates a close button for unpinned tabs (line 409: `if (!tab.Pinned)`). The outerBorder
  hover handler at line 490 checks `if (closeBtn != null)` which is always false for pinned tabs.
  A close button needs to be created for pinned tabs too and shown/hidden on hover.

  **Issue 2 (VISUAL): Close icon too small (line 429)**
  ChromeClose glyph E711 is at FontSize 10. User explicitly says "delete icon should be bigger."
  The pin icon uses FontSize 12. The close icon should be increased to at least 12 to match.

fix:
verification:
files_changed: []
