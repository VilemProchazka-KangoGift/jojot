---
phase: quick-16
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Models/NoteTab.cs
  - JoJot.Tests/Models/NoteTabTests.cs
  - JoJot.Tests/Models/NoteTabBoundaryTests.cs
autonomous: true
requirements: [QUICK-16]
must_haves:
  truths:
    - "When tab content exceeds 45 chars and no custom name is set, the tab title ends with '...'"
    - "When tab content is 45 chars or fewer, no ellipsis is appended"
    - "When a custom Name is set, it displays as-is without ellipsis (even if long)"
    - "Placeholder 'New note' has no ellipsis"
  artifacts:
    - path: "JoJot/Models/NoteTab.cs"
      provides: "DisplayLabel with ellipsis on truncation"
      contains: "..."
    - path: "JoJot.Tests/Models/NoteTabTests.cs"
      provides: "Updated tests for ellipsis behavior"
    - path: "JoJot.Tests/Models/NoteTabBoundaryTests.cs"
      provides: "Updated boundary tests for ellipsis behavior"
  key_links:
    - from: "JoJot/Models/NoteTab.cs"
      to: "JoJot/Views/MainWindow.xaml"
      via: "DisplayLabel binding"
      pattern: "Text=.*DisplayLabel"
---

<objective>
Add ellipsis ("...") to auto-generated tab titles when the editor content is truncated.

Purpose: Give users a visual cue that the tab title is showing a truncated preview of the note content, not the full text.
Output: Updated DisplayLabel property and passing tests.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Models/NoteTab.cs
@JoJot.Tests/Models/NoteTabTests.cs
@JoJot.Tests/Models/NoteTabBoundaryTests.cs
</context>

<interfaces>
<!-- Key code from NoteTab.cs DisplayLabel property (lines 111-139) -->

```csharp
public string DisplayLabel
{
    get
    {
        if (!string.IsNullOrWhiteSpace(Name))
        {
            return Name;
        }

        if (!string.IsNullOrWhiteSpace(Content))
        {
            var cleaned = Content.Trim()
                .Replace("\r\n", " ")
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            while (cleaned.Contains("  "))
                cleaned = cleaned.Replace("  ", " ");

            if (cleaned.Length <= DisplayLabelMaxLength)
            {
                return cleaned;
            }

            return cleaned[..DisplayLabelMaxLength];
        }

        return PlaceholderLabel;
    }
}
```
</interfaces>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Update tests to expect ellipsis on truncated DisplayLabel</name>
  <files>JoJot.Tests/Models/NoteTabTests.cs, JoJot.Tests/Models/NoteTabBoundaryTests.cs</files>
  <behavior>
    - DisplayLabel_TruncatesAt45Chars_WhenContentIsLong: result should end with "..." (total length = 45 + 3 = 48, or the truncated content is shortened to 45 chars and then "..." appended)
    - DisplayLabel_Content46Chars_Truncated: result should be first 45 chars + "..."
    - DisplayLabel_Content45Chars_NoTruncation: no ellipsis (content fits exactly)
    - DisplayLabel_Content44Chars_NoTruncation: no ellipsis (content fits with room)
    - DisplayLabel_ReturnsFullContent_WhenContentIs45CharsOrLess: no ellipsis
    - DisplayLabel_TruncatesAfterNewlineStripping: result should end with "..."
    - DisplayLabel_ReturnsName_WhenNameIsSet: custom name has NO ellipsis regardless of length
    - DisplayLabel_ReturnsPlaceholder: "New note" has NO ellipsis
  </behavior>
  <action>
Update the existing test assertions in NoteTabTests.cs and NoteTabBoundaryTests.cs:

In NoteTabTests.cs:
1. `DisplayLabel_TruncatesAt45Chars_WhenContentIsLong` (line 25): Change expected length to 48 (45 chars + "..."), and assert `.Should().EndWith("...")`.
2. `DisplayLabel_ReturnsFullContent_WhenContentIs45CharsOrLess` (line 34): Keep as-is, content fits so no ellipsis.
3. `DisplayLabel_TruncatesAfterNewlineStripping` (line 84): Update expected length to 48 and assert the result ends with "...". Update the `.Should().Be(...)` assertion to include "..." at end. The first 45 chars of "AAAAAAAAAAAAAAA BBBBBBBBBBBBBBB CCCCCCCCCCCCC" plus "...".

In NoteTabBoundaryTests.cs:
1. `DisplayLabel_Content45Chars_NoTruncation` (line 15): Keep as-is, 45 chars exactly = no truncation = no ellipsis.
2. `DisplayLabel_Content46Chars_Truncated` (line 24): Update expected length to 48 (45 + "..."), assert ends with "...".
3. `DisplayLabel_Content44Chars_NoTruncation` (line 33): Keep as-is, 44 chars = no truncation.

Run tests to confirm they FAIL (RED phase).
  </action>
  <verify>
    <automated>dotnet test JoJot.Tests/JoJot.Tests.csproj --filter "DisplayLabel" --no-build 2>&1 | tail -5</automated>
  </verify>
  <done>Tests updated to expect "..." suffix on truncated content. Tests fail because DisplayLabel does not yet append ellipsis.</done>
</task>

<task type="auto">
  <name>Task 2: Add ellipsis to DisplayLabel when content is truncated</name>
  <files>JoJot/Models/NoteTab.cs</files>
  <action>
In `NoteTab.cs`, modify the `DisplayLabel` getter. In the content fallback branch, when `cleaned.Length > DisplayLabelMaxLength`, change the return from:

```csharp
return cleaned[..DisplayLabelMaxLength];
```

to:

```csharp
return cleaned[..DisplayLabelMaxLength] + "...";
```

This only affects the auto-generated label from content. Custom names (Name property) and the placeholder "New note" are unaffected.

Do NOT change DisplayLabelMaxLength constant -- the truncation point stays at 45 characters of actual content, with "..." appended after.
  </action>
  <verify>
    <automated>dotnet test JoJot.Tests/JoJot.Tests.csproj --filter "DisplayLabel"</automated>
  </verify>
  <done>All DisplayLabel tests pass. Truncated content-based tab titles end with "...". Non-truncated content, custom names, and placeholder show no ellipsis.</done>
</task>

</tasks>

<verification>
1. `dotnet test JoJot.Tests/JoJot.Tests.csproj` -- all tests pass (no regressions)
2. `dotnet build JoJot/JoJot.slnx` -- builds without warnings
</verification>

<success_criteria>
- Tab titles auto-generated from content show "..." when content exceeds 45 characters
- Tab titles that fit within 45 characters show NO ellipsis
- Custom-named tabs show NO ellipsis regardless of name length
- Empty tabs show "New note" with NO ellipsis
- All existing tests pass (updated to match new behavior)
</success_criteria>

<output>
After completion, create `.planning/quick/16-when-auto-naming-the-tabs-from-the-edito/16-SUMMARY.md`
</output>
