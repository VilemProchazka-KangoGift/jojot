---
phase: quick-17
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Services/NoteStore.cs
  - JoJot.Tests/Services/NoteStoreTests.cs
autonomous: true
requirements: [QUICK-17]
must_haves:
  truths:
    - "Tab timestamps display correct relative time (e.g., 'Just now' after editing, not '1 hour ago')"
    - "Newly created tabs show 'Just now' in the updated-at display"
    - "Timestamps loaded from database match timestamps set in-memory by UI code"
  artifacts:
    - path: "JoJot/Services/NoteStore.cs"
      provides: "Note CRUD with local-time timestamps"
      contains: "Clock.Now"
    - path: "JoJot.Tests/Services/NoteStoreTests.cs"
      provides: "Tests verifying local-time timestamp storage"
      contains: "clock.Now"
  key_links:
    - from: "JoJot/Services/NoteStore.cs"
      to: "JoJot/Models/NoteTab.cs"
      via: "Both use local DateTime for CreatedAt/UpdatedAt"
      pattern: "Clock\\.Now"
---

<objective>
Fix timestamp mismatch causing tab updated-at to show incorrect relative time (e.g., "1 hour ago" immediately after editing).

Purpose: NoteStore writes timestamps via `DatabaseCore.Clock.UtcNow` (UTC), but NoteTab's display formatting compares against `SystemClock.Instance.Now` (local time), and all in-memory UI code sets `DateTime.Now` (local time). For users not in UTC, this creates a visible offset — a UTC+1 user sees updates as "1 hour ago" when they just happened.

Output: NoteStore uses `Clock.Now` (local time) consistently, matching both the UI code and the display formatting.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Services/NoteStore.cs
@JoJot/Services/IClock.cs
@JoJot/Services/DatabaseCore.cs
@JoJot/Models/NoteTab.cs
@JoJot.Tests/Services/NoteStoreTests.cs

<interfaces>
<!-- Key contracts the executor needs -->

From JoJot/Services/IClock.cs:
```csharp
public interface IClock
{
    DateTime Now { get; }      // Local time
    DateTime UtcNow { get; }   // UTC time
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
}
```

From JoJot/Services/DatabaseCore.cs:
```csharp
internal static IClock Clock => _clock;
```

From JoJot.Tests/Helpers/TestClock.cs:
```csharp
public sealed class TestClock : IClock
{
    public DateTime Now { get; set; } = new(2025, 6, 15, 10, 30, 0);
    public DateTime UtcNow { get; set; } = new(2025, 6, 15, 14, 30, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan duration) { Now += duration; UtcNow += duration; }
}
```
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Change NoteStore to use local time instead of UTC for timestamps</name>
  <files>JoJot/Services/NoteStore.cs, JoJot.Tests/Services/NoteStoreTests.cs</files>
  <behavior>
    - InsertNote_SetsTimestamps: CreatedAt and UpdatedAt should equal clock.Now (not clock.UtcNow)
    - UpdateNoteContent_UpdatesTimestamp: UpdatedAt after update should equal clock.Now (not clock.UtcNow)
  </behavior>
  <action>
    1. In `JoJot.Tests/Services/NoteStoreTests.cs`, update the `InsertNote_SetsTimestamps` test:
       - Change line `note.CreatedAt.Should().Be(clock.UtcNow);` to `note.CreatedAt.Should().Be(clock.Now);`
       - Change line `note.UpdatedAt.Should().Be(clock.UtcNow);` to `note.UpdatedAt.Should().Be(clock.Now);`

    2. Run tests — the two timestamp assertions should FAIL (RED) because NoteStore still uses `Clock.UtcNow`.

    3. In `JoJot/Services/NoteStore.cs`, change all 4 occurrences of `DatabaseCore.Clock.UtcNow` to `DatabaseCore.Clock.Now`:
       - Line 47 in InsertNoteAsync
       - Line 82 in UpdateNoteContentAsync
       - Line 109 in UpdateNoteNameAsync
       - Line 136 in UpdateNotePinnedAsync

    4. Run tests again — all should PASS (GREEN).

    This is safe because:
    - JoJot is a local desktop app with no multi-timezone concerns
    - All UI code already uses DateTime.Now for in-memory timestamps
    - NoteTab.FormatRelativeTime/FormatRelativeDate compare against SystemClock.Now (local time)
    - SQLite stores DateTime as text — the values are opaque to SQLite, only consumed by C# code
    - PendingMoveStore.cs also uses Clock.UtcNow but that's for a different purpose (move detection timestamps, not user-facing display) — leave it unchanged
  </action>
  <verify>
    <automated>dotnet test JoJot.Tests/JoJot.Tests.csproj --filter "FullyQualifiedName~NoteStoreTests" --no-build</automated>
  </verify>
  <done>
    - NoteStore uses Clock.Now (local time) for all 4 timestamp assignments
    - InsertNote_SetsTimestamps asserts against clock.Now
    - All NoteStore tests pass
    - Full test suite passes: `dotnet test JoJot.Tests/JoJot.Tests.csproj`
  </done>
</task>

</tasks>

<verification>
1. `dotnet build JoJot/JoJot.slnx` compiles without errors
2. `dotnet test JoJot.Tests/JoJot.Tests.csproj` — all tests pass
3. Grep confirms no remaining `Clock.UtcNow` in NoteStore.cs: `grep "Clock.UtcNow" JoJot/Services/NoteStore.cs` returns nothing
4. Grep confirms `Clock.Now` is used: `grep "Clock.Now" JoJot/Services/NoteStore.cs` returns 4 matches
</verification>

<success_criteria>
- NoteStore writes local-time timestamps to the database
- Timestamps are consistent between database writes (NoteStore) and in-memory updates (UI code)
- NoteTab display formatting shows correct relative times (e.g., "Just now" after editing, not "1 hour ago")
- All existing tests pass
</success_criteria>

<output>
After completion, create `.planning/quick/17-fix-timestamps-to-use-local-time-zone-in/17-SUMMARY.md`
</output>
