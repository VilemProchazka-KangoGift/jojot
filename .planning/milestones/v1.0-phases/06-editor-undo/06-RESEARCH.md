# Phase 6: Editor & Undo - Research

**Researched:** 2026-03-03
**Domain:** WPF TextBox autosave, custom undo/redo stack, clipboard and file export
**Confidence:** HIGH

## Summary

Phase 6 adds autosave with debounced persistence, a custom two-tier undo/redo system, enhanced clipboard behavior, and Save As TXT export to the existing plain-text editor. The TextBox is already configured (Consolas 13pt, word-wrap, IsUndoEnabled=False from Phase 4). The key technical challenges are: (1) reset-on-keystroke debounce using DispatcherTimer, (2) per-tab UndoStack with seamless two-tier traversal, (3) intercepting Ctrl+Z/Y before WPF's native handling via PreviewKeyDown, and (4) synchronous flush on app close.

**Primary recommendation:** Use DispatcherTimer for debounce (resets on each keystroke), a simple List<string>-based UndoStack class with pointer navigation, and WPF's built-in Clipboard and SaveFileDialog APIs. No external libraries needed — everything is available in the .NET/WPF framework.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **Autosave feel:** Fully invisible — no save indicator, no status bar, no dot in title bar
- **Save failures:** Silent retry on next debounce cycle; error logged only
- **Debounce:** Reset-on-keystroke: timer resets each keystroke, save fires 500ms after LAST keystroke
- **App close:** Block until save completes (synchronous flush), no optimistic fire-and-forget
- **Undo granularity:** Autosave-aligned — each Ctrl+Z jumps to previous autosave snapshot (~500ms typing gaps)
- **No finer undo:** No word-boundary or character-level undo — snapshots are the granularity unit
- **Seamless tiers:** No visual indication when crossing from tier-1 (50 snapshots) to tier-2 (20 checkpoints)
- **Linear stack:** Typing after undo destroys the redo future (standard behavior)
- **Initial content:** Database load counts as first undo snapshot — Ctrl+Z can always restore to loaded state
- **Ctrl+C no selection:** Copies entire note silently — no visual feedback
- **Save As dialog:** Remembers last used directory in-memory per session; resets on app launch
- **Default filename:** "JoJot note YYYY-MM-DD.txt" when tab has no name and no content
- **No feedback after save:** OS dialog closing is sufficient confirmation
- **Collapse silent:** No notification, no per-tab indicator for memory collapse
- **50MB budget:** Hardcoded constant, not configurable
- **Active tab never collapsed:** Regardless of size
- **Undo survives soft-delete:** If tab restored within 4 seconds, full undo history intact

