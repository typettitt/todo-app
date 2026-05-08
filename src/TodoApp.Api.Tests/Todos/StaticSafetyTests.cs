using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using TodoApp.Api.Data;

namespace TodoApp.Api.Tests.Todos;

[Trait("Category", "Ownership")]
public sealed class StaticSafetyTests
{
    private static readonly string[] BareResultBans =
    {
        "Results.NotFound()",
        "Results.Unauthorized()",
    };

    /// <summary>
    /// Belt-and-suspenders companion to the CI grep gate. Walks the production
    /// source tree and asserts no .cs file references <c>IgnoreQueryFilters</c>.
    /// The grep gate is the primary enforcement; this runs inside
    /// <c>dotnet test</c> so a regression surfaces in test output too.
    /// </summary>
    [Fact]
    public void Production_NeverBypasses_GlobalQueryFilter()
    {
        var apiSrc = LocateApiSourceRoot();
        Directory.Exists(apiSrc).Should().BeTrue("expected to find production source under {0}", apiSrc);

        var offenders = Directory.EnumerateFiles(apiSrc, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("IgnoreQueryFilters", StringComparison.Ordinal);
            })
            .ToList();

        offenders.Should().BeEmpty(
            "production code must never bypass the global query filter; offenders: {0}",
            string.Join(", ", offenders));
    }

    /// <summary>
    /// Migrations must run cleanly with no HTTP request in scope (dotnet ef
    /// migrate, container startup, health probe). These paths use
    /// <see cref="MaintenanceDbContext"/>, which has no <c>ICurrentUser</c>
    /// dependency and no global filter — so migrating an empty database from a
    /// non-HTTP context is a structural property, not a side effect of a
    /// fail-open disjunction.
    /// </summary>
    [Fact]
    public async Task Migrations_Apply_OnEmptyDatabase_WithoutHttpContext()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MaintenanceDbContext(options);

        var act = async () => await DbInitializer.MigrateAsync(db, new HostingEnv("Test"), NullLogger.Instance);
        await act.Should().NotThrowAsync();

        // Tables created.
        var tableNames = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        tableNames.Should().Contain("Users").And.Contain("Todos");
    }

    [Fact]
    public void BareResultGrepGate_EndpointErrorsUseProblemDetails()
    {
        var featuresRoot = Path.Combine(LocateApiSourceRoot(), "Features");
        Directory.Exists(featuresRoot).Should().BeTrue("expected to find production features under {0}", featuresRoot);

        var offenders = Directory.EnumerateFiles(featuresRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file);
                return BareResultBans
                    .Where(ban => text.Contains(ban, StringComparison.Ordinal))
                    .Select(ban => $"{Path.GetRelativePath(featuresRoot, file)} contains {ban}");
            })
            .ToList();

        offenders.Should().BeEmpty(
            "endpoint errors must flow through ProblemDetails helpers; offenders: {0}",
            string.Join(", ", offenders));
    }

    private static string LocateApiSourceRoot()
    {
        // Walk up from the test assembly directory to the repo root, then into src/TodoApp.Api.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoApp.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("expected to find TodoApp.slnx walking up from {0}", AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "src", "TodoApp.Api");
    }

    private sealed class HostingEnv(string envName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = envName;

        public string ApplicationName { get; set; } = "TodoApp.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
