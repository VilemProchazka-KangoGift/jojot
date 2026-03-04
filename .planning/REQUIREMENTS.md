# Requirements: JoJot

**Defined:** 2026-03-03
**Core Value:** Instant note capture tied to your virtual desktop context — switch desktops, switch notes, zero friction.

## v1.1 Requirements

Requirements for v1.1 Polish & Stability release. Each maps to roadmap phases.

### Bug Fixes

- [x] **BUG-01**: Pin/unpin no longer causes stack overflow exception
- [x] **BUG-02**: Delete no longer causes stack overflow exception
- [x] **BUG-03**: Renaming a tab no longer freezes the app

### Tab Panel UX

- [x] **TABUX-01**: Tab highlight uses background-color instead of left border
- [x] **TABUX-02**: Pin icon visible on pinned tabs
- [x] **TABUX-03**: Tab title shortens when delete icon is shown, spans full width otherwise
- [x] **TABUX-04**: Tab panel is user-resizable
- [x] **TABUX-05**: Drag-and-drop reorder works in the tab panel

### Theme & Display

- [ ] **THEME-01**: Dark mode tab names are legible (proper contrast)
- [ ] **THEME-02**: Text resize shows percentages instead of pt and affects tab labels too

### Window & Menu

- [ ] **WIN-01**: Virtual desktop name appears in window title
- [ ] **WIN-02**: Hamburger menu closes when user clicks anywhere outside of it

### Distribution

- [ ] **DIST-01**: Windows installer (MSI or MSIX) support

## v2 Requirements

None deferred at this time.

## Out of Scope

| Feature | Reason |
|---------|--------|
| Rich text / markdown rendering | Plain text only by design |
| Cloud sync | Local-first, no network dependency |
| Auto-update mechanism | Defer to future milestone |
| Portable (non-installer) distribution | Installer-only for v1.1 |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| BUG-01 | Phase 11 | Complete — human-verified 2026-03-03 |
| BUG-02 | Phase 11 | Complete — human-verified 2026-03-03 |
| BUG-03 | Phase 11 | Complete — human-verified 2026-03-03 |
| TABUX-01 | Phase 12 | Complete — verified 2026-03-04 |
| TABUX-02 | Phase 12 | Complete — verified 2026-03-04 |
| TABUX-03 | Phase 12 | Complete — verified 2026-03-04 |
| TABUX-04 | Phase 12 | Complete — verified 2026-03-04 |
| TABUX-05 | Phase 12 | Complete — verified 2026-03-04 |
| THEME-01 | Phase 13 | Pending |
| THEME-02 | Phase 13 | Pending |
| WIN-01 | Phase 13 | Pending |
| WIN-02 | Phase 13 | Pending |
| DIST-01 | Phase 14 | Pending |

**Coverage:**
- v1.1 requirements: 13 total
- Mapped to phases: 13
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-03*
*Last updated: 2026-03-04 — TABUX-01/02/03/04/05 complete and verified in Phase 12*
