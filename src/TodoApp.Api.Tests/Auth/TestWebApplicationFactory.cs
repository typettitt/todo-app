using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Interceptors;
using TodoApp.Api.Features.Auth;

namespace TodoApp.Api.Tests.Auth;

/// <summary>
/// Wires <see cref="WebApplicationFactory{TEntryPoint}"/> against a named shared
/// in-memory SQLite database. The open <see cref="SqliteConnection"/> below keeps
/// the database alive for the factory lifetime, while request-scoped EF contexts
/// use their own connections so concurrent TestServer requests do not share a
/// single connection object.
/// </summary>
public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    private readonly Action<JwtOptions>? _configureJwt;
    private readonly IReadOnlyDictionary<string, string?> _configuration;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly bool _enableDemoSeed;
    private readonly bool _enableExceptionProbe;
    private readonly string _environmentName;
    private readonly string? _priorSigningKey;
    private readonly string? _priorSigningKeyFile;
    private readonly string? _signingKey;

    public TestWebApplicationFactory(
        Action<JwtOptions>? configureJwt = null,
        string environmentName = "Development",
        string? signingKey = null,
        bool enableExceptionProbe = false,
        bool enableDemoSeed = false,
        IReadOnlyDictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"TodoAppTests-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        _configureJwt = configureJwt;
        _configureServices = configureServices;
        _enableDemoSeed = enableDemoSeed;
        _enableExceptionProbe = enableExceptionProbe;
        _environmentName = environmentName;
        _signingKey = signingKey;
        _configuration = configuration ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        if (signingKey is not null)
        {
            _priorSigningKey = Environment.GetEnvironmentVariable(JwtKeyProvider.EnvVarName);
            _priorSigningKeyFile = Environment.GetEnvironmentVariable(JwtKeyProvider.EnvFileVarName);
        }
    }

    public List<string> StartupLogLines { get; } = new();

    public List<CapturedLogRecord> LogRecords { get; } = new();

    public SqliteConnection Connection => _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(_environmentName);
        builder.UseSetting("ConnectionStrings:Default", _connection.ConnectionString);

        if (_signingKey is not null)
        {
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvVarName, _signingKey);
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvFileVarName, null);
        }

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new CapturingLoggerProvider(StartupLogLines, LogRecords));
            logging.SetMinimumLevel(LogLevel.Information);
        });

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connection.ConnectionString,
                ["Jwt:Issuer"] = "todoapp-tests",
                ["Jwt:Audience"] = "todoapp-tests",
                ["Testing:EnableExceptionProbe"] = _enableExceptionProbe ? "true" : "false",
                ["Testing:DisableDemoSeed"] = _enableDemoSeed ? "false" : "true",
            });
            config.AddInMemoryCollection(_configuration);
        });

        builder.ConfigureServices(services =>
        {
            // Point both contexts at the named in-memory database kept alive by
            // _connection, while letting each scope open its own connection.
            services.RemoveAll<DbContextOptions<TodoDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<TodoDbContext>>();
            services.AddDbContext<TodoDbContext>((sp, opts) =>
            {
                opts.UseSqlite(_connection.ConnectionString);
                opts.AddInterceptors(
                    sp.GetRequiredService<RowVersionInterceptor>(),
                    sp.GetRequiredService<SqlitePragmaConnectionInterceptor>());
            });

            services.RemoveAll<DbContextOptions<MaintenanceDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<MaintenanceDbContext>>();
            services.AddDbContext<MaintenanceDbContext>((sp, opts) =>
            {
                opts.UseSqlite(_connection.ConnectionString);
                opts.AddInterceptors(
                    sp.GetRequiredService<RowVersionInterceptor>(),
                    sp.GetRequiredService<SqlitePragmaConnectionInterceptor>());
            });

            if (_configureJwt is not null)
            {
                services.PostConfigure<JwtOptions>(_configureJwt);
            }

            _configureServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_signingKey is not null)
            {
                Environment.SetEnvironmentVariable(JwtKeyProvider.EnvVarName, _priorSigningKey);
                Environment.SetEnvironmentVariable(JwtKeyProvider.EnvFileVarName, _priorSigningKeyFile);
            }

            _connection.Dispose();
        }

        base.Dispose(disposing);
    }
}

public sealed record CapturedLogRecord(
    LogLevel Level,
    string Category,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, string> Properties);

internal sealed class CapturingLoggerProvider(
    List<string> lines,
    List<CapturedLogRecord> records) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(
        categoryName,
        lines,
        records,
        () => _scopeProvider);

    public void Dispose() { }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    private sealed class CapturingLogger(
        string category,
        List<string> lines,
        List<CapturedLogRecord> records,
        Func<IExternalScopeProvider> scopeProviderAccessor) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => scopeProviderAccessor().Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var message = formatter(state, exception);
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            AddProperties(properties, state);
            scopeProviderAccessor().ForEachScope(static (scope, bag) => AddProperties(bag, scope), properties);

            lock (lines)
            {
                lines.Add($"[{logLevel}] {category}: {message}");
                records.Add(new CapturedLogRecord(
                    logLevel,
                    category,
                    eventId,
                    message,
                    new Dictionary<string, string>(properties, StringComparer.Ordinal)));
            }
        }

        private static void AddProperties(Dictionary<string, string> properties, object? state)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    if (pair.Key == "{OriginalFormat}")
                    {
                        continue;
                    }

                    properties[pair.Key] = pair.Value?.ToString() ?? string.Empty;
                }

                return;
            }

            if (state is not null)
            {
                properties["State"] = state.ToString() ?? string.Empty;
            }
        }
    }
}
