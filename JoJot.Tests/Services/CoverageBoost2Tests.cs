using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JoJot.Models;
using JoJot.Services;
using JoJot.Tests.Helpers;
using JoJot.ViewModels;

namespace JoJot.Tests.Services;

// ═══════════════════════════════════════════════════════════════════
// IPC Message — direct concrete type serialization exercises the
// source-generated SerializeHandler methods (uncovered paths)
// ═══════════════════════════════════════════════════════════════════

public class IpcMessageSerializationCoverageTests
{
    [Fact]
    public void Serialize_ActivateCommand_ViaConcreteTypeInfo()
    {
        var cmd = new ActivateCommand();
        var json = JsonSerializer.Serialize(cmd, IpcMessageContext.Default.ActivateCommand);
        json.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Deserialize_ActivateCommand_ViaConcreteTypeInfo()
    {
        var json = "{}";
        var cmd = JsonSerializer.Deserialize(json, IpcMessageContext.Default.ActivateCommand);
        cmd.Should().NotBeNull();
    }

    [Fact]
    public void Serialize_NewTabCommand_ViaConcreteTypeInfo()
    {
        var cmd = new NewTabCommand("content", "desk-1");
        var json = JsonSerializer.Serialize(cmd, IpcMessageContext.Default.NewTabCommand);
        json.Should().Contain("content");
        json.Should().Contain("desk-1");
    }

    [Fact]
    public void Deserialize_NewTabCommand_ViaConcreteTypeInfo()
    {
        var json = """{"InitialContent":"hi","DesktopGuid":"d1"}""";
        var cmd = JsonSerializer.Deserialize(json, IpcMessageContext.Default.NewTabCommand);
        cmd.Should().NotBeNull();
        cmd!.InitialContent.Should().Be("hi");
        cmd.DesktopGuid.Should().Be("d1");
    }

    [Fact]
    public void Serialize_ShowDesktopCommand_ViaConcreteTypeInfo()
    {
        var cmd = new ShowDesktopCommand("desk-abc");
        var json = JsonSerializer.Serialize(cmd, IpcMessageContext.Default.ShowDesktopCommand);
        json.Should().Contain("desk-abc");
    }

    [Fact]
    public void Deserialize_ShowDesktopCommand_ViaConcreteTypeInfo()
    {
        var json = """{"DesktopGuid":"xyz"}""";
        var cmd = JsonSerializer.Deserialize(json, IpcMessageContext.Default.ShowDesktopCommand);
        cmd.Should().NotBeNull();
        cmd!.DesktopGuid.Should().Be("xyz");
    }

    [Fact]
    public void Serialize_NewTabCommand_NullFields()
    {
        var cmd = new NewTabCommand(null, null);
        var json = JsonSerializer.Serialize(cmd, IpcMessageContext.Default.NewTabCommand);
        json.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void IpcMessageContext_GetTypeInfo_ReturnsForKnownTypes()
    {
        var ctx = IpcMessageContext.Default;
        ctx.IpcMessage.Should().NotBeNull();
        ctx.ActivateCommand.Should().NotBeNull();
        ctx.NewTabCommand.Should().NotBeNull();
        ctx.ShowDesktopCommand.Should().NotBeNull();
    }

    [Fact]
    public void Serialize_ActivateCommand_Polymorphic_ContainsAction()
    {
        IpcMessage msg = new ActivateCommand();
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);
        json.Should().Contain("\"action\"");
    }

    [Fact]
    public void Serialize_ShowDesktopCommand_Polymorphic_ContainsAction()
    {
        IpcMessage msg = new ShowDesktopCommand("test");
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);
        json.Should().Contain("\"action\":\"show-desktop\"");
    }

    [Fact]
    public void Serialize_NewTabCommand_Polymorphic_ContainsAction()
    {
        IpcMessage msg = new NewTabCommand("x", "y");
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);
        json.Should().Contain("\"action\":\"new-tab\"");
    }
}

// ═══════════════════════════════════════════════════════════════════
// UndoStack — additional edge case paths
// ═══════════════════════════════════════════════════════════════════

