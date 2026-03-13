---
phase: quick-15
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Models/NoteTab.cs
  - JoJot/Views/MainWindow.TabDeletion.cs
  - JoJot/ViewModels/MainWindowViewModel.cs
  - JoJot.Tests/Models/NoteTabTests.cs
  - JoJot.Tests/ViewModels/EditorStateTests.cs
  - JoJot.Tests/ViewModels/ViewModelCoverage2Tests.cs
autonomous: true
requirements: [QUICK-15]

must_haves:
  truths:
    - "Tab titles in the sidebar display up to 45 characters before truncating (was 30)"
    - "Toast messages on tab deletion truncate at 45 characters (was 30)"
    - "Save-As default filename uses first 45 chars of content (was 30)"
    - "All truncation rules (newline collapsing, whitespace trimming, placeholder fallback) remain identical"
  artifacts:
    - path: "JoJot/Models/NoteTab.cs"
      provides: "DisplayLabelMaxLength constant = 45"
      contains: "DisplayLabelMaxLength = 45"
    - path: "JoJot/Views/MainWindow.TabDeletion.cs"
      provides: "Toast label truncation at 45"
      contains: "> 45"
    - path: "JoJot/ViewModels/MainWindowViewModel.cs"
      provides: "GetDefaultFilename preview truncation at 45"
      contains: "> 45"
  key_links:
    - from: "JoJot/Models/NoteTab.cs"
      to: "DisplayLabel property"
      via: "DisplayLabelMaxLength constant"
      pattern: "DisplayLabelMaxLength = 45"
---

<objective>
Increase tab title max display length by 50% from 30 to 45 characters. Update the single constant in NoteTab.cs plus two other hardcoded "30" truncation values in toast messages and filename generation. Update all affected tests to expect the new limit.

Purpose: When the tab panel is expanded wider, longer titles provide more context to the user.
Output: Updated constant, consistent 45-char truncation everywhere, all tests passing.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Models/NoteTab.cs
@JoJot/Views/MainWindow.TabDeletion.cs
@JoJot/ViewModels/MainWindowViewModel.cs
@JoJot.Tests/Models/NoteTabTests.cs
@JoJot.Tests/ViewModels/EditorStateTests.cs
@JoJot.Tests/ViewModels/ViewModelCoverage2Tests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Update truncation constant and all hardcoded 30-char limits to 45</name>
  <files>JoJot/Models/NoteTab.cs, JoJot/Views/MainWindow.TabDeletion.cs, JoJot/ViewModels/MainWindowViewModel.cs</files>
  <action>
In `JoJot/Models/NoteTab.cs`:
- Change `private const int DisplayLabelMaxLength = 30;` to `private const int DisplayLabelMaxLength = 45;`
- Update the XML doc on line 109 from "first ~30 chars" to "first ~45 chars"

In `JoJot/Views/MainWindow.TabDeletion.cs`:
- Line 166: Change `rawLabel.Length > 30 ? rawLabel[..30] : rawLabel` to `rawLabel.Length > 45 ? rawLabel[..45] : rawLabel`
- Update the XML doc on line 162 from "Truncates raw label to 30 chars" to "Truncates raw label to 45 chars"

In `JoJot/ViewModels/MainWindowViewModel.cs`:
- Line 477: Update comment from "first 30 chars" to "first 45 chars"
- Lines 487-488: Change `preview.Length > 30` to `preview.Length > 45` and `preview[..30]` to `preview[..45]`
- Line 635: Update comment from "first 30 chars" to "first 45 chars"
  </action>
  <verify>
    <automated>dotnet build JoJot/JoJot.slnx</automated>
  </verify>
  <done>All three files compile with the new 45-character limit. No hardcoded "30" remains in truncation logic.</done>
</task>

<task type="auto">
  <name>Task 2: Update all tests to expect 45-character truncation</name>
  <files>JoJot.Tests/Models/NoteTabTests.cs, JoJot.Tests/ViewModels/EditorStateTests.cs, JoJot.Tests/ViewModels/ViewModelCoverage2Tests.cs</files>
  <action>
In `JoJot.Tests/Models/NoteTabTests.cs`:
- `DisplayLabel_TruncatesAt30Chars_WhenContentIsLong` (line 25): Rename to `DisplayLabel_TruncatesAt45Chars_WhenContentIsLong`. Change `HaveLength(30)` to `HaveLength(45)` and `new string('A', 30)` to `new string('A', 45)`.
- `DisplayLabel_ReturnsFullContent_WhenContentIs30CharsOrLess` (line 34): Rename to `DisplayLabel_ReturnsFullContent_WhenContentIs45CharsOrLess`. Change `new string('B', 30)` to `new string('B', 45)`.
- `DisplayLabel_TruncatesAfterNewlineStripping` (line 84): Update the test content so that after newline-to-space conversion, the cleaned string is longer than 45 chars. Use content like `new string('A', 15) + "\n" + new string('B', 15) + "\n" + new string('C', 15) + "\n" + new string('D', 15)` which produces "AAAAAAAAAAAAAAA BBBBBBBBBBBBBBB CCCCCCCCCCCCCCC DDDDDDDDDDDDDDD" (63 chars). Assert `HaveLength(45)` and the expected 45-char prefix `"AAAAAAAAAAAAAAA BBBBBBBBBBBBBBB CCCCCCCCCCCCC"`. Update the comment accordingly.

In `JoJot.Tests/ViewModels/EditorStateTests.cs`:
- `GetDefaultFilename_TruncatesLongContent` (line 133): Change `new string('a', 30) + ".txt"` to `new string('a', 45) + ".txt"`.

In `JoJot.Tests/ViewModels/ViewModelCoverage2Tests.cs`:
- `GetDefaultFilename_LongContent_Truncates` (line 375): Change `new string('A', 30) + ".txt"` to `new string('A', 45) + ".txt"`.
  </action>
  <verify>
    <automated>dotnet test JoJot.Tests/JoJot.Tests.csproj --filter "DisplayLabel_Truncat|DisplayLabel_Returns|GetDefaultFilename" --no-restore</automated>
  </verify>
  <done>All truncation-related tests pass with the new 45-character limit. Test names reflect the updated limit.</done>
</task>

</tasks>

<verification>
- `dotnet build JoJot/JoJot.slnx` succeeds with no errors
- `dotnet test JoJot.Tests/JoJot.Tests.csproj` -- all tests pass (no regressions)
- Grep for hardcoded `> 30` or `[..30]` in truncation contexts returns zero matches
</verification>

<success_criteria>
- DisplayLabelMaxLength constant is 45
- Toast truncation uses 45
- GetDefaultFilename preview truncation uses 45
- All existing truncation tests updated and passing
- No other tests regressed
- All comments referencing "30 chars" updated to "45 chars"
</success_criteria>

<output>
After completion, create `.planning/quick/15-increase-the-max-length-of-the-tab-title/15-SUMMARY.md`
</output>
