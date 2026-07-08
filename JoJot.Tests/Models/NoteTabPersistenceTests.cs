using JoJot.Models;

namespace JoJot.Tests.Models;

/// <summary>Tests the optimistic-creation persistence gate on NoteTab.</summary>
public sealed class NoteTabPersistenceTests
{
    [Fact]
    public void Persisted_IsCompleted_ByDefault()
    {
        new NoteTab().Persisted.IsCompleted.Should().BeTrue(
            "tabs loaded from the database are persisted by definition");
    }

    [Fact]
    public void BeginPersistence_MakesPersistedPending_UntilCompleted()
    {
        var tab = new NoteTab();
        tab.BeginPersistence();
        tab.Persisted.IsCompleted.Should().BeFalse();

        tab.CompletePersistence();
        tab.Persisted.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void NextTempId_ReturnsUniqueNegativeIds()
    {
        long a = NoteTab.NextTempId();
        long b = NoteTab.NextTempId();
        a.Should().BeNegative();
        b.Should().BeNegative();
        a.Should().NotBe(b);
    }
}
