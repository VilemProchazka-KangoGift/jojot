using JoJot.Services;
using JoJot.ViewModels;

namespace JoJot.Models;

/// <summary>
/// In-memory representation of a note/tab entry.
/// Maps 1:1 to the notes table in SQLite.
/// Raises PropertyChanged for UI-affecting properties.
/// </summary>
public class NoteTab : ObservableObject
{
    /// <summary>
    /// Maximum number of characters shown in the display label before truncation.
    /// </summary>
    private const int DisplayLabelMaxLength = 45;

    /// <summary>
    /// Placeholder text shown when a note has no name and no content.
    /// </summary>
    private const string PlaceholderLabel = "New note";

    /// <summary>
    /// Threshold in minutes below which "Just now" is shown instead of a timestamp.
    /// </summary>
    private const double JustNowThresholdMinutes = 1.0;

    private static readonly string[] NameDependents = [nameof(DisplayLabel), nameof(IsPlaceholder)];
    private static readonly string[] ContentDependents = [nameof(DisplayLabel), nameof(IsPlaceholder)];
    private static readonly string[] UpdatedAtDependents = [nameof(UpdatedDisplay), nameof(UpdatedTooltipText)];

    private string? _name;
    private string _content = "";
    private bool _pinned;
    private DateTime _updatedAt;
    private int _sortOrder;

    /// <summary>
    /// Primary key (auto-incremented row ID).
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// GUID string identifying the virtual desktop this note belongs to.
    /// </summary>
    public string DesktopGuid { get; set; } = "";

    /// <summary>
    /// Optional custom name for the tab. When null, the display label falls back to content preview.
    /// </summary>
    public string? Name
    {
        get => _name;
        set => SetProperty(ref _name, value, NameDependents);
    }

    /// <summary>
    /// Full text content of the note.
    /// </summary>
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value, ContentDependents);
    }

    /// <summary>
    /// Whether this tab is pinned to the left side of the tab bar.
    /// </summary>
    public bool Pinned
    {
        get => _pinned;
        set => SetProperty(ref _pinned, value);
    }

    /// <summary>
    /// Timestamp when the note was first created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp of the most recent content or metadata change.
    /// </summary>
    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value, UpdatedAtDependents);
    }

    /// <summary>
    /// Position index used to persist drag-reorder within the tab bar.
    /// </summary>
    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    /// <summary>
    /// Saved vertical scroll offset of the text editor for this tab.
    /// </summary>
    public int EditorScrollOffset { get; set; }

    /// <summary>
    /// Saved caret position within the text editor for this tab.
    /// </summary>
    public int CursorPosition { get; set; }

    /// <summary>
    /// Three-tier label fallback: custom name, first ~45 chars of content, or "New note" placeholder.
    /// </summary>
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
                var cleaned = Content.Trim()
                    .Replace("\r\n", " ")
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');
                while (cleaned.Contains("  "))
                    cleaned = cleaned.Replace("  ", " ");

                if (cleaned.Length <= DisplayLabelMaxLength)
                {
                    return cleaned;
                }

                return cleaned[..DisplayLabelMaxLength];
            }

            return PlaceholderLabel;
        }
    }

    /// <summary>
    /// True when using the "New note" placeholder, which triggers muted italic styling in the UI.
    /// </summary>
    public bool IsPlaceholder => Name is null && string.IsNullOrWhiteSpace(Content);

    /// <summary>
    /// Relative date display for the created-at timestamp.
    /// </summary>
    public string CreatedDisplay => FormatRelativeDate(CreatedAt, null);

    /// <summary>
    /// Relative time display for the updated-at timestamp.
    /// </summary>
    public string UpdatedDisplay => FormatRelativeTime(UpdatedAt, null);

    /// <summary>
    /// Formats a <see cref="DateTime"/> as a relative date string for the created-at column.
    /// </summary>
    public static string FormatCreatedDisplay(DateTime dt, IClock? clock = null) => FormatRelativeDate(dt, clock);

    /// <summary>
    /// Formats a <see cref="DateTime"/> as a relative time string for the updated-at column.
    /// </summary>
    public static string FormatUpdatedDisplay(DateTime dt, IClock? clock = null) => FormatRelativeTime(dt, clock);

    /// <summary>
    /// Formats a timestamp as a relative date: time-only for today, "Yesterday",
    /// month-day for same year, or full date for older entries.
    /// </summary>
    internal static string FormatRelativeDate(DateTime dt, IClock? clock)
    {
        var now = (clock ?? SystemClock.Instance).Now;

        if (dt.Date == now.Date)
        {
            return dt.ToString("h:mm tt");
        }

        if (dt.Date == now.Date.AddDays(-1))
        {
            return "Yesterday";
        }

        if (dt.Year == now.Year)
        {
            return dt.ToString("MMM d");
        }

        return dt.ToString("MMM d, yyyy");
    }

    /// <summary>
    /// Formats a timestamp as a relative time string, always including hour:minute except for "Just now".
    /// </summary>
    internal static string FormatRelativeTime(DateTime dt, IClock? clock)
    {
        var now = (clock ?? SystemClock.Instance).Now;
        var diff = now - dt;

        if (diff.TotalMinutes < JustNowThresholdMinutes)
        {
            return "Just now";
        }

        if (dt.Date == now.Date)
        {
            return $"Today {dt:h:mm tt}";
        }

        if (dt.Date == now.Date.AddDays(-1))
        {
            return $"Yesterday {dt:h:mm tt}";
        }

        if (dt.Year == now.Year)
        {
            return dt.ToString("MMM d, h:mm tt");
        }

        return dt.ToString("MMM d, yyyy h:mm tt");
    }

    /// <summary>
    /// Tooltip text for the created-at date, bound in the tab DataTemplate.
    /// </summary>
    public string CreatedTooltipText => CreatedTooltip(CreatedAt);

    /// <summary>
    /// Tooltip text for the updated-at date, bound in the tab DataTemplate.
    /// Raises PropertyChanged when UpdatedAt changes (via UpdatedAtDependents).
    /// </summary>
    public string UpdatedTooltipText => UpdatedTooltip(UpdatedAt);

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
