---
phase: quick-5
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Views/MainWindow.xaml.cs
autonomous: true
requirements: [QUICK-5]

must_haves:
  truths:
    - "Tab title updates character-by-character as user types in a new note (no custom name set)"
    - "Tab title shows first ~30 chars of content when note has no custom name"
    - "Tab title shows 'New note' placeholder only when both name and content are empty"
    - "Autosave debounce behavior is unchanged (still 500ms, no double-saves)"
    - "Undo/redo and tab-switch continue working correctly"
  artifacts:
    - path: "JoJot/Views/MainWindow.xaml.cs"
      provides: "ContentEditor_TextChanged syncs content to NoteTab model"
  key_links:
    - from: "JoJot/Views/MainWindow.xaml.cs"
      to: "JoJot/Models/NoteTab.cs"
      via: "_activeTab.Content assignment triggers PropertyChanged for DisplayLabel"
      pattern: "_activeTab\\.Content\\s*="
---

<objective>
Fix tab title not updating live as the user types in a new note.

Purpose: When a note has no custom name, the tab title should show a preview of the content (first ~30 chars). Currently, the tab title only updates on tab switch or autosave flush because `ContentEditor_TextChanged` never syncs the editor text back to `NoteTab.Content`. The XAML binding `Text="{Binding DisplayLabel}"` depends on `NoteTab.Content` being current.

Output: One-line fix in `ContentEditor_TextChanged` that syncs editor text to the active tab's Content property on every keystroke.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Views/MainWindow.xaml.cs (lines 492-503 — ContentEditor_TextChanged handler)
@JoJot/Models/NoteTab.cs (Content setter with ContentDependents → DisplayLabel, IsPlaceholder)
@JoJot/Views/MainWindow.xaml (line 72 — Text="{Binding DisplayLabel}" in TabItemTemplate)

<interfaces>
From JoJot/Models/NoteTab.cs:
```csharp
// Content setter fires PropertyChanged for DisplayLabel and IsPlaceholder
// via ContentDependents = [nameof(DisplayLabel), nameof(IsPlaceholder)]
public string Content
{
    get => _content;
    set => SetProperty(ref _content, value, ContentDependents);
}

// Three-tier fallback: custom Name → first 30 chars of Content → "New note"
public string DisplayLabel { get; }
```

From JoJot/Views/MainWindow.xaml.cs:
```csharp
// Current handler — only notifies autosave, does NOT sync content to model
private void ContentEditor_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_suppressTextChanged || _activeTab is null) return;
    _autosaveService.NotifyTextChanged();

    // Start/reset checkpoint timer on user input
    _checkpointTimer.Stop();
    _checkpointTimer.Start();
}
```

From JoJot/Views/MainWindow.xaml (TabItemTemplate):
```xml
<TextBlock x:Name="TitleBlock" Text="{Binding DisplayLabel}" ... />
```
</interfaces>

**Root cause analysis:**
- `ContentEditor_TextChanged` fires on every keystroke but only calls `_autosaveService.NotifyTextChanged()`
- It never sets `_activeTab.Content = ContentEditor.Text`
- The autosave `contentProvider` lambda reads `ContentEditor.Text` but only passes it to `NoteStore.UpdateNoteContentAsync` — it does not write it back to `_activeTab.Content`
- The `onSaveCompleted` callback only updates `tab.UpdatedAt`, not `Content`
- Therefore `NoteTab.Content` (and thus `DisplayLabel`) stays stale until tab switch calls `SaveEditorStateToTab`

**Fix:** Add `_activeTab.Content = ContentEditor.Text;` in `ContentEditor_TextChanged`.

**Why this is safe:**
- `SetProperty` has an equality check — no-op if value unchanged
- `PropertyChanged` for `DisplayLabel` is lightweight (no DB, no layout, just binding update)
- Autosave remains debounced — this line does NOT trigger a save, it only syncs the in-memory model
- `_suppressTextChanged` guard already prevents this from firing during programmatic text assignment (undo/redo, tab restore)
</context>

<tasks>

<task type="auto">
  <name>Task 1: Sync editor text to NoteTab.Content on every keystroke</name>
  <files>JoJot/Views/MainWindow.xaml.cs</files>
  <action>
In `ContentEditor_TextChanged` (around line 495-503), add a single line after the guard clause to sync the editor content to the active tab model:

```csharp
private void ContentEditor_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_suppressTextChanged || _activeTab is null) return;

    // Sync editor text to model so DisplayLabel binding updates live
    _activeTab.Content = ContentEditor.Text;

    _autosaveService.NotifyTextChanged();

    // Start/reset checkpoint timer on user input
    _checkpointTimer.Stop();
    _checkpointTimer.Start();
}
```

Place the Content assignment BEFORE `NotifyTextChanged()` so the model is current when the autosave timer eventually fires. The `NoteTab.Content` setter's `SetProperty` will fire `PropertyChanged` for `DisplayLabel` and `IsPlaceholder`, causing the XAML `Text="{Binding DisplayLabel}"` binding to update the tab title immediately.

Do NOT modify any other files. The XAML binding and NoteTab property notification infrastructure already handle everything — this is purely a missing sync line.
  </action>
  <verify>
    <automated>dotnet build JoJot/JoJot.slnx && dotnet test JoJot.Tests/JoJot.Tests.csproj --no-build</automated>
  </verify>
  <done>
- Build succeeds with no warnings related to this change
- All 302 existing tests pass
- `ContentEditor_TextChanged` now sets `_activeTab.Content = ContentEditor.Text` before notifying autosave
- Tab title will update live as user types (verified by XAML binding chain: Content setter → PropertyChanged(DisplayLabel) → TextBlock update)
  </done>
</task>

</tasks>

<verification>
1. `dotnet build JoJot/JoJot.slnx` — clean build
2. `dotnet test JoJot.Tests/JoJot.Tests.csproj` — all 302 tests pass
3. Manual smoke test: create a new tab, type text, observe tab title updating character by character
</verification>

<success_criteria>
- Tab title updates live as user types in a note with no custom name
- Tab title shows first ~30 characters of content, truncated with ellipsis for longer text
- Tab title shows "New note" only when content is completely empty
- Existing autosave, undo/redo, and tab-switch behavior unchanged
- All tests pass
</success_criteria>

<output>
After completion, create `.planning/quick/5-fix-tab-title-not-updating-live-as-user-/5-SUMMARY.md`
</output>
