using Microsoft.EntityFrameworkCore;
using JoJot.Models;
using JoJot.Services;

namespace JoJot.Data;

/// <summary>
/// EF Core DbContext for the JoJot SQLite database.
/// Configured via Fluent API to match the existing snake_case schema.
/// </summary>
public class JoJotDbContext(string connectionString) : DbContext
{
    public DbSet<NoteTab> Notes { get; set; } = null!;
    public DbSet<AppState> AppStates { get; set; } = null!;
    public DbSet<Preference> Preferences { get; set; } = null!;
    public DbSet<PendingMove> PendingMoves { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(connectionString);
        optionsBuilder.AddInterceptors(new SqlitePragmaInterceptor());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // NoteTab -> notes
        modelBuilder.Entity<NoteTab>(e =>
        {
            e.ToTable("notes");
            e.HasKey(n => n.Id);

            e.Property(n => n.Id).HasColumnName("id");
            e.Property(n => n.DesktopGuid).HasColumnName("desktop_guid").IsRequired();
            e.Property(n => n.Name).HasColumnName("name");
            e.Property(n => n.Content).HasColumnName("content").IsRequired().HasDefaultValue("");
            e.Property(n => n.Pinned).HasColumnName("pinned").HasConversion<int>().HasDefaultValue(0);
            e.Property(n => n.CreatedAt).HasColumnName("created_at").IsRequired().HasDefaultValueSql("datetime('now')");
            e.Property(n => n.UpdatedAt).HasColumnName("updated_at").IsRequired().HasDefaultValueSql("datetime('now')");
            e.Property(n => n.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            e.Property(n => n.EditorScrollOffset).HasColumnName("editor_scroll_offset").HasDefaultValue(0);
            e.Property(n => n.CursorPosition).HasColumnName("cursor_position").HasDefaultValue(0);

            // Computed properties are not mapped to the database
            e.Ignore(n => n.DisplayLabel);
            e.Ignore(n => n.IsPlaceholder);
            e.Ignore(n => n.CreatedDisplay);
            e.Ignore(n => n.UpdatedDisplay);
        });

        // AppState -> app_state
        modelBuilder.Entity<AppState>(e =>
        {
            e.ToTable("app_state");
            e.HasKey(a => a.Id);

            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.DesktopGuid).HasColumnName("desktop_guid").IsRequired();
            e.Property(a => a.DesktopName).HasColumnName("desktop_name");
            e.Property(a => a.DesktopIndex).HasColumnName("desktop_index");
            e.Property(a => a.WindowLeft).HasColumnName("window_left");
            e.Property(a => a.WindowTop).HasColumnName("window_top");
            e.Property(a => a.WindowWidth).HasColumnName("window_width");
            e.Property(a => a.WindowHeight).HasColumnName("window_height");
            e.Property(a => a.ActiveTabId).HasColumnName("active_tab_id");
            e.Property(a => a.ScrollOffset).HasColumnName("scroll_offset");
            e.Property(a => a.WindowState).HasColumnName("window_state");

            e.HasIndex(a => a.DesktopGuid).IsUnique();
        });

        // Preference -> preferences
        modelBuilder.Entity<Preference>(e =>
        {
            e.ToTable("preferences");
            e.HasKey(p => p.Key);

            e.Property(p => p.Key).HasColumnName("key");
            e.Property(p => p.Value).HasColumnName("value").IsRequired();
        });

        // PendingMove -> pending_moves
        modelBuilder.Entity<PendingMove>(e =>
        {
            e.ToTable("pending_moves");
            e.HasKey(p => p.Id);

            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.WindowId).HasColumnName("window_id").IsRequired();
            e.Property(p => p.FromDesktop).HasColumnName("from_desktop").IsRequired();
            e.Property(p => p.ToDesktop).HasColumnName("to_desktop");
            e.Property(p => p.DetectedAt).HasColumnName("detected_at").IsRequired().HasDefaultValueSql("datetime('now')");
        });
    }
}