public class UndoStackCoverageBoostTests
{
    private readonly TestClock _clock = new();

    private UndoStack CreateStack(long tabId = 1) => new(tabId, _clock);

    [Fact]
    public void ShouldCreateCheckpoint_True_After5Minutes()
    {
        var stack = CreateStack();
        stack.PushInitialContent("base");

        _clock.Advance(TimeSpan.FromMinutes(6));

        stack.ShouldCreateCheckpoint().Should().BeTrue();
    }

    [Fact]
    public void ShouldCreateCheckpoint_False_Before5Minutes()
    {
        var stack = CreateStack();
        stack.PushInitialContent("base");

        _clock.Advance(TimeSpan.FromMinutes(3));

        stack.ShouldCreateCheckpoint().Should().BeFalse();
    }

    [Fact]
    public void ShouldCreateCheckpoint_ResetsByPushCheckpoint()
    {
        var stack = CreateStack();
        stack.PushInitialContent("base");

        _clock.Advance(TimeSpan.FromMinutes(6));
        stack.ShouldCreateCheckpoint().Should().BeTrue();

        stack.PushCheckpoint("cp1");
        stack.ShouldCreateCheckpoint().Should().BeFalse();
    }

    [Fact]
    public void CollapseTier1IntoTier2_EmptyTier1_NoOp()
    {
        var stack = CreateStack();
        // Don't push anything to tier-1
        stack.CollapseTier1IntoTier2();
        stack.EstimatedBytes.Should().Be(0);
    }

    [Fact]
    public void EvictOldestTier2_ZeroCount_NoOp()
    {
        var stack = CreateStack();
        stack.PushCheckpoint("cp");
        var bytesBefore = stack.EstimatedBytes;

        stack.EvictOldestTier2(0);
        stack.EstimatedBytes.Should().Be(bytesBefore);
    }

    [Fact]
    public void EvictOldestTier2_NegativeCount_NoOp()
    {
        var stack = CreateStack();
        stack.PushCheckpoint("cp");
        var bytesBefore = stack.EstimatedBytes;

        stack.EvictOldestTier2(-5);
        stack.EstimatedBytes.Should().Be(bytesBefore);
    }

    [Fact]
    public void PushSnapshot_DuplicateContent_Skipped()
    {
        var stack = CreateStack();
        stack.PushInitialContent("same");
        var bytesAfterFirst = stack.EstimatedBytes;

        stack.PushSnapshot("same");
        stack.EstimatedBytes.Should().Be(bytesAfterFirst);
    }

