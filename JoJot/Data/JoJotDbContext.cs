using Microsoft.EntityFrameworkCore;
using JoJot.Models;
using JoJot.Services;

namespace JoJot.Data;

/// <summary>
/// EF Core DbContext for the JoJot SQLite database.
/// Configured via Fluent API to match the existing snake_case schema.
/// Supports both direct construction (string) and pooled construction (options).
/// </summary>
public class JoJotDbContext : DbContext
{
    private readonly string? _connectionString;

    /// <summary>
    /// Creates a context using pre-built options (used by <see cref="PooledDbContextFactory{TContext}"/>).
    /// </summary>
    public JoJotDbContext(DbContextOptions<JoJotDbContext> options) : base(options) { }

    /// <summary>
    /// Creates a context from a connection string (fallback when pool is not initialized, e.g. tests).
    /// </summary>
    internal JoJotDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Notes table — one row per tab/note.
    /// </summary>
    public DbSet<NoteTab> Notes { get; set; } = null!;

    /// <summary>
    /// App state table — per-desktop session and window geometry.
    /// </summary>
    public DbSet<AppState> AppStates { get; set; } = null!;

    /// <summary>
    /// Preferences table — key/value application settings.
    /// </summary>
    public DbSet<Preference> Preferences { get; set; } = null!;

    /// <summary>
    /// Pending moves table — tracks in-flight window drags between desktops.
    /// </summary>
    public DbSet<PendingMove> PendingMoves { get; set; } = null!;

    /// <summary>
    /// Configures the SQLite provider and registers the WAL/pragma interceptor.
    /// </summary>
    /// <param name="optionsBuilder">The options builder provided by EF Core.</param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured && _connectionString is not null)
        {
            optionsBuilder.UseSqlite(_connectionString);
            optionsBuilder.AddInterceptors(new SqlitePragmaInterceptor());
        }
    }

    /// <summary>
    /// Maps entity types to their snake_case SQLite tables and columns via Fluent API.
    /// </summary>
    /// <param name="modelBuilder">The model builder provided by EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureNoteTab(modelBuilder);
        ConfigureAppState(modelBuilder);
        ConfigurePreference(modelBuilder);
        ConfigurePendingMove(modelBuilder);
    }

    /// <summary>
    /// Maps <see cref="NoteTab"/> to the <c>notes</c> table.
    /// </summary>
    private static void ConfigureNoteTab(ModelBuilder modelBuilder)
    {
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
            e.Property(n => n.FilePath).HasColumnName("file_path");

            // Computed properties are not mapped to the database
            e.Ignore(n => n.DisplayLabel);
            e.Ignore(n => n.IsPlaceholder);
            e.Ignore(n => n.IsFileBacked);
            e.Ignore(n => n.CreatedDisplay);
            e.Ignore(n => n.UpdatedDisplay);
        });
    }

    /// <summary>
    /// Maps <see cref="AppState"/> to the <c>app_state</c> table.
    /// </summary>
    private static void ConfigureAppState(ModelBuilder modelBuilder)
    {
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
    }

    /// <summary>
    /// Maps <see cref="Preference"/> to the <c>preferences</c> table.
    /// </summary>
    private static void ConfigurePreference(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Preference>(e =>
        {
            e.ToTable("preferences");
            e.HasKey(p => p.Key);

            e.Property(p => p.Key).HasColumnName("key");
            e.Property(p => p.Value).HasColumnName("value").IsRequired();
        });
    }

    /// <summary>
    /// Maps <see cref="PendingMove"/> to the <c>pending_moves</c> table.
    /// </summary>
    private static void ConfigurePendingMove(ModelBuilder modelBuilder)
    {
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
