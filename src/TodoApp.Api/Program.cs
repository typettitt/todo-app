using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

using FluentValidation;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

using Scalar.AspNetCore;

using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;
using TodoApp.Api.Data.Interceptors;
using TodoApp.Api.Features.Auth;
using TodoApp.Api.Features.Common;
using TodoApp.Api.Features.Todos;

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    var exitCode = 1;
    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(2),
    };

    try
    {
        using var response = await httpClient
            .GetAsync("http://127.0.0.1:5000/health/live")
            .ConfigureAwait(false);
        exitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
    }
    catch (TaskCanceledException)
    {
    }

    Environment.ExitCode = exitCode;
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.With<TraceIdLogEventEnricher>()
        .Destructure.ByTransforming<LoginRequest>(request => new
        {
            request.Email,
            Password = "[REDACTED]",
        })
        .Destructure.ByTransforming<RegisterRequest>(request => new
        {
            request.Email,
            Password = "[REDACTED]",
        });

    if (context.HostingEnvironment.IsDevelopment())
    {
        loggerConfiguration.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
    }
    else
    {
        loggerConfiguration.WriteTo.Console(new JsonFormatter(renderMessage: true));
    }
}, writeToProviders: true);

// --- Configuration -----------------------------------------------------------------------------
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"));

// --- Database ----------------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "ConnectionStrings:Default must be configured outside Development.");
    }

    connectionString = "Data Source=todoapp.db";
}
connectionString = NormalizeSqliteConnectionString(connectionString);

builder.Services.AddScoped<RowVersionInterceptor>();
builder.Services.AddSingleton<SqlitePragmaConnectionInterceptor>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddDbContext<TodoDbContext>((sp, opts) =>
{
    opts.UseSqlite(connectionString);
    opts.AddInterceptors(
        sp.GetRequiredService<RowVersionInterceptor>(),
        sp.GetRequiredService<SqlitePragmaConnectionInterceptor>());
});

// Non-request maintenance context — same schema, NO row-scoping filter, NO
// ICurrentUser dependency. Used for migrations, dev seeding, and the
// /health/ready DB probe. Sharing the RowVersionInterceptor preserves
// RowVersion stamping on seed writes.
builder.Services.AddDbContext<MaintenanceDbContext>((sp, opts) =>
{
    opts.UseSqlite(connectionString);
    opts.AddInterceptors(
        sp.GetRequiredService<RowVersionInterceptor>(),
        sp.GetRequiredService<SqlitePragmaConnectionInterceptor>());
});

// --- Auth services -----------------------------------------------------------------------------
builder.Services.AddSingleton<JwtKeyProvider>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthSessionService>();
builder.Services.AddSingleton<LoginEmailRateLimiter>();
builder.Services.AddSingleton<AuthCookies>();
// PBKDF2-SHA256 at 600,000 iterations meets NIST SP 800-63B Rev 4 (March 2024) and
// OWASP 2023+ guidance for memorized-secret verifiers. PasswordHasher<T> v3 supplies
// the FIPS-approved primitive (Rfc2898DeriveBytes / HMAC-SHA-256, 128-bit random salt);
// FedRAMP / FIPS 140-2/140-3 conformance requires the host to run on a FIPS-validated
// crypto module (Windows CNG, or Linux with FIPS-mode OpenSSL). Argon2id is NOT FIPS-
// approved, which is why we stay on PBKDF2 and turn the cost knob.
//
// Existing 310k-iteration hashes verify correctly (count is encoded in the v3 hash
// prefix) and are auto-upgraded to 600k via PasswordVerificationResult.SuccessRehashNeeded
// in AuthService.LoginAsync on the user's next successful sign-in. No forced reset.
builder.Services.Configure<PasswordHasherOptions>(opts =>
{
    opts.IterationCount = Math.Max(opts.IterationCount, 600_000);
});
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddMemoryCache();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddOpenApi("v1");
builder.Services.AddHealthChecks()
    .AddCheck<DbReadyHealthCheck>("db");

builder.Services.AddExceptionHandler<ConcurrencyExceptionHandler>();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<FallbackExceptionHandler>();

builder.Services.Configure<RouteHandlerOptions>(opts =>
{
    opts.ThrowOnBadRequest = false;
});

