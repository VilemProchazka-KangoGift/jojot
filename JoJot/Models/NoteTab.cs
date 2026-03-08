namespace JoJot.Models;

/// <summary>
/// In-memory representation of a note/tab entry.
/// Maps 1:1 to the notes table in SQLite.
/// </summary>
public class NoteTab
{
    public long Id { get; set; }
    public string DesktopGuid { get; set; } = "";
    public string? Name { get; set; }
    public string Content { get; set; } = "";
    public bool Pinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int SortOrder { get; set; }
    public int EditorScrollOffset { get; set; }
    public int CursorPosition { get; set; }

    /// <summary>
    /// Three-tier label fallback: custom name, first ~30 chars of content, or "New note" placeholder.
    /// </summary>
    public string DisplayLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return Name;

            if (!string.IsNullOrWhiteSpace(Content))
            {
                string trimmed = Content.Trim();
                if (trimmed.Length <= 30)
                    return trimmed;
                return trimmed[..30];
            }

            return "New note";
        }
    }

    /// <summary>
    /// True when using the "New note" placeholder, which triggers muted italic styling in the UI.
    /// </summary>
    public bool IsPlaceholder => Name == null && string.IsNullOrWhiteSpace(Content);

    /// <summary>
    /// Relative date display for the created-at timestamp.
    /// </summary>
    public string CreatedDisplay => FormatRelativeDate(CreatedAt);

    /// <summary>
    /// Relative time display for the updated-at timestamp.
    /// </summary>
    public string UpdatedDisplay => FormatRelativeTime(UpdatedAt);

    /// <summary>
    /// Signals the UI to re-read display properties after Name or Content changes.
    /// </summary>
    public void RefreshDisplayProperties()
    {
        // No-op: code-behind pattern uses explicit property reads, not binding notifications.
    }

    /// <summary>
    /// Formats a DateTime as a relative date string for the created-at column.
    /// </summary>
    public static string FormatCreatedDisplay(DateTime dt) => FormatRelativeDate(dt);

    /// <summary>
    /// Formats a DateTime as a relative time string for the updated-at column.
    /// </summary>
    public static string FormatUpdatedDisplay(DateTime dt) => FormatRelativeTime(dt);

    private static string FormatRelativeDate(DateTime dt)
    {
        var now = DateTime.Now;

        if (dt.Date == now.Date)
            return dt.ToString("h:mm tt");

        if (dt.Date == now.Date.AddDays(-1))
            return "Yesterday";

        if (dt.Year == now.Year)
            return dt.ToString("MMM d");

        return dt.ToString("MMM d, yyyy");
    }

    /// <summary>
    /// Formats a DateTime as a relative time string, always including hour:minute except for "Just now".
    /// </summary>
    private static string FormatRelativeTime(DateTime dt)
    {
        var diff = DateTime.Now - dt;

        if (diff.TotalMinutes < 1)
            return "Just now";

        if (dt.Date == DateTime.Now.Date)
            return $"Today {dt:h:mm tt}";

        if (dt.Date == DateTime.Now.Date.AddDays(-1))
            return $"Yesterday {dt:h:mm tt}";

        if (dt.Year == DateTime.Now.Year)
            return dt.ToString("MMM d, h:mm tt");

        return dt.ToString("MMM d, yyyy h:mm tt");
    }

    /// <summary>
    /// Tooltip string showing the exact created-at date and time.
    /// </summary>
    public static string CreatedTooltip(DateTime dt) =>
        $"Created: {dt:MMM d, yyyy h:mm tt}";

    /// <summary>
    /// Tooltip string showing the exact updated-at date and time.
    /// </summary>
    public static string UpdatedTooltip(DateTime dt) =>
        $"Last updated: {dt:MMM d, yyyy h:mm tt}";
}
