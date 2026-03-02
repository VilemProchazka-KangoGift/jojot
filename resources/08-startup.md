# JoJot — Startup Sequence

---

## Technology notes (AOT)

Publishing with `PublishAot=true` in .NET 10 compiles to a self-contained native binary — no JIT warm-up, significantly faster cold starts.

**AOT trade-offs:**
- No runtime reflection by default — avoid libraries that rely on it. Use `Microsoft.Data.Sqlite` with NativeAOT trimming annotations.
- Binary size ~15–30 MB self-contained (acceptable for desktop).
- WPF + AOT support matured in .NET 9/10 — test XAML-heavy paths early.

**Startup target: < 200 ms** to first interactive window.

---

## Startup sequence (happy path)

```
1. Executable launched (native AOT binary, no JIT)
2. Acquire named mutex — if already held, send IPC to existing process and exit
3. Check pending_moves table — if a row exists, restore window to origin desktop and delete row
4. Open / create SQLite DB (AppData\Local\JoJot\jojot.db)
   — if DB is new:    create schema now (first launch only, fast)
   — if DB exists:    skip migrations until after window is shown
5. Attempt three-tier session match for this desktop GUID (see 02-virtual-desktops.md)
6. Load matched tabs ordered by pinned DESC, sort_order ASC
   — or create one empty tab if no session found
7. Restore window geometry for this desktop
8. Apply saved theme
9. Focus last active tab and restore cursor position + scroll offset
10. Show window  ◄── target: < 200 ms total
11. [Background] Run any pending DB migrations if schema version mismatch
```

---

## Key constraints

- **Migrations never run on the cold-start path.** Window shows first, migrations run in a background thread after.
- **First launch only:** create schema synchronously before window opens (fast, one-time).
- **Migration failure:** log error, continue — do not block or crash.
- **pending_moves on startup:** any row here means a previous crash mid-drag — restore window to `origin_desktop_guid` and delete the row.