// --- Authentication ----------------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtKeyProvider, IOptions<JwtOptions>>((opts, keyProvider, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        opts.MapInboundClaims = false; // keep claim names as-issued
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyProvider.Key),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = "role",
        };
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var cookieName = ctx.HttpContext.RequestServices
                    .GetRequiredService<IOptions<JwtOptions>>().Value.CookieName;
                if (ctx.Request.Cookies.TryGetValue(cookieName, out var token)
                    && !string.IsNullOrEmpty(token))
                {
                    ctx.Token = token;
                }

                return Task.CompletedTask;
            },
            // JWT signature/lifetime are necessary but not sufficient: the
            // validator also requires a live, unrevoked AuthSession row keyed
            // by `sid`, and that row must belong to the token's `sub`.
            OnTokenValidated = async ctx =>
            {
                var sidClaim = ctx.Principal?.FindFirst("sid")?.Value;
                if (string.IsNullOrEmpty(sidClaim) || !Guid.TryParse(sidClaim, out var sid))
                {
                    ctx.Fail("session invalid");
                    return;
                }

                var subClaim = ctx.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(subClaim) || !Guid.TryParse(subClaim, out var sub))
                {
                    ctx.Fail("session invalid");
                    return;
                }

                var sessions = ctx.HttpContext.RequestServices
                    .GetRequiredService<AuthSessionService>();
                var session = await sessions.ValidateAsync(sid, ctx.HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                if (session is null || session.UserId != sub)
                {
                    ctx.Fail("session invalid");
                }
            },
            OnChallenge = ctx =>
            {
                // Suppress default WWW-Authenticate: Bearer header so a 401 does not
                // advertise the auth scheme to JS.
                ctx.HandleResponse();
                if (ctx.Response.HasStarted)
                {
                    return Task.CompletedTask;
                }

                ctx.Response.Headers.Remove("WWW-Authenticate");
                var pd = ProblemDetailsBuilder.Problem(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized.",
                    "Authentication is required.",
                    ctx.HttpContext);
                return ctx.Response.WriteProblemDetailsAsync(pd);
            },
        };
    });

builder.Services.AddAuthorization();

// --- Forwarded headers -------------------------------------------------------------------------
// The rate limiter partitions by Connection.RemoteIpAddress. Behind a reverse proxy that field
// is the proxy's bridge IP (one bucket for all internet traffic) unless we honor X-Forwarded-For
// from a trusted upstream. Read the trusted proxy list from `Trust:KnownProxies` (CSV of IPs
// and/or CIDR blocks). When unset/empty, defaults stand (loopback only) — Dev convenience.
// When set, the loopback defaults are CLEARED so production cannot accidentally trust 127.0.0.1.
builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opts.ForwardLimit = 1; // single proxy hop (nginx → API)
    opts.RequireHeaderSymmetry = false;

    var trusted = builder.Configuration["Trust:KnownProxies"];
    if (!string.IsNullOrWhiteSpace(trusted))
    {
        opts.KnownProxies.Clear();
        opts.KnownIPNetworks.Clear();

        foreach (var raw in trusted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var slash = raw.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                var prefix = raw[..slash];
                if (IPAddress.TryParse(prefix, out var net)
                    && int.TryParse(raw[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var prefixLength))
                {
                    opts.KnownIPNetworks.Add(new System.Net.IPNetwork(net, prefixLength));
                }
            }
            else if (IPAddress.TryParse(raw, out var ip))
            {
                opts.KnownProxies.Add(ip);
            }
        }
    }
});

// --- Rate limiting -----------------------------------------------------------------------------
var authRateLimitPermitLimit = Math.Max(
    1,
    builder.Configuration.GetValue<int>("RateLimits:Auth:PermitLimit", 10));
var authRateLimitWindowSeconds = Math.Max(
    1,
    builder.Configuration.GetValue<int>("RateLimits:Auth:WindowSeconds", 60));

builder.Services.AddRateLimiter(opts =>
{
    opts.AddPolicy(AuthEndpoints.AuthRateLimitPolicy, http =>
    {
        var key = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromSeconds(authRateLimitWindowSeconds),
            PermitLimit = authRateLimitPermitLimit,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });

    // Authenticated abuse controls partition by `sub` so one user cannot
    // saturate SQLite from one tab while leaving other users on the same proxy
    // unaffected. The IP fallback keeps the policy safe if it is ever applied
    // to a public endpoint.
    opts.AddPolicy(TodoEndpoints.WriteRateLimitPolicy, http =>
    {
        var sub = http.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var key = sub ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 60,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
    opts.AddPolicy(TodoEndpoints.ReadRateLimitPolicy, http =>
    {
        var sub = http.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var key = sub ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 600,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opts.OnRejected = async (ctx, cancellationToken) =>
    {
        var pd = ProblemDetailsBuilder.Problem(
            StatusCodes.Status429TooManyRequests,
            "Too many requests.",
            "Rate limit exceeded; please retry later.",
            ctx.HttpContext);
        await ctx.HttpContext.Response
            .WriteProblemDetailsAsync(pd, cancellationToken)
            .ConfigureAwait(false);
    };
});

builder.Services.AddProblemDetails(opts =>
{
    opts.CustomizeProblemDetails = context =>
    {
        ProblemDetailsExtensions.NormalizeFrameworkProblemDetails(
            context.ProblemDetails,
            context.HttpContext);
    };
});

// Serialize enums as their string names (Role -> "Basic", "Admin"). Numeric values
// on input are rejected (allowIntegerValues: false) — `?status=2` and {"priority": 2}
// must 400 because the wire contract is strings only. Framework binding errors
// are normalized through ProblemDetailsService.
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNameCaseInsensitive = false;
    opts.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter(
            namingPolicy: null,
            allowIntegerValues: false));
});

var app = builder.Build();

// Resolve key provider once at startup so we get the source log line + fail-fast in non-Dev.
{
    using var scope = app.Services.CreateScope();
    var keyProvider = scope.ServiceProvider.GetRequiredService<JwtKeyProvider>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("TodoApp.Api.Startup");
    startupLogger.JwtKeySource(keyProvider.Source);
}

// --- Migrate -----------------------------------------------------------------------------------
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
    var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger(typeof(DbInitializer));
    await DbInitializer.MigrateAsync(db, env, logger).ConfigureAwait(false);
    await AssertSqliteForeignKeysEnabledAsync(db, app.Lifetime.ApplicationStopping)
        .ConfigureAwait(false);
    if (env.IsDevelopment() && !app.Configuration.GetValue<bool>("Testing:DisableDemoSeed"))
    {
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        await DbInitializer.SeedAsync(db, env, clock, hasher).ConfigureAwait(false);
    }
}

