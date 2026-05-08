using System.Globalization;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Features.Todos;

public static class TodoEndpoints
{
    public const string GetTodoRouteName = "GetTodoById";

    // Authenticated abuse controls. `todos-write` caps mutations at
    // 60/min/sub so one user cannot saturate SQLite write throughput;
    // `todos-read` caps reads at 600/min/sub which still allows a tight
    // interactive loop but stops a runaway scanner. Both partition by the
    // `sub` JWT claim so noisy clients do not punish other tenants on the
    // same egress IP.
    public const string WriteRateLimitPolicy = "todos-write";
    public const string ReadRateLimitPolicy = "todos-read";

    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    internal const int MaxPage = 10000;
    internal const int MaxSearchLength = 100;

    public static IEndpointRouteBuilder MapTodoEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/todos").RequireAuthorization();

        group.MapGet("/", ListAsync)
            .RequireRateLimiting(ReadRateLimitPolicy)
            .Produces<PagedResult<TodoResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName(GetTodoRouteName)
            .RequireRateLimiting(ReadRateLimitPolicy)
            .Produces<TodoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationFilter<CreateTodoRequest>>()
            .RequireRateLimiting(WriteRateLimitPolicy)
            .Produces<TodoResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPut("/{id:guid}", UpdateAsync)
            .AddEndpointFilter<ValidationFilter<UpdateTodoRequest>>()
            .RequireRateLimiting(WriteRateLimitPolicy)
            .Produces<TodoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPatch("/{id:guid}/complete", CompleteAsync)
            .AddEndpointFilter<ValidationFilter<CompleteTodoRequest>>()
            .RequireRateLimiting(WriteRateLimitPolicy)
            .Produces<TodoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireRateLimiting(WriteRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        TodoDbContext db,
        HttpContext http,
        CancellationToken cancellationToken,
        [FromQuery] string? status = null,
        [FromQuery] string? q = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? today = null,
        [FromQuery] string? dueFrom = null,
        [FromQuery] string? dueTo = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(http);

        // Strict string-only enum parsing — numeric query values like ?status=2 are
        // rejected even though the enum is integer-backed (the wire contract is
        // "named values only").
        if (!TryParseEnum<TodoStatusFilter>(status, out var effectiveStatus, TodoStatusFilter.All))
        {
            return BadRequestField(http, "status", $"Unknown status '{status}'.");
        }

        if (!TryParseEnum<SortBy>(sortBy, out var effectiveSortBy, SortBy.CreatedAt))
        {
            return BadRequestField(http, "sortBy", $"Unknown sortBy '{sortBy}'.");
        }

        if (!TryParseEnum<SortDir>(sortDir, out var effectiveSortDir, SortDir.Desc))
        {
            return BadRequestField(http, "sortDir", $"Unknown sortDir '{sortDir}'.");
        }

        // Hard cap page numbers so a request cannot force a deep OFFSET scan.
        // Larger paginations need cursor-based paging.
        if (page is > MaxPage)
        {
            return BadRequestField(http, "page", $"page must be {MaxPage} or less.");
        }

        // Bound the search term so a large `q` cannot turn one GET into a
        // multi-second scan. The 100-char ceiling is generous for human input.
        if (q is not null && q.Length > MaxSearchLength)
        {
            return BadRequestField(http, "q", $"q must be {MaxSearchLength} characters or less.");
        }

        var effectivePage = page is > 0 ? page.Value : 1;
        var effectivePageSize = (pageSize is > 0 ? pageSize.Value : DefaultPageSize);
        if (effectivePageSize > MaxPageSize)
        {
            effectivePageSize = MaxPageSize; // documented clamp, not a 400
        }

        var todayParse = TryParseQueryDate(today, "today", http);
        if (todayParse.Error is not null)
        {
            return todayParse.Error;
        }

        var dueFromParse = TryParseQueryDate(dueFrom, "dueFrom", http);
        if (dueFromParse.Error is not null)
        {
            return dueFromParse.Error;
        }

        var dueToParse = TryParseQueryDate(dueTo, "dueTo", http);
        if (dueToParse.Error is not null)
        {
            return dueToParse.Error;
        }

        var todayDate = todayParse.Value;
        var dueFromDate = dueFromParse.Value;
        var dueToDate = dueToParse.Value;

        if (dueFromDate.HasValue && dueToDate.HasValue && dueFromDate.Value > dueToDate.Value)
        {
            return BadRequestField(http, "dueTo", "dueTo must be on or after dueFrom.");
        }

        if (effectiveStatus == TodoStatusFilter.DueToday && todayDate is null)
        {
            return BadRequestField(
                http,
                "today",
                "DueToday filter requires the 'today' query parameter (YYYY-MM-DD).");
        }

        var query = db.Todos
            .AsNoTracking()
            .WithStatus(effectiveStatus, todayDate)
            .WithSearch(q)
            .WithDueWindow(dueFromDate, dueToDate);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .WithSort(effectiveSortBy, effectiveSortDir)
            .WithPaging(effectivePage, effectivePageSize)
            .Select(t => new TodoResponse(
                t.Id,
                t.Title,
                t.Description,
                t.DueDate,
                t.Priority,
                t.IsCompleted,
                t.CompletedAt,
                t.Tags,
                t.RowVersion,
                t.CreatedAt,
                t.UpdatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasNext = effectivePage * effectivePageSize < total;

        return Results.Ok(new PagedResult<TodoResponse>(
            items,
            effectivePage,
            effectivePageSize,
            total,
            hasNext));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        TodoDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(http);

        var todo = await db.Todos
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return todo is null ? ProblemDetailsExtensions.NotFoundProblem(http) : Results.Ok(ToResponse(todo));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateTodoRequest request,
        TodoDbContext db,
        ICurrentUser currentUser,
        ILogger<TodoMutationAudit> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(logger);

        var todo = new Todo
        {
            Id = Guid.NewGuid(),
            UserId = currentUser.Id,
            Title = request.Title,
            Description = request.Description,
            DueDate = request.DueDate,
            Priority = request.Priority ?? Priority.Low,
            IsCompleted = false,
            Tags = request.Tags ?? Array.Empty<string>(),
            RowVersion = 0, // interceptor stamps to 1 on Add
        };

        db.Todos.Add(todo);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.TodoMutation("Create", currentUser.Id, todo.Id);

        var response = ToResponse(todo);
        return Results.Created($"/api/todos/{todo.Id}", response);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateTodoRequest request,
        TodoDbContext db,
        ICurrentUser currentUser,
        HttpContext http,
        ILogger<TodoMutationAudit> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);

        // Filtered lookup — cross-user rows return null → 404. Do NOT fall back to an
        // unfiltered lookup; the global filter is the single source of truth.
        var todo = await db.Todos
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (todo is null)
        {
            return ProblemDetailsExtensions.NotFoundProblem(http);
        }

        if (todo.RowVersion != request.RowVersion!.Value)
        {
            return Conflict(http);
        }

        todo.Title = request.Title;
        todo.Description = request.Description;
        todo.DueDate = request.DueDate;
        todo.Priority = request.Priority!.Value;
        todo.Tags = request.Tags ?? Array.Empty<string>();

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.TodoMutation("Update", currentUser.Id, todo.Id);

        return Results.Ok(ToResponse(todo));
    }

    private static async Task<IResult> CompleteAsync(
        Guid id,
        [FromBody] CompleteTodoRequest request,
        TodoDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        HttpContext http,
        ILogger<TodoMutationAudit> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);

        var todo = await db.Todos
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (todo is null)
        {
            return ProblemDetailsExtensions.NotFoundProblem(http);
        }

        if (todo.RowVersion != request.RowVersion!.Value)
        {
            return Conflict(http);
        }

        if (request.IsCompleted!.Value)
        {
            todo.CompletedAt ??= clock.Now;
        }
        else
        {
            todo.CompletedAt = null;
        }

        todo.IsCompleted = request.IsCompleted.Value;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.TodoMutation("Complete", currentUser.Id, todo.Id);

        return Results.Ok(ToResponse(todo));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        TodoDbContext db,
        ICurrentUser currentUser,
        HttpContext http,
        ILogger<TodoMutationAudit> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);