### Claude's Discretion
- Exact UndoStack class design and data structures
- Tier-2 checkpoint timing implementation
- Memory estimation approach for the 50MB budget
- How to intercept Ctrl+Z/Y before WPF's native handling
- Scroll offset save/restore implementation details

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| EDIT-01 | Plain-text editor with monospace font, word-wrap, no horizontal scrollbar | Already implemented in Phase 4 (Consolas 13pt, TextWrapping=Wrap, IsUndoEnabled=False) |
| EDIT-02 | Autosave with configurable debounce (default 500ms) to SQLite; updated_at set on every write | DispatcherTimer reset-on-keystroke pattern; DatabaseService.UpdateNoteContentAsync already exists |
| EDIT-03 | Write frequency cap: new write cannot be scheduled sooner than debounce interval after previous write completed | Track lastWriteCompleted timestamp; skip timer reset if within cooldown |
| EDIT-04 | On app close: flush immediately, no data loss | Synchronous save in OnClosing/FlushAndClose before window destruction |
| EDIT-05 | On tab restore: reload content, cursor position, and scroll offset from database | Already partially implemented; add scroll offset restore via ScrollToVerticalOffset |
| EDIT-06 | Copy behavior: selection copied normally; no selection copies entire note silently | PreviewKeyDown intercept for Ctrl+C; Clipboard.SetText for full content |
| EDIT-07 | Save as TXT (Ctrl+S): OS save dialog, UTF-8 with BOM, default filename from tab name | SaveFileDialog + File.WriteAllText with UTF8 encoding (with BOM preamble) |
| UNDO-01 | Custom per-tab in-memory UndoStack (WPF native disabled) | UndoStack class with List<string> snapshots and int pointer |
| UNDO-02 | Tier-1: up to 50 full content snapshots, pushed on every debounced autosave if content differs | Push snapshot in autosave callback after DB write |
| UNDO-03 | Tier-2: up to 20 coarse checkpoints, saved every 5 minutes of active editing | Secondary DispatcherTimer (5-min interval) creates checkpoint if content differs from last checkpoint |
| UNDO-04 | Undo/redo pointer moves across both tiers seamlessly | Single merged traversal: exhaust tier-1 history first, then fall through to tier-2 |
| UNDO-05 | Global 50MB budget across all UndoStacks; collapse at 80%, target 60% | UndoManager singleton tracks total memory; triggers collapse when exceeding 40MB (80% of 50MB) |
| UNDO-06 | Collapse: oldest tabs first, tier-1 into tier-2, then evict oldest tier-2; active tab never collapsed | Sort tabs by last-access time; collapse from least-recently-used |
| UNDO-07 | Tab switch saves content/cursor to in-memory model; arriving tab binds its UndoStack | Extend TabList_SelectionChanged to swap UndoStack references |
| UNDO-08 | UndoStacks are in-memory only — not persisted, discarded on window close | No persistence needed; stacks live on NoteTab or in a Dictionary<long, UndoStack> |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WPF TextBox | .NET 10 | Text editing surface | Already in use; IsUndoEnabled=False disables native undo |
| DispatcherTimer | .NET 10 | Debounce timer on UI thread | WPF-native, no threading concerns, reset-friendly |
| System.Windows.Clipboard | .NET 10 | Copy to clipboard | WPF built-in, supports SetText and GetText |
| Microsoft.Win32.SaveFileDialog | .NET 10 | Save As TXT dialog | WPF built-in, remembers last directory automatically within session |
| System.Text.Encoding.UTF8 | .NET 10 | UTF-8 with BOM encoding | new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) for BOM |
| Microsoft.Data.Sqlite | existing | SQLite persistence | Already in use for note content writes |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.IO.File | .NET 10 | WriteAllText for TXT export | Save As handler |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| DispatcherTimer | System.Timers.Timer | Timer.Timer fires on thread pool, requires Dispatcher.Invoke; DispatcherTimer is simpler for WPF |
| List<string> for undo | LinkedList<string> | LinkedList has O(1) middle removal but undo needs index-based pointer navigation; List is simpler |

## Architecture Patterns

### Pattern 1: Reset-on-Keystroke Debounce
**What:** A DispatcherTimer that resets its countdown on every TextChanged event. The save only fires when the user stops typing for 500ms.
**When to use:** Autosave that shouldn't fire mid-word
**Example:**
```csharp
private DispatcherTimer? _autosaveTimer;
private DateTime _lastWriteCompleted = DateTime.MinValue;

private void ContentEditor_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_activeTab == null) return;

    // EDIT-03: Write frequency cap
    var elapsed = DateTime.Now - _lastWriteCompleted;
    if (elapsed.TotalMilliseconds < AutosaveDebounceMs)
    {
        // Don't reset timer if within cooldown — let existing timer fire
        if (_autosaveTimer?.IsEnabled == true) return;
    }

    _autosaveTimer?.Stop();
    _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutosaveDebounceMs) };
    _autosaveTimer.Tick += AutosaveTimer_Tick;
    _autosaveTimer.Start();
}

private async void AutosaveTimer_Tick(object? sender, EventArgs e)
{
    _autosaveTimer?.Stop();
    if (_activeTab == null) return;

    string content = ContentEditor.Text;
    if (content == _activeTab.Content) return;

    _activeTab.Content = content;
    _activeTab.UpdatedAt = DateTime.Now;
    await DatabaseService.UpdateNoteContentAsync(_activeTab.Id, content);
    _lastWriteCompleted = DateTime.Now;

    // Push undo snapshot
    _undoManager.PushSnapshot(_activeTab.Id, content);

    UpdateTabItemDisplay(_activeTab);
}
```