// UseForwardedHeaders MUST run before the rate limiter and authentication so that
// HttpContext.Connection.RemoteIpAddress reflects the real client IP when the request
// arrives from a proxy listed in `Trust:KnownProxies`. The auth rate-limit policy
// reads RemoteIpAddress for partitioning. For direct (non-proxied) requests, or
// requests from untrusted sources, the field is left unchanged — spoofed
// X-Forwarded-For from random clients is ignored.
app.UseForwardedHeaders();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging(opts =>
{
    opts.GetLevel = static (httpContext, _, exception) =>
        exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : LogEventLevel.Information;
    opts.EnrichDiagnosticContext = static (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set(TraceIdLogEventEnricher.TraceIdPropertyName, ProblemDetailsBuilder.ResolveTraceId(httpContext));
        if (httpContext.Request.Headers.ContainsKey(HeaderNames.Authorization))
        {
            diagnosticContext.Set(HeaderNames.Authorization, "[REDACTED]");
        }
    };
});
app.UseAuthentication();
app.UseAuthorization();

// The rate limiter runs after authentication so the `todos-write` /
// `todos-read` policies can partition by the validated `sub` claim. The auth
// rate-limit policy on /login + /register partitions by remote IP, so it does
// not depend on the principal being populated.
app.UseRateLimiter();

app.UseMiddleware<SlidingRenewalMiddleware>();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase)
        || InternalHealthAuth.IsAllowed(context, app.Environment, app.Configuration))
    {
        await next(context).ConfigureAwait(false);
        return;
    }

    var problemDetails = ProblemDetailsBuilder.Problem(
        StatusCodes.Status401Unauthorized,
        "Unauthorized.",
        "Internal health-check authentication is required.",
        context);
    await context.Response
        .WriteProblemDetailsAsync(problemDetails, context.RequestAborted)
        .ConfigureAwait(false);
});

app.MapAuthEndpoints();
app.MapTodoEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
}
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static _ => false,
}).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference("/scalar/v1", opts =>
    {
        opts.WithOpenApiRoutePattern("/openapi/v1.json");
    }).AllowAnonymous();
}

if (app.Configuration.GetValue<bool>("Testing:EnableExceptionProbe"))
{
    app.MapGet("/__test/throw", static (HttpContext _) =>
        throw new InvalidOperationException("phase3-secret-exception-message"))
        .AllowAnonymous();
}

app.MapGet("/", () => Results.Ok(new { service = "todoapp", status = "ok" }));

static string NormalizeSqliteConnectionString(string rawConnectionString)
{
    var builder = new SqliteConnectionStringBuilder(rawConnectionString)
    {
        ForeignKeys = true,
    };

    return builder.ToString();
}

static async Task AssertSqliteForeignKeysEnabledAsync(
    DbContext db,
    CancellationToken cancellationToken)
{
    var connection = db.Database.GetDbConnection();
    var closeAfter = connection.State == System.Data.ConnectionState.Closed;
    if (closeAfter)
    {
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    try
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
            Convert.ToString(result, CultureInfo.InvariantCulture),
            "1",
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SQLite foreign key enforcement is not enabled.");
        }
    }
    finally
    {
        if (closeAfter)
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }
}

await app.RunAsync().ConfigureAwait(false);

// Expose Program to WebApplicationFactory<Program> in the test project.
public partial class Program;

internal static class InternalHealthAuth
{
    public const string HeaderName = "X-Internal-Auth";
    public const string ConfigKey = "Internal:HealthHeader";

    public static bool IsAllowed(
        HttpContext httpContext,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        if (environment.IsDevelopment())
        {
            return true;
        }

        var expected = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var supplied))
        {
            return false;
        }

        var actual = supplied.ToString();
        if (string.IsNullOrEmpty(actual))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
