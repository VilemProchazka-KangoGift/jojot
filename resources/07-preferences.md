# JoJot — Preferences

---

## Preferences dialog

Opened via `[≡] → Preferences…`. All changes apply live — no restart required, no confirm needed.

---

### Theme

Three-way toggle: `Light | System | Dark`

- **System** follows Windows app mode setting, re-evaluates on `SystemEvents.UserPreferenceChanged`
- Changes apply instantly via WPF `ResourceDictionary` swap
- Stored in `preferences` table, key `theme`, values: `light` / `dark` / `system`

---

### Editor font size

Control layout: `[ − ]  13  [ + ]`  with a `Reset` link

| Property | Value |
|---|---|
| Default | 13 pt |
| Range | 8 pt – 32 pt |
| Step | 1 pt per click |
| Button size | 26 × 26 px, border `1px c-border`, radius `2px` |
| Display | 28px min-width, monospace, centre-aligned |
| Persistence | `preferences` table, key `font_size` |

- Change applies **live** to the editor — no confirmation
- Also adjustable via `Ctrl+=` / `Ctrl+-` / `Ctrl+0` from the main window (see `06-keyboard-shortcuts.md`)
- Reset link returns to 13 pt

---

### Autosave debounce interval

Numeric input, milliseconds. Default `500`. Range `200–2000`.

Stored in `preferences` table, key `autosave_debounce_ms`.

---

### Global hotkey

Key combination picker. Default `Win+Shift+N`.

Registered via `RegisterHotKey` Win32 API. The hotkey applies to the current desktop's window; minimises if already focused.

Stored in `preferences` table, key `global_hotkey`.