### Pattern 2: Two-Tier UndoStack with Pointer Navigation
**What:** A per-tab undo stack with tier-1 (fine-grained, 50 snapshots) and tier-2 (coarse, 20 checkpoints). A single pointer walks backwards through tier-1, then seamlessly continues into tier-2.
**When to use:** Undo/redo with memory-efficient long history
**Example:**
```csharp
public class UndoStack
{
    private readonly List<string> _tier1 = new(); // Fine-grained: max 50
    private readonly List<string> _tier2 = new(); // Coarse: max 20
    private int _pointer = -1; // Current position in tier-1
    private bool _inTier2 = false;
    private int _tier2Pointer = -1;

    public const int MaxTier1 = 50;
    public const int MaxTier2 = 20;

    public long EstimatedBytes =>
        _tier1.Sum(s => (long)s.Length * 2) +
        _tier2.Sum(s => (long)s.Length * 2);
}
```

### Pattern 3: PreviewKeyDown Interception for Undo/Redo
**What:** WPF's tunneling PreviewKeyDown fires before the TextBox processes keys. Setting e.Handled = true prevents the TextBox from ever seeing Ctrl+Z/Y.
**When to use:** Custom undo/redo that replaces WPF's native behavior
**Example:**
```csharp
// In Window_PreviewKeyDown:
if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
{
    PerformUndo();
    e.Handled = true;
    return;
}
if ((e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) ||
    (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
{
    PerformRedo();
    e.Handled = true;
    return;
}
```

### Pattern 4: Synchronous Flush on Close
**What:** Block the closing sequence until all pending saves complete. Use `.GetAwaiter().GetResult()` sparingly — only in the shutdown path where blocking the UI thread is acceptable.
**When to use:** App close where data loss is unacceptable
**Example:**
```csharp
protected override void OnClosing(CancelEventArgs e)
{
    // Stop autosave timer to prevent races
    _autosaveTimer?.Stop();

    // Synchronous flush — block until save completes
    FlushContentSync();

    base.OnClosing(e);
}

private void FlushContentSync()
{
    if (_activeTab == null) return;
    string content = ContentEditor.Text;
    if (content == _activeTab.Content) return;

    _activeTab.Content = content;
    DatabaseService.UpdateNoteContentAsync(_activeTab.Id, content)
        .GetAwaiter().GetResult(); // Safe: only called during shutdown
}
```

### Anti-Patterns to Avoid
- **Using Task.Run for debounce timer:** DispatcherTimer runs on UI thread — no marshalling needed. Task.Run + Dispatcher.Invoke is overcomplicated.
- **Storing undo snapshots as diffs:** For plain text notes (typically < 10KB), full content snapshots are simpler and fast enough. Diff/patch adds complexity with no real benefit at this scale.
- **Using WPF's TextBox.UndoLimit:** Already disabled (IsUndoEnabled=False). The native undo clears on programmatic Text assignment (tab switch), making it useless for per-tab undo.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Debounce timer | Custom thread-based timer | DispatcherTimer | UI-thread safe, built into WPF |
| File save dialog | Custom file picker UI | Microsoft.Win32.SaveFileDialog | OS-native, remembers last directory, handles edge cases |
| Clipboard access | P/Invoke clipboard APIs | System.Windows.Clipboard | WPF wrapper handles clipboard ownership and format negotiation |
| UTF-8 BOM encoding | Manual BOM byte insertion | new UTF8Encoding(true) | Framework handles the 3-byte BOM preamble correctly |

