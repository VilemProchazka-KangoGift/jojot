# Plan 04-01 Summary: NoteTab Model + DatabaseService CRUD

**Status:** Complete
**Duration:** ~3 min
**Commits:** 1

## What Was Built
- NoteTab model (JoJot/Models/NoteTab.cs) with all notes table columns mapped as properties
- DisplayLabel computed property with 3-tier fallback: custom name -> first 30 chars of content -> "New note"
- IsPlaceholder flag for muted/italic UI styling
- Relative date formatting: CreatedDisplay and UpdatedDisplay with all specified tiers
- 8 DatabaseService CRUD methods for notes: GetNotesForDesktop, InsertNote, UpdateNoteContent, UpdateNoteName, UpdateNotePinned, UpdateNoteSortOrders, DeleteNote, GetMaxSortOrder

## Key Decisions
- No INotifyPropertyChanged: project uses code-behind pattern, not MVVM binding
- All DB methods use parameterized queries (no string interpolation in SQL)
- UpdateNoteSortOrdersAsync operates under single lock acquisition for batch efficiency
- DateTime.Parse for SQLite datetime strings (converts UTC to local)

## Self-Check: PASSED
- [x] NoteTab.cs compiles with all properties matching notes table
- [x] DisplayLabel returns "New note" for empty content
- [x] All 8 DB methods compile with parameterized queries
- [x] Build succeeds with 0 errors, 0 warnings

## Key Files
- **Created:** JoJot/Models/NoteTab.cs
- **Modified:** JoJot/Services/DatabaseService.cs (added Notes CRUD section)