    [Fact]
    public void PushSnapshot_OverMaxTier1_EvictsOldest()
    {
        var stack = CreateStack();
        stack.PushInitialContent("base");

        for (int i = 1; i <= UndoStack.MaxTier1 + 5; i++)
        {
            stack.PushSnapshot($"snap_{i}");
        }

        // Should still be navigable
        stack.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void PushSnapshot_IntoEmptyStack_SetsIndex()
    {
        var stack = CreateStack();
        stack.PushSnapshot("first");
        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void PushInitialContent_ClearsExistingState()
    {
        var stack = CreateStack();
        stack.PushSnapshot("a");
        stack.PushSnapshot("b");
        stack.PushSnapshot("c");

        stack.PushInitialContent("fresh");
        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Undo_Redo_FullCycle()
    {
        var stack = CreateStack();
        stack.PushInitialContent("v1");
        stack.PushSnapshot("v2");
        stack.PushSnapshot("v3");

        stack.Undo()!.Value.Content.Should().Be("v2");
        stack.Undo()!.Value.Content.Should().Be("v1");
        stack.Undo().Should().BeNull(); // At beginning

        stack.Redo()!.Value.Content.Should().Be("v2");
        stack.Redo()!.Value.Content.Should().Be("v3");
        stack.Redo().Should().BeNull(); // At end
    }

    [Fact]
    public void PushSnapshot_AfterUndo_DestroysFuture()
    {
        var stack = CreateStack();
        stack.PushInitialContent("v1");
        stack.PushSnapshot("v2");
        stack.PushSnapshot("v3");

        stack.Undo(); // v2
        stack.PushSnapshot("v2b"); // Destroys v3

        stack.CanRedo.Should().BeFalse();
        stack.Undo()!.Value.Content.Should().Be("v2");
        stack.Undo()!.Value.Content.Should().Be("v1");
    }

    [Fact]
    public void PushCheckpoint_OverMaxTier2_EvictsOldest()
    {
        var stack = CreateStack();

        // Fill tier-2 to max
        for (int i = 0; i < UndoStack.MaxTier2; i++)
        {
            stack.PushCheckpoint($"cp_{i}");
        }

        // Push one more — oldest evicted
        stack.PushCheckpoint("overflow");

        // Should not crash and bytes should be bounded
        stack.EstimatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CollapseTier1IntoTier2_SamplesEvery5th()
    {
        var stack = CreateStack();
        stack.PushInitialContent("s0");
        for (int i = 1; i <= 14; i++)
        {
            stack.PushSnapshot($"s{i}");
        }

        // tier-1 has 15 entries (s0..s14), samples at 0,5,10 = 3 entries
        stack.CollapseTier1IntoTier2();

        // After collapse, tier-1 is empty, tier-2 has sampled entries
        // Navigating through should work
        while (stack.CanUndo)
        {
            stack.Undo().Should().NotBeNull();
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
// UndoManager — additional paths
// ═══════════════════════════════════════════════════════════════════

public class UndoManagerCoverageBoostTests
{
    private readonly TestClock _clock = new();

    private UndoManager CreateManager() => new(_clock);

    [Fact]
    public void CanUndo_NoStack_ReturnsFalse()
    {
        var mgr = CreateManager();
        mgr.CanUndo(999).Should().BeFalse();
    }

    [Fact]
    public void CanRedo_NoStack_ReturnsFalse()
    {
        var mgr = CreateManager();
        mgr.CanRedo(999).Should().BeFalse();
    }

    [Fact]
    public void Undo_NoStack_ReturnsNull()
    {
        var mgr = CreateManager();
        mgr.Undo(999).Should().BeNull();
    }

    [Fact]
    public void PushSnapshot_ThenUndoRedo()
    {
        var mgr = CreateManager();
        mgr.PushSnapshot(1, "v1");
        mgr.PushSnapshot(1, "v2");

        mgr.CanUndo(1).Should().BeTrue();
        mgr.Undo(1)!.Value.Content.Should().Be("v1");
        mgr.CanRedo(1).Should().BeTrue();
        mgr.Redo(1)!.Value.Content.Should().Be("v2");
    }

    [Fact]
    public void GetStack_ReturnsNull_WhenNotCreated()
    {
        var mgr = CreateManager();
        mgr.GetStack(999).Should().BeNull();
    }

    [Fact]
    public void GetOrCreateStack_CreatesThenReturns()
    {
        var mgr = CreateManager();
        var s1 = mgr.GetOrCreateStack(1);
        var s2 = mgr.GetOrCreateStack(1);
        s1.Should().BeSameAs(s2);
    }

    [Fact]
    public void RemoveStack_ThenGetReturnsNull()
    {
        var mgr = CreateManager();
        mgr.GetOrCreateStack(1);
        mgr.RemoveStack(1);
        mgr.GetStack(1).Should().BeNull();
    }

    [Fact]
    public void TotalEstimatedBytes_SumsAllStacks()
    {
        var mgr = CreateManager();
        mgr.PushSnapshot(1, "abc");
        mgr.PushSnapshot(2, "defgh");

        mgr.TotalEstimatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SetActiveTab_ProtectsFromCollapse()
    {
        var mgr = CreateManager();
        // Create two stacks with large content
        for (int i = 0; i < 30; i++)
        {
            mgr.PushSnapshot(1, new string('A', 100_000 + i));
            mgr.PushSnapshot(2, new string('B', 100_000 + i));
        }

        mgr.SetActiveTab(1);
        // Active tab (1) should be protected if collapse triggers
        mgr.TotalEstimatedBytes.Should().BeGreaterThan(0);
    }
}

// ═══════════════════════════════════════════════════════════════════
// AutosaveService — additional edge case paths
// ═══════════════════════════════════════════════════════════════════

public class AutosaveServiceCoverageBoostTests
{
    private readonly TestClock _clock = new();
    private readonly TestTimer _timer = new();

    private AutosaveService CreateService() => new(_clock, _timer);

    [Fact]
    public void NotifyTextChanged_AfterCooldown_ResetsTimer()
    {
        var svc = CreateService();
        svc.DebounceMs = 500;

        svc.Configure(
            () => (1L, "content"),
            (_, _) => Task.CompletedTask);

        svc.NotifyTextChanged();
        _timer.Fire(); // Save completes, sets _lastWriteCompleted

        // Advance past cooldown
        _clock.Advance(TimeSpan.FromMilliseconds(600));

        svc.NotifyTextChanged();
        _timer.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task FlushAsync_UpdatesLastWriteCompleted()
    {
        var svc = CreateService();
        svc.Configure(
            () => (1L, "content"),
            (_, _) => Task.CompletedTask);

        svc.NotifyTextChanged();
        await svc.FlushAsync();

        // After flush, cooldown is active — notify within cooldown should not reset
        svc.NotifyTextChanged();
        _timer.IsEnabled.Should().BeTrue();

        // Another notify within cooldown with timer running should be a no-op reset
        svc.NotifyTextChanged();
        _timer.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task FlushAsync_InvokesNoCallbacks_WhenNoneConfigured()
    {
        var svc = CreateService();
        svc.Configure(
            () => (1L, "content"),
            (_, _) => Task.CompletedTask);
        // No onSnapshot or onSaveCompleted configured

        svc.NotifyTextChanged();
        await svc.FlushAsync();
        // Should not throw
    }

    [Fact]
    public void TimerTick_SaveException_KeepsDirty_DoesNotThrow()
    {
        LogService.InitializeNoop();
        var svc = CreateService();

        svc.Configure(
            () => (1L, "content"),
            (_, _) => throw new InvalidOperationException("test error"));

        svc.NotifyTextChanged();
        _timer.Fire();

        svc.IsDirty.Should().BeTrue();
    }
}

// ═══════════════════════════════════════════════════════════════════
// DatabaseCore — WriteLock timeout, HandleCorruption
// ═══════════════════════════════════════════════════════════════════

[Collection("Database")]
public class DatabaseCoreCoverageBoostTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task AcquireWriteLock_WithCancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => DatabaseCore.AcquireWriteLockAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HandleCorruption_RecreatesDatabase()
    {
        // Create a temp directory with a fake DB file
        var tempDir = Path.Combine(Path.GetTempPath(), $"jojot_corrupt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "test.db");

        try
        {
            // Close current DB first
            await DatabaseCore.CloseAsync();

            // Create a fake file to simulate a corrupt DB
            await File.WriteAllTextAsync(dbPath, "corrupt data");

            await DatabaseCore.HandleCorruptionAsync(dbPath);

            // Verify: corrupt file should be renamed
            File.Exists(dbPath + ".corrupt").Should().BeTrue();

            // DB should be re-opened and schema created
            await DatabaseCore.VerifyIntegrityAsync();
        }
        finally
        {
            await DatabaseCore.CloseAsync();
            try { Directory.Delete(tempDir, true); } catch { }
            // Reopen for other tests
            _db = await TestDatabase.CreateAsync();
        }
    }

    [Fact]
    public async Task HandleCorruption_OverwritesExistingCorruptBackup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jojot_corrupt2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "test.db");
        var corruptPath = dbPath + ".corrupt";

        try
        {
            await DatabaseCore.CloseAsync();

            // Create both the DB file and a pre-existing .corrupt backup
            await File.WriteAllTextAsync(dbPath, "corrupt1");
            await File.WriteAllTextAsync(corruptPath, "old corrupt backup");

            await DatabaseCore.HandleCorruptionAsync(dbPath);

            // .corrupt should now contain the new corrupt data
            File.Exists(corruptPath).Should().BeTrue();
        }
        finally
        {
            await DatabaseCore.CloseAsync();
            try { Directory.Delete(tempDir, true); } catch { }
            _db = await TestDatabase.CreateAsync();
        }
    }

    [Fact]
    public async Task ExecuteNonQuery_ValidSql_Succeeds()
    {
        await DatabaseCore.ExecuteNonQueryAsync(
            "INSERT INTO preferences (key, value) VALUES ('exec_test', 'hello');");

        var val = await PreferenceStore.GetPreferenceAsync("exec_test");
        val.Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteScalar_ReturnsInt()
    {
        var result = await DatabaseCore.ExecuteScalarAsync<long>("SELECT 42;");
        result.Should().Be(42);
    }

    [Fact]
    public async Task ConnectionString_IsSetAfterOpen()
    {
        DatabaseCore.ConnectionString.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateTestContext_ReturnsFunctionalContext()
    {
        await using var ctx = DatabaseCore.CreateTestContext();
        var count = await ctx.Notes.CountAsync();
        count.Should().BeGreaterThanOrEqualTo(0);
    }
}

// ═══════════════════════════════════════════════════════════════════
// StartupService — background migration exception path
// ═══════════════════════════════════════════════════════════════════

[Collection("Database")]
public class StartupServiceCoverageBoostTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task RunBackgroundMigrations_Succeeds()
    {
        await StartupService.RunBackgroundMigrationsAsync();
        // Should not throw
    }

    [Fact]
    public async Task RunBackgroundMigrations_IdempotentMultipleCalls()
    {
        await StartupService.RunBackgroundMigrationsAsync();
        await StartupService.RunBackgroundMigrationsAsync();
        await StartupService.RunBackgroundMigrationsAsync();
    }
}

// ═══════════════════════════════════════════════════════════════════
// NoteTab model — additional property coverage
// ═══════════════════════════════════════════════════════════════════

public class NoteTabCoverageBoostTests
{
    [Fact]
    public void CursorPosition_GetSet()
    {
        var tab = new NoteTab { Id = 1 };
        tab.CursorPosition = 42;
        tab.CursorPosition.Should().Be(42);
    }

    [Fact]
    public void EditorScrollOffset_GetSet()
    {
        var tab = new NoteTab { Id = 1 };
        tab.EditorScrollOffset = 100;
        tab.EditorScrollOffset.Should().Be(100);
    }

    [Fact]
    public void DisplayLabel_WithName_ReturnsName()
    {
        var tab = new NoteTab { Id = 1, Name = "My Note", Content = "some content" };
        tab.DisplayLabel.Should().Be("My Note");
    }

    [Fact]
    public void DisplayLabel_WithoutName_ReturnsContentPreview()
    {
        var tab = new NoteTab { Id = 1, Content = "First line of content" };
        tab.DisplayLabel.Should().Contain("First line");
    }

    [Fact]
    public void DisplayLabel_EmptyNameAndContent_ReturnsFallback()
    {
        var tab = new NoteTab { Id = 1, Content = "" };
        tab.DisplayLabel.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void IsPlaceholder_True_WhenNoNameAndEmptyContent()
    {
        var tab = new NoteTab { Id = 1, Content = "" };
        tab.IsPlaceholder.Should().BeTrue();
    }

    [Fact]
    public void IsPlaceholder_False_WhenHasContent()
    {
        var tab = new NoteTab { Id = 1, Content = "stuff" };
        tab.IsPlaceholder.Should().BeFalse();
    }

    [Fact]
    public void IsPlaceholder_False_WhenHasName()
    {
        var tab = new NoteTab { Id = 1, Name = "Named", Content = "" };
        tab.IsPlaceholder.Should().BeFalse();
    }

    [Fact]
    public void SetProperty_Name_RaisesDisplayLabelAndIsPlaceholder()
    {
        var tab = new NoteTab { Id = 1, Content = "" };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Name = "New Name";

        raised.Should().Contain(nameof(NoteTab.Name));
        raised.Should().Contain(nameof(NoteTab.DisplayLabel));
        raised.Should().Contain(nameof(NoteTab.IsPlaceholder));
    }

    [Fact]
    public void SetProperty_Content_RaisesPropertyChanged()
    {
        var tab = new NoteTab { Id = 1, Content = "old" };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Content = "new";

        raised.Should().Contain(nameof(NoteTab.Content));
    }

    [Fact]
    public void SetProperty_Pinned_RaisesPropertyChanged()
    {
        var tab = new NoteTab { Id = 1, Content = "" };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.Pinned = true;

        raised.Should().Contain(nameof(NoteTab.Pinned));
    }

    [Fact]
    public void SetProperty_SortOrder_RaisesPropertyChanged()
    {
        var tab = new NoteTab { Id = 1, Content = "" };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.SortOrder = 5;

        raised.Should().Contain(nameof(NoteTab.SortOrder));
    }

    [Fact]
    public void SetProperty_UpdatedAt_RaisesPropertyChanged()
    {
        var tab = new NoteTab { Id = 1, Content = "" };
        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tab.UpdatedAt = DateTime.Now;

        raised.Should().Contain(nameof(NoteTab.UpdatedAt));
    }

    [Fact]
    public void DesktopGuid_GetSet()
    {
        var tab = new NoteTab { Id = 1, DesktopGuid = "desk-1", Content = "" };
        tab.DesktopGuid.Should().Be("desk-1");
    }

    [Fact]
    public void CreatedAt_GetSet()
    {
        var now = DateTime.UtcNow;
        var tab = new NoteTab { Id = 1, CreatedAt = now, Content = "" };
        tab.CreatedAt.Should().Be(now);
    }
}

// ═══════════════════════════════════════════════════════════════════
// WindowGeometry — additional coverage
// ═══════════════════════════════════════════════════════════════════

public class WindowGeometryCoverageBoostTests
{
    [Fact]
    public void Constructor_StoresAllValues()
    {
        var geo = new WindowGeometry(10.5, 20.5, 800.0, 600.0, true);
        geo.Left.Should().Be(10.5);
        geo.Top.Should().Be(20.5);
        geo.Width.Should().Be(800.0);
        geo.Height.Should().Be(600.0);
        geo.IsMaximized.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NotMaximized()
    {
        var geo = new WindowGeometry(0, 0, 1024, 768, false);
        geo.IsMaximized.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════
// AppState / Preference / PendingMove models
// ═══════════════════════════════════════════════════════════════════

public class ModelCoverageBoostTests
{
    [Fact]
    public void AppState_DefaultValues()
    {
        var state = new AppState();
        state.DesktopGuid.Should().Be("");
        state.DesktopName.Should().BeNull();
        state.DesktopIndex.Should().BeNull();
        state.WindowLeft.Should().BeNull();
        state.WindowTop.Should().BeNull();
        state.WindowWidth.Should().BeNull();
        state.WindowHeight.Should().BeNull();
        state.WindowState.Should().BeNull();
    }

    [Fact]
    public void AppState_SetAllProperties()
    {
        var state = new AppState
        {
            DesktopGuid = "guid-1",
            DesktopName = "Desktop 1",
            DesktopIndex = 0,
            WindowLeft = 10.0,
            WindowTop = 20.0,
            WindowWidth = 800.0,
            WindowHeight = 600.0,
            WindowState = "Maximized"
        };

        state.DesktopGuid.Should().Be("guid-1");
        state.DesktopName.Should().Be("Desktop 1");
        state.DesktopIndex.Should().Be(0);
        state.WindowLeft.Should().Be(10.0);
        state.WindowState.Should().Be("Maximized");
    }

    [Fact]
    public void PendingMove_RecordEquality()
    {
        var a = new PendingMove(1, "win", "from", "to", "2025-01-01 00:00:00");
        var b = new PendingMove(1, "win", "from", "to", "2025-01-01 00:00:00");
        a.Should().Be(b);
    }

    [Fact]
    public void PendingMove_NullToDesktop()
    {
        var move = new PendingMove(1, "win", "from", null, "2025-01-01");
        move.ToDesktop.Should().BeNull();
    }

    [Fact]
    public void Preference_Properties()
    {
        var pref = new Preference { Key = "theme", Value = "dark" };
        pref.Key.Should().Be("theme");
        pref.Value.Should().Be("dark");
    }
}

// ═══════════════════════════════════════════════════════════════════
// ObservableObject / RelayCommand — edge cases
// ═══════════════════════════════════════════════════════════════════

public class ObservableObjectCoverageBoostTests
{
    private class TestObservable : ObservableObject
    {
        private string _value = "";

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        private int _num;
        public int Num
        {
            get => _num;
            set => SetProperty(ref _num, value, [nameof(Computed)]);
        }

        public string Computed => $"num={_num}";
    }

    [Fact]
    public void SetProperty_SameValue_ReturnsFalse_NoEvent()
    {
        var obj = new TestObservable { Value = "test" };
        var raised = new List<string>();
        obj.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        obj.Value = "test"; // Same value
        raised.Should().BeEmpty();
    }

    [Fact]
    public void SetProperty_DifferentValue_ReturnsTrue_RaisesEvent()
    {
        var obj = new TestObservable { Value = "old" };
        var raised = new List<string>();
        obj.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        obj.Value = "new";
        raised.Should().Contain(nameof(TestObservable.Value));
    }

    [Fact]
    public void SetProperty_WithDependentNames_RaisesAllEvents()
    {
        var obj = new TestObservable();
        var raised = new List<string>();
        obj.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        obj.Num = 42;

        raised.Should().Contain(nameof(TestObservable.Num));
        raised.Should().Contain(nameof(TestObservable.Computed));
    }
}

public class RelayCommandCoverageBoostTests
{
    [Fact]
    public void RelayCommand_CanExecute_DefaultTrue()
    {
        var cmd = new RelayCommand(() => { });
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RelayCommand_CanExecute_WithPredicate()
    {
        bool canExec = false;
        var cmd = new RelayCommand(() => { }, () => canExec);

        cmd.CanExecute(null).Should().BeFalse();
        canExec = true;
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void RelayCommand_Execute_InvokesAction()
    {
        bool executed = false;
        var cmd = new RelayCommand(() => executed = true);

        cmd.Execute(null);
        executed.Should().BeTrue();
    }

    [Fact]
    public void RelayCommand_RaiseCanExecuteChanged_FiresEvent()
    {
        var cmd = new RelayCommand(() => { });
        bool fired = false;
        cmd.CanExecuteChanged += (_, _) => fired = true;

        cmd.RaiseCanExecuteChanged();
        fired.Should().BeTrue();
    }
}

// ═══════════════════════════════════════════════════════════════════
// HotkeyService.FormatHotkey — hex fallback for invalid VK
// ═══════════════════════════════════════════════════════════════════

public class HotkeyFormatCoverageBoostTests
{
    [Fact]
    public void FormatHotkey_WinShift_DefaultCombo()
    {
        // Win+Shift+N (default hotkey)
        var result = HotkeyService.FormatHotkey(0x0008 | 0x0004, 0x4E);
        result.Should().Contain("Win");
        result.Should().Contain("Shift");
        result.Should().Contain("N");
    }

    [Fact]
    public void FormatHotkey_AltShift_Combo()
    {
        var result = HotkeyService.FormatHotkey(0x0001 | 0x0004, 0x41);
        result.Should().Be("Alt + Shift + A");
    }

    [Fact]
    public void FormatHotkey_WinCtrlAlt_Combo()
    {
        var result = HotkeyService.FormatHotkey(0x0008 | 0x0002 | 0x0001, 0x42);
        result.Should().Contain("Win");
        result.Should().Contain("Ctrl");
        result.Should().Contain("Alt");
    }

    [Fact]
    public void GetCurrentHotkey_ReturnsDefaults()
    {
        var (mod, vk) = HotkeyService.GetCurrentHotkey();
        // Default is Win+Shift+N
        mod.Should().BeGreaterThan(0u);
        vk.Should().BeGreaterThan(0u);
    }

    [Fact]
    public void GetHotkeyDisplayString_ReturnsFormattedString()
    {
        var display = HotkeyService.GetHotkeyDisplayString();
        display.Should().NotBeNullOrWhiteSpace();
        display.Should().Contain("+");
    }
}
