---
phase: quick-6
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Models/NoteTab.cs
  - JoJot.Tests/Models/NoteTabTests.cs
autonomous: true
requirements: [QUICK-6]
must_haves:
  truths:
    - "Tab titles derived from content never contain newline characters"
    - "Tab titles derived from content collapse multiple whitespace into a single space"
    - "Custom names (Name property) are returned as-is since the user explicitly chose them"
  artifacts:
    - path: "JoJot/Models/NoteTab.cs"
      provides: "DisplayLabel with newline stripping"
      contains: "Replace"
    - path: "JoJot.Tests/Models/NoteTabTests.cs"
      provides: "Tests covering newline and carriage return in content-derived labels"
  key_links:
    - from: "JoJot/Models/NoteTab.cs"
      to: "DisplayLabel property"
      via: "Content fallback path strips \\r and \\n"
      pattern: "Replace.*\\\\n|Replace.*\\\\r"
---

<objective>
Strip newlines from auto-generated tab titles in NoteTab.DisplayLabel.

Purpose: When a tab has no custom name, DisplayLabel falls back to the first 30 characters of Content. If the content starts with lines like "Hello\nWorld", the tab title renders with a literal line break, causing layout issues in the tab bar. Newlines (and carriage returns) must be replaced with spaces, and consecutive whitespace collapsed.

Output: Patched NoteTab.DisplayLabel property and new tests confirming newline stripping.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Models/NoteTab.cs
@JoJot.Tests/Models/NoteTabTests.cs
</context>

<interfaces>
<!-- Key types and contracts the executor needs. -->

From JoJot/Models/NoteTab.cs:
```csharp
// DisplayLabel property (lines 111-133) — the content-fallback path:
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
            var trimmed = Content.Trim();
            if (trimmed.Length <= DisplayLabelMaxLength)
            {
                return trimmed;
            }

            return trimmed[..DisplayLabelMaxLength];
        }

        return PlaceholderLabel;
    }
}
```

From JoJot/ViewModels/MainWindowViewModel.cs (line 538) — pattern already used for cleanup excerpts:
```csharp
string content = tab.Content.Trim().Replace('\n', ' ').Replace('\r', ' ');
```
</interfaces>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Strip newlines from content-derived DisplayLabel</name>
  <files>JoJot/Models/NoteTab.cs, JoJot.Tests/Models/NoteTabTests.cs</files>
  <behavior>
    - Content "Hello\nWorld" with no Name -> DisplayLabel is "Hello World" (newline replaced with space)
    - Content "Line1\r\nLine2\r\nLine3" with no Name -> DisplayLabel is "Line1 Line2 Line3" (CRLF replaced)
    - Content "Hello\n\n\nWorld" with no Name -> DisplayLabel collapses to "Hello World" (multiple newlines become single space)
    - Content with leading newline "\nHello" with no Name -> DisplayLabel is "Hello" (trimmed first, then stripped)
    - Long content with newlines: first 30 chars of the cleaned result, not of raw content
    - Name "Custom\nName" -> DisplayLabel is "Custom\nName" (custom names returned as-is; user chose them)
    - Existing tests still pass (short content, long content, placeholder, whitespace-only)
  </behavior>
  <action>
In JoJot.Tests/Models/NoteTabTests.cs, add test methods in the DisplayLabel section:

1. `DisplayLabel_ReplacesNewlines_InContentFallback` — content "Hello\nWorld", Name null -> "Hello World"
2. `DisplayLabel_ReplacesCRLF_InContentFallback` — content "Line1\r\nLine2" -> "Line1 Line2"
3. `DisplayLabel_CollapsesMultipleNewlines` — content "A\n\n\nB" -> "A B" (no runs of multiple spaces)
4. `DisplayLabel_TruncatesAfterNewlineStripping` — content with newlines where the cleaned text exceeds 30 chars -> truncated to 30 chars of the cleaned text
5. `DisplayLabel_PreservesNewlinesInCustomName` — Name = "Has\nNewline", Content = "whatever" -> "Has\nNewline" (name returned as-is)

Run tests: they should FAIL (RED).

Then modify NoteTab.DisplayLabel content-fallback path. After `Content.Trim()`, replace `\r\n`, `\r`, and `\n` with a single space, then collapse consecutive spaces using a Regex or sequential Replace. Apply the same DisplayLabelMaxLength truncation to the cleaned string.

Specifically, change the content-fallback branch to:
```csharp
if (!string.IsNullOrWhiteSpace(Content))
{
    var cleaned = Content.Trim()
        .Replace("\r\n", " ")
        .Replace('\r', ' ')
        .Replace('\n', ' ');
    // Collapse runs of multiple spaces into one
    while (cleaned.Contains("  "))
        cleaned = cleaned.Replace("  ", " ");

    if (cleaned.Length <= DisplayLabelMaxLength)
    {
        return cleaned;
    }

    return cleaned[..DisplayLabelMaxLength];
}
```

Do NOT use Regex — keep it simple with string.Replace, consistent with the GetCleanupExcerpt pattern already in the codebase (MainWindowViewModel line 538). The while loop for collapsing spaces is fine for a max-30-char string.

Do NOT modify the Name branch — custom names are user-chosen and returned as-is.

Run tests again: they should PASS (GREEN).
  </action>
  <verify>
    <automated>dotnet test JoJot.Tests/JoJot.Tests.csproj --filter "FullyQualifiedName~NoteTabTests" --no-restore</automated>
  </verify>
  <done>
    - All existing NoteTabTests pass unchanged
    - New tests confirm newlines (\n, \r\n, \r) in content are replaced with spaces in DisplayLabel
    - Multiple consecutive newlines/spaces are collapsed to a single space
    - Custom Name property is unaffected (returned as-is)
    - DisplayLabel truncation at 30 chars applies to the cleaned text
  </done>
</task>

</tasks>

<verification>
- `dotnet test JoJot.Tests/JoJot.Tests.csproj` — all 302+ tests pass
- `dotnet build JoJot/JoJot.slnx` — clean build with no warnings
</verification>

<success_criteria>
- Tab titles derived from note content never contain \n, \r\n, or \r characters
- Consecutive whitespace in content-derived titles is collapsed to a single space
- All tests pass including new newline-stripping tests
</success_criteria>

<output>
After completion, create `.planning/quick/6-strip-newlines-from-auto-generated-tab-t/6-SUMMARY.md`
</output>
