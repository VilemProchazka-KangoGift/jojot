# Phase 2: Create a Marketing README for GitHub Homepage - Context

**Gathered:** 2026-03-14
**Status:** Ready for planning

<domain>
## Phase Boundary

Create a marketing-quality README.md for the GitHub repository homepage. This is the first thing visitors see — it should sell JoJot as a productivity tool and drive downloads. No other documentation pages, wiki, or contributing guides are in scope.

</domain>

<decisions>
## Implementation Decisions

### Tone & positioning
- **Positioning:** Productivity app — targets productivity-minded users who want zero-friction capture and per-desktop organization
- **Tone:** Clean & confident — professional but not corporate, short sentences, bold claims, no fluff (think Linear/Raycast style)
- **Lead hook:** Per-desktop notes as the unique differentiator — "Your notes follow your virtual desktops"
- **No AI credit** — the README focuses purely on the product, how it was built is irrelevant to users

### Content & structure
- **Length:** One-pager — everything visible without much scrolling. Hero area, feature highlights, install, done
- **Installation:** Download link to GitHub Releases only — no build-from-source, no system requirements section
- **No keyboard shortcuts reference** — keep README marketing-focused
- **No comparison section** — let features speak, no mention of competitors or alternatives
- **License:** One-line MIT License mention in footer

### Visual elements
- **Hero area:** Screenshot placeholder — HTML comment or placeholder image tag where a screenshot will go later. Structure accounts for it now
- **No GitHub badges** — clean top area with just logo/name
- **Feature presentation:** Simple markdown bullet list — bold title + one-line description per feature. Clean, scannable
- **App icon:** Use `icon-jj-3.png` from resources/ in the README header

### Feature highlights (ordered)
1. **Virtual desktop notes** — each desktop gets its own set of notes (lead differentiator)
2. **Tabbed editing** — multiple notes per window, pin tabs, drag to reorder
3. **No save, no naming** — never asks you to save or name things, just type
4. **Auto-save / persistence** — everything persists automatically
5. **Super fast, no bloat** — lightweight, instant startup
6. **Easy cleanup** — clean up old notes easily
- **Keep it tight** — only these core features, no secondary feature mentions (hotkey, themes, find & replace, etc.)

### Claude's Discretion
- Exact wording of the tagline/hook
- Markdown structure details (heading levels, spacing)
- Screenshot placeholder format (HTML comment vs image placeholder)
- How to reference the icon (relative path, size)

</decisions>

<specifics>
## Specific Ideas

- Hook should center on "notes follow your virtual desktops" — this is the unique angle no other notepad offers
- Tone reference: Linear and Raycast READMEs — confident, clean, no unnecessary words
- User specifically called out: "never asks you to save or name things" as a key selling point
- "No bloat" and "super fast" are product values to convey, not just features

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `resources/icon-jj-3.png`: App icon to use in README header
- `resources/OVERVIEW.md`: Contains feature descriptions and constraints that can inform README copy
- `LICENSE`: MIT License — reference in footer

### Established Patterns
- No existing README — this is a fresh creation
- No docs/ directory or .github/ directory exists
- No screenshots or GIFs exist yet — placeholder needed

### Integration Points
- README.md goes in repo root (P:\projects\JoJot\README.md)
- Icon referenced from resources/ directory (relative path)
- Download link should point to GitHub Releases page

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 02-create-a-marketing-readme-for-github-homepage-for-the-app*
*Context gathered: 2026-03-14*
