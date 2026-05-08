using System.Linq.Expressions;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using TodoApp.Api.Data.Entities;

namespace TodoApp.Api.Data;

/// <summary>
/// Shared EF Core model configuration for <see cref="User"/> + <see cref="Todo"/>.
/// Two contexts call this helper:
/// <list type="bullet">
///   <item><see cref="TodoDbContext"/> — request-scoped; passes a row-scoping
///     query filter on <see cref="Todo"/> anchored on the current user.</item>
///   <item><see cref="MaintenanceDbContext"/> — non-request; passes
///     <see langword="null"/> so the maintenance context sees ALL rows. Used
///     for migrations, dev seeding, and the <c>/health/ready</c> DB check.</item>
/// </list>
/// Centralizing the schema here eliminates drift between the two contexts
/// without forcing inheritance.
/// </summary>
internal static class TodoModelConfiguration
{
    /// <summary>
    /// Tag-column JSON options are intentionally local to the converter — see
    /// plan.md: future tweaks to API-level JSON behavior must not change DB
    /// storage format.
    /// </summary>
    private static readonly JsonSerializerOptions TagsJson = new()
    {
        // Defaults are fine; explicit construction documents the boundary.
    };

    public static void Configure(ModelBuilder modelBuilder, Expression<Func<Todo, bool>>? todoQueryFilter)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var user = modelBuilder.Entity<User>();
        user.HasKey(u => u.Id);
        user.Property(u => u.Email).IsRequired().HasMaxLength(256);
        user.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);
        user.Property(u => u.Role).IsRequired();
        user.Property(u => u.CreatedAt).IsRequired();
        user.Property(u => u.UpdatedAt).IsRequired();

        // Lockout ladder state defaults to "fresh account" so existing rows
        // on migration are not stuck already failed. LockoutUntil is nullable;
        // null means no active lockout.
        user.Property(u => u.FailedLoginCount).IsRequired().HasDefaultValue(0);
        user.Property(u => u.LockoutLadderStep).IsRequired().HasDefaultValue(0);
        user.Property(u => u.LockoutUntil);
        user.Property(u => u.LockoutVersion)
            .IsRequired()
            .HasDefaultValue(0)
            .IsConcurrencyToken();

        // SQLite is ASCII case-insensitive by default for plain comparisons, but emails
        // can contain non-ASCII; the AuthService lowers + trims before reads/writes so the
        // unique index is enforced on the normalized form.
        user.HasIndex(u => u.Email).IsUnique();

        var todo = modelBuilder.Entity<Todo>();
        todo.HasKey(t => t.Id);
        todo.Property(t => t.Title).IsRequired().HasMaxLength(200);
        todo.Property(t => t.Description).HasMaxLength(2000);

        // DateOnly stored as ISO YYYY-MM-DD text. SQLite has no date type; ISO-8601
        // makes lexicographic order = chronological order, which keeps ORDER BY DueDate
        // honest and the DueToday equality predicate trivial.
        todo.Property(t => t.DueDate).HasConversion<string?>();

        // Priority stored as text — readable in raw queries; renaming an enum member
        // with numeric storage silently changes meaning.
        todo.Property(t => t.Priority).HasConversion<string>().IsRequired();

        todo.Property(t => t.IsCompleted).IsRequired();

        todo.Property(t => t.CompletedAt)
            .HasConversion(
                v => v.HasValue ? v.Value.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture) : null,
                v => v == null ? null : DateTimeOffset.ParseExact(v, "o", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal));

        // SQLite cannot ORDER BY a DateTimeOffset column directly. Store these
        // timestamps as ISO-8601 UTC text so lexicographic order = chronological
        // order, which keeps "sortBy=createdAt" honest.
        todo.Property(t => t.CreatedAt)
            .HasConversion(
                v => v.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                v => DateTimeOffset.ParseExact(v, "o", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal))
            .IsRequired();
        todo.Property(t => t.UpdatedAt)
            .HasConversion(
                v => v.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                v => DateTimeOffset.ParseExact(v, "o", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal))
            .IsRequired();

        // Tags: JSON-serialized text + ValueComparer for change tracking. Without the
        // comparer EF treats the array reference as opaque and misses element mutations
        // — the classic SQLite + array trap.
        todo.Property(t => t.Tags)
            .HasConversion(
                v => JsonSerializer.Serialize(v, TagsJson),
                s => JsonSerializer.Deserialize<string[]>(s, TagsJson) ?? Array.Empty<string>())
            .Metadata.SetValueComparer(new ValueComparer<string[]>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode(StringComparison.Ordinal))),
                v => v.ToArray()));

        todo.Property(t => t.RowVersion).IsConcurrencyToken();

        // Composite (UserId, DueDate) covers the DueToday hot path and provides the
        // leftmost UserId index used by the per-user list query.
        todo.HasIndex(t => new { t.UserId, t.DueDate });

        todo.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // AuthSession is the cookie invalidation surface. It is intentionally
        // NOT row-scoped by user: the JwtBearer validator and revocation paths
        // need to read sessions across all users. Row scoping is on Todo only.
        var session = modelBuilder.Entity<AuthSession>();
        session.ToTable("AuthSessions");
        session.HasKey(s => s.Sid);
        session.Property(s => s.Sid).ValueGeneratedNever();
        session.Property(s => s.UserId).IsRequired();
        session.Property(s => s.CreatedAt).IsRequired();
        session.Property(s => s.ExpiresAt).IsRequired();
        session.Property(s => s.AbsoluteExpiresAt).IsRequired();
        // RevokedAt nullable by default.

        // Composite covering the validator hot path: lookup by Sid PK is the
        // common case; this index supports RevokeAllForUserAsync and any future
        // "list active sessions" view.
        session.HasIndex(s => new { s.UserId, s.RevokedAt, s.ExpiresAt });

        session.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        if (todoQueryFilter is not null)
        {
            modelBuilder.Entity<Todo>().HasQueryFilter(todoQueryFilter);
        }
    }
}
