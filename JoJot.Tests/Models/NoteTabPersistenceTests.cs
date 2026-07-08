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
    public async Task Persisted_Completes_EvenWhenPersistWorkThrows()
    {
        // Mirrors CreateNewTab's background lambda shape: the persist work runs in a try and
        // CompletePersistence() is in a finally, so an exception from the "insert" still completes
        // Persisted (which the close-path deletion awaits under a sync block).
        var tab = new NoteTab();
        tab.BeginPersistence();

        var attempt = () =>
        {
            try
            {
                throw new InvalidOperationException("simulated insert failure");
            }
            finally
            {
                tab.CompletePersistence();
            }
        };

        attempt.Should().Throw<InvalidOperationException>("the exception must still propagate for logging/toast");
        tab.Persisted.IsCompleted.Should().BeTrue("finally must complete Persisted despite the failure");
        await tab.Persisted; // must not hang
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
