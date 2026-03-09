using JoJot.Data;
using JoJot.Models;
using JoJot.Services;
using JoJot.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace JoJot.Tests.Models;

/// <summary>
/// EF Core mapping tests — verifies entities round-trip correctly through the DbContext.
/// </summary>
[Collection("Database")]
public class NoteTabModelTests : IAsyncLifetime
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
    public async Task NoteTab_RoundTrips_ThroughDbContext()
    {
        var id = await NoteStore.InsertNoteAsync("rt-desk", "Round Trip", "Hello", true, 5);

        var notes = await NoteStore.GetNotesForDesktopAsync("rt-desk");
        var note = notes.Should().ContainSingle().Subject;

        note.Id.Should().Be(id);
        note.DesktopGuid.Should().Be("rt-desk");
        note.Name.Should().Be("Round Trip");
        note.Content.Should().Be("Hello");
        note.Pinned.Should().BeTrue();
        note.SortOrder.Should().Be(5);
        note.CreatedAt.Should().BeAfter(DateTime.MinValue);
        note.UpdatedAt.Should().BeAfter(DateTime.MinValue);
    }

    [Fact]
    public async Task NoteTab_DefaultValues_AppliedCorrectly()
    {
        var id = await NoteStore.InsertNoteAsync("def-desk", null, "", false, 0);

        var notes = await NoteStore.GetNotesForDesktopAsync("def-desk");
        var note = notes.Should().ContainSingle().Subject;

        note.Name.Should().BeNull();
        note.Content.Should().BeEmpty();
        note.Pinned.Should().BeFalse();
        note.SortOrder.Should().Be(0);
        note.EditorScrollOffset.Should().Be(0);
        note.CursorPosition.Should().Be(0);
    }

    [Fact]
    public async Task NoteTab_ComputedProperties_NotPersistedToDb()
    {
        // Verify that computed properties (DisplayLabel, IsPlaceholder, etc.)
        // don't interfere with EF Core mapping
        var id = await NoteStore.InsertNoteAsync("comp-desk", null, "", false, 0);

        var notes = await NoteStore.GetNotesForDesktopAsync("comp-desk");
        var note = notes.Should().ContainSingle().Subject;

        // These should work without EF Core errors (they're ignored in OnModelCreating)
        note.DisplayLabel.Should().Be("New note");
        note.IsPlaceholder.Should().BeTrue();
    }

    [Fact]
    public async Task Preference_RoundTrips()
    {
        await PreferenceStore.SetPreferenceAsync("model_test", "value_1");
        var value = await PreferenceStore.GetPreferenceAsync("model_test");
        value.Should().Be("value_1");
    }

    [Fact]
    public async Task AppState_RoundTrips()
    {
        await SessionStore.CreateSessionAsync("model-session", "Test Desktop", 2);

        var sessions = await SessionStore.GetAllSessionsAsync();
        sessions.Should().Contain(s =>
            s.DesktopGuid == "model-session" &&
            s.DesktopName == "Test Desktop" &&
            s.DesktopIndex == 2);
    }
}
