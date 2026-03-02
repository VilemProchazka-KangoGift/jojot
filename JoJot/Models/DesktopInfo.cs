namespace JoJot.Models
{
    /// <summary>
    /// Represents a virtual desktop with its identity and position.
    /// This is a clean POCO — no COM types, no interop references.
    /// Used as the public contract between VirtualDesktopService and business logic.
    /// </summary>
    public sealed record DesktopInfo(Guid Id, string Name, int Index);
}