        var todo = await db.Todos
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (todo is null)
        {
            return ProblemDetailsExtensions.NotFoundProblem(http);
        }

        db.Todos.Remove(todo);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.TodoMutation("Delete", currentUser.Id, todo.Id);
        return Results.NoContent();
    }

    private static TodoResponse ToResponse(Todo t) => new(
        t.Id,
        t.Title,
        t.Description,
        t.DueDate,
        t.Priority,
        t.IsCompleted,
        t.CompletedAt,
        t.Tags,
        t.RowVersion,
        t.CreatedAt,
        t.UpdatedAt);

    private static bool TryParseEnum<TEnum>(string? raw, out TEnum value, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = fallback;
            return true;
        }

        // Strict: reject numeric values (e.g., "2") and unknown names. The contract
        // is named-values only, so a raw integer is treated as malformed input.
        if (raw.AsSpan().Length > 0 && (char.IsDigit(raw[0]) || raw[0] == '-' || raw[0] == '+'))
        {
            value = fallback;
            return false;
        }

        if (Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value))
        {
            return true;
        }

        value = fallback;
        return false;
    }

    private static IResult BadRequestField(HttpContext http, string field, string message)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = new[] { message },
        };
        return ProblemDetailsExtensions.ValidationProblemDetails(http, errors);
    }

    private static (DateOnly? Value, IResult? Error) TryParseQueryDate(
        string? raw,
        string field,
        HttpContext http)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return (null, null);
        }

        // We require strict YYYY-MM-DD; reject ISO datetimes, slashes, etc. so a
        // mistaken `new Date().toISOString()` from the FE fails loudly here.
        return DateOnly.TryParseExact(
            raw,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? (parsed, null)
            : (null, BadRequestField(http, field, $"{field} must be in YYYY-MM-DD format."));
    }

    private static IResult Conflict(HttpContext http) =>
        ProblemDetailsExtensions.ConflictProblem(http);
}

internal sealed class TodoMutationAudit;