## Common Pitfalls

### Pitfall 1: TextChanged fires on programmatic Text assignment
**What goes wrong:** Setting `ContentEditor.Text = tab.Content` during tab switch fires TextChanged, which resets the autosave timer and potentially pushes a false undo snapshot.
**Why it happens:** WPF TextBox.TextChanged fires for ALL text changes, not just user input.
**How to avoid:** Use a `_suppressTextChanged` boolean flag. Set true before programmatic text assignment, clear after.
**Warning signs:** Undo history contains duplicate entries of the same content; autosave fires immediately on tab switch.

### Pitfall 2: DispatcherTimer not stopped on tab switch
**What goes wrong:** Timer from tab A fires after switching to tab B, saving tab B's content as tab A's content.
**Why it happens:** The timer callback captures `_activeTab` which changes on tab switch.
**How to avoid:** Stop the autosave timer in TabList_SelectionChanged before switching `_activeTab`. Flush pending content for the outgoing tab first.
**Warning signs:** Content appears in wrong tabs; database shows cross-contamination.

### Pitfall 3: Clipboard.SetText throws on STA thread issues
**What goes wrong:** Clipboard.SetText can throw ExternalException if the clipboard is locked by another application.
**Why it happens:** Windows clipboard is a shared resource with exclusive locking.
**How to avoid:** Wrap in try/catch; log the error silently (per user decision: no visual feedback).
**Warning signs:** Occasional clipboard failures with COM exceptions.

### Pitfall 4: SaveFileDialog.InitialDirectory persistence
**What goes wrong:** Setting InitialDirectory manually requires tracking state. Not setting it defaults to Documents.
**Why it happens:** SaveFileDialog.RestoreDirectory defaults to false, which means it changes the process CWD.
**How to avoid:** Track `_lastSaveDirectory` as a string field. Set SaveFileDialog.InitialDirectory to it. Update after successful save. Per user decision: resets on app launch (field default is null/empty).
**Warning signs:** Dialog opens in unexpected directories.

### Pitfall 5: Memory estimation using string.Length vs bytes
**What goes wrong:** string.Length returns character count, not byte count. The 50MB budget is in bytes.
**Why it happens:** .NET strings are UTF-16 internally (2 bytes per char), but string.Length returns chars.
**How to avoid:** Multiply `string.Length * 2` for memory estimation (sizeof(char) = 2 in .NET). This gives a reasonable approximation for the 50MB budget without the overhead of Encoding.GetByteCount.
**Warning signs:** Memory budget triggers too early (if using Length directly) or too late (if ignoring overhead).

### Pitfall 6: Undo replaces Text which fires TextChanged
**What goes wrong:** Performing undo sets ContentEditor.Text to the previous snapshot, which fires TextChanged, which pushes a new snapshot and resets the timer.
**Why it happens:** Same as Pitfall 1 — TextChanged fires on programmatic assignment.
**How to avoid:** Set `_suppressTextChanged = true` before undo/redo text restoration.
**Warning signs:** Undo stack grows after undo operations; redo history destroyed by undo.

## Code Examples

### Enhanced Copy (Ctrl+C with no selection)
```csharp
if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
{
    if (ContentEditor.SelectionLength == 0 && _activeTab != null)
    {
        try { Clipboard.SetText(_activeTab.Content); }
        catch (Exception ex) { LogService.Warn("Clipboard access failed", ex); }
        e.Handled = true;
        return;
    }
    // If there IS a selection, let WPF handle it normally (don't set e.Handled)
}
```

