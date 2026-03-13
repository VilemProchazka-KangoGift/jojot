---
phase: quick-11
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Views/MainWindow.xaml.cs
  - JoJot.Tests/Services/UndoStackTests.cs
autonomous: true
requirements: [QUICK-11]

must_haves:
  truths:
    - "User types text, presses Ctrl+Z, then Ctrl+Y and gets back to the exact text they had before undo"
    - "Undo still works normally when editor content matches the last snapshot (no duplicate entries)"
    - "Redo after undo restores unsaved typing that occurred between the last autosave snapshot and the undo"
  artifacts:
    - path: "JoJot/Views/MainWindow.xaml.cs"
      provides: "PerformUndo captures current editor content before undo"
      contains: "PushSnapshot"
    - path: "JoJot.Tests/Services/UndoStackTests.cs"
      provides: "Test proving redo restores content pushed before undo"
  key_links:
    - from: "MainWindow.PerformUndo"
      to: "UndoManager.PushSnapshot"
      via: "push current content before calling Undo"
      pattern: "PushSnapshot.*Undo"
---

<objective>
Fix the bug where Ctrl+Z then Ctrl+Y does not restore the user's original text.

**Root cause:** When the user types text and presses Ctrl+Z before the 500ms autosave debounce fires, the current editor content was never pushed to the undo stack. `PerformUndo()` calls `UndoManager.Undo()` which moves the pointer back, but there is no "forward" entry for redo to return to because the current text was never captured.

**Fix:** In `PerformUndo()`, push the current editor content as a snapshot BEFORE calling `Undo()`. The existing `PushSnapshot` method handles deduplication (skips if content equals current index), so this is safe to call unconditionally. When content differs from the last snapshot, it gets pushed and becomes the redo target.

Purpose: Ensure redo always restores the state the user had before they pressed undo.
Output: Modified PerformUndo + tests proving the fix.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Views/MainWindow.xaml.cs (lines 545-578 — PerformUndo and PerformRedo)
@JoJot/Services/UndoStack.cs (PushSnapshot, Undo, Redo methods)
@JoJot/Services/UndoManager.cs (PushSnapshot, Undo, Redo delegation)
@JoJot.Tests/Services/UndoStackTests.cs (test patterns)
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Add test and fix PerformUndo to capture current content before undo</name>
  <files>JoJot.Tests/Services/UndoStackTests.cs, JoJot/Views/MainWindow.xaml.cs</files>
  <behavior>
    - Test: PushSnapshot("a"), PushSnapshot("b"), then simulate unsaved typing by NOT pushing "c". Call PushSnapshot("c") then Undo(). Redo() should return "c". This proves that pushing current content before undo makes it available via redo.
    - Test: PushSnapshot("a"), PushSnapshot("b") (content matches current index). Calling PushSnapshot("b") again is a no-op (dedup). Undo returns "a", Redo returns "b". No duplicate entries created.
    - Test: Full cycle — PushInitialContent("a"), PushSnapshot("b"), PushSnapshot("c_unsaved") then Undo then Undo returns "b" then "a", then Redo returns "b" then "c_unsaved". Complete round-trip.
  </behavior>
  <action>
1. In `JoJot.Tests/Services/UndoStackTests.cs`, add a new test section `// --- Undo captures current content for redo ---` with the three tests described in `<behavior>`. These tests validate the pattern at the UndoStack level (PushSnapshot before Undo ensures redo works).

2. Run the tests to confirm they pass (they should, since PushSnapshot + Undo + Redo already works correctly at the UndoStack level -- the bug is that PerformUndo never calls PushSnapshot first).

3. In `JoJot/Views/MainWindow.xaml.cs`, modify `PerformUndo()` (around line 545):
   - BEFORE the `UndoManager.Instance.Undo(...)` call, add:
     ```csharp
     // Capture current editor content so redo can restore it.
     // PushSnapshot deduplicates if content matches the current index.
     UndoManager.Instance.PushSnapshot(_activeTab.Id, ContentEditor.Text);
     ```
   - This single line is the entire fix. The existing PushSnapshot handles:
     - Dedup: if editor text equals the last snapshot, it's a no-op
     - New content: pushed to stack, becomes the redo target after Undo moves the pointer back

4. No changes needed to `PerformRedo()`, `UndoStack`, or `UndoManager` -- the existing APIs already support this pattern correctly.
  </action>
  <verify>
    <automated>dotnet test JoJot.Tests/JoJot.Tests.csproj --filter "FullyQualifiedName~UndoStackTests" --no-restore</automated>
  </verify>
  <done>
    - Three new tests pass proving PushSnapshot-before-Undo enables correct redo
    - PerformUndo() pushes current editor content before calling Undo
    - Existing undo/redo tests still pass (no regression)
    - Full test suite passes: `dotnet test JoJot.Tests/JoJot.Tests.csproj`
  </done>
</task>

</tasks>

<verification>
1. `dotnet build JoJot/JoJot.slnx` compiles without errors or warnings
2. `dotnet test JoJot.Tests/JoJot.Tests.csproj` — all tests pass including new ones
3. Manual verification scenario:
   - Open JoJot, type "Hello World" in a note
   - Wait for autosave (500ms)
   - Type " extra text" (so note reads "Hello World extra text")
   - Press Ctrl+Z immediately (before autosave fires)
   - Note should show "Hello World"
   - Press Ctrl+Y
   - Note should show "Hello World extra text" (restored)
</verification>

<success_criteria>
- Ctrl+Z followed by Ctrl+Y restores the exact text the user had before pressing undo
- No duplicate undo entries when editor content matches the last snapshot
- All existing tests pass without modification
</success_criteria>

<output>
After completion, create `.planning/quick/11-autosave-editor-state-before-undo-so-red/11-SUMMARY.md`
</output>
