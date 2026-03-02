namespace JoJot.Models
{
    /// <summary>
    /// In-memory representation of a note/tab entry.
    /// Maps 1:1 to the notes table in SQLite.
    /// Phase 4: TABS-02, TABS-03 — display label fallback and date formatting.
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
        /// TABS-02: 3-tier label fallback.
        /// 1. Custom name (if set)
        /// 2. First ~30 chars of content (if content is non-empty)
        /// 3. "New note" placeholder
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
        /// True when using "New note" placeholder — triggers muted italic styling in UI.
        /// </summary>
        public bool IsPlaceholder => Name == null && string.IsNullOrWhiteSpace(Content);

        /// <summary>
        /// Relative date display for the created_at field.
        /// Format: "h:mm tt" (today), "Yesterday", "MMM d" (same year), "MMM d, yyyy" (other year).
        /// </summary>
        public string CreatedDisplay => FormatRelativeDate(CreatedAt);

        /// <summary>
        /// Relative time display for the updated_at field.
        /// Format: "Just now", "N min ago", "Today h:mm tt", "Yesterday", "MMM d", "MMM d, yyyy".
        /// </summary>
        public string UpdatedDisplay => FormatRelativeTime(UpdatedAt);

        /// <summary>
        /// Placeholder for UI refresh signaling. The code-behind calls this after
        /// modifying Name or Content to know it should re-read display properties.
        /// No-op — the UI explicitly re-reads DisplayLabel etc. when needed.
        /// </summary>
        public void RefreshDisplayProperties()
        {
            // No-op: code-behind pattern uses explicit property reads, not binding notifications.
        }

        /// <summary>
        /// Formats a DateTime as a relative date string for the created_at column.
        /// </summary>
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
        /// Formats a DateTime as a relative time string for the updated_at column.
        /// More granular than FormatRelativeDate — includes "Just now" and "N min ago" tiers.
        /// </summary>
        private static string FormatRelativeTime(DateTime dt)
        {
            var diff = DateTime.Now - dt;

            if (diff.TotalMinutes < 1)
                return "Just now";

            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes} min ago";

            if (dt.Date == DateTime.Now.Date)
                return $"Today {dt:h:mm tt}";

            if (dt.Date == DateTime.Now.Date.AddDays(-1))
                return "Yesterday";

            if (dt.Year == DateTime.Now.Year)
                return dt.ToString("MMM d");

            return dt.ToString("MMM d, yyyy");
        }
    }
}