### Save As TXT (Ctrl+S)
```csharp
private string? _lastSaveDirectory;

private void SaveAsTxt()
{
    if (_activeTab == null) return;

    var dialog = new Microsoft.Win32.SaveFileDialog
    {
        Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
        DefaultExt = ".txt",
        FileName = GetDefaultFilename(_activeTab)
    };

    if (!string.IsNullOrEmpty(_lastSaveDirectory))
        dialog.InitialDirectory = _lastSaveDirectory;

    if (dialog.ShowDialog(this) == true)
    {
        var utf8Bom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        System.IO.File.WriteAllText(dialog.FileName, _activeTab.Content, utf8Bom);
        _lastSaveDirectory = System.IO.Path.GetDirectoryName(dialog.FileName);
    }
}

private static string GetDefaultFilename(NoteTab tab)
{
    if (!string.IsNullOrWhiteSpace(tab.Name))
        return SanitizeFilename(tab.Name) + ".txt";
    if (!string.IsNullOrWhiteSpace(tab.Content))
        return SanitizeFilename(tab.Content.Trim()[..Math.Min(30, tab.Content.Trim().Length)]) + ".txt";
    return $"JoJot note {DateTime.Now:yyyy-MM-dd}.txt";
}
```

### Scroll Offset Save/Restore
```csharp
// Save (in TabList_SelectionChanged before switch):
if (_activeTab != null)
{
    var scrollViewer = GetScrollViewer(ContentEditor);
    _activeTab.EditorScrollOffset = (int)(scrollViewer?.VerticalOffset ?? 0);
    _activeTab.CursorPosition = ContentEditor.CaretIndex;
}

// Restore (after setting text):
ContentEditor.CaretIndex = Math.Min(tab.CursorPosition, ContentEditor.Text.Length);
ContentEditor.Dispatcher.BeginInvoke(() =>
{
    var scrollViewer = GetScrollViewer(ContentEditor);
    scrollViewer?.ScrollToVerticalOffset(tab.EditorScrollOffset);
}, System.Windows.Threading.DispatcherPriority.Loaded);

// Helper to find ScrollViewer inside TextBox:
private static ScrollViewer? GetScrollViewer(DependencyObject parent)
{
    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
    {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is ScrollViewer sv) return sv;
        var result = GetScrollViewer(child);
        if (result != null) return result;
    }
    return null;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| WPF TextBox.IsUndoEnabled=True | Custom UndoStack | By design | Native undo clears on programmatic Text set; custom stack survives tab switches |
| Immediate save (fire-and-forget) | Debounced autosave | Phase 6 | Reduces write frequency, groups rapid keystrokes into single save |

## Open Questions

1. **DispatcherTimer reuse vs recreation**
   - What we know: Stopping and restarting a single DispatcherTimer is more efficient than creating new ones
   - What's unclear: Whether Stop() + changing Interval + Start() has any edge cases in WPF
   - Recommendation: Reuse a single DispatcherTimer instance; just Stop() and Start() on each keystroke. No need to recreate.

2. **Tier-2 checkpoint timer behavior during idle**
   - What we know: 5-minute checkpoints should only fire during "active editing"
   - What's unclear: How to define "active editing" — is it any TextChanged, or only user-initiated?
   - Recommendation: Start/reset a 5-minute DispatcherTimer on user-initiated TextChanged (when !_suppressTextChanged). If timer fires, save checkpoint if content differs from last checkpoint. Stop timer on tab switch.

## Sources

### Primary (HIGH confidence)
- WPF TextBox documentation — TextChanged event, IsUndoEnabled, CaretIndex
- WPF DispatcherTimer — Interval, Start/Stop, Tick event
- Microsoft.Win32.SaveFileDialog — Filter, DefaultExt, InitialDirectory
- System.Windows.Clipboard — SetText, GetText
- System.Text.UTF8Encoding(true) — BOM preamble

### Secondary (MEDIUM confidence)
- .NET string memory: 2 bytes per char (UTF-16 internal representation) — well-documented CLR behavior

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All WPF built-in APIs, well-documented
- Architecture: HIGH - Standard debounce + undo stack patterns, proven in similar apps
- Pitfalls: HIGH - Based on established WPF TextBox behaviors and prior project decisions (IsUndoEnabled=False)

**Research date:** 2026-03-03
**Valid until:** Indefinite — WPF APIs are stable and mature
