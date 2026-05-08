namespace TodoApp.Api.Features.Common;

/// <summary>
/// Source-generated <see cref="ILogger"/> messages used across the app. Centralized
/// because CA1848 (use <c>LoggerMessage</c> instead of extension methods) is enforced
/// repo-wide; declaring messages once keeps the call sites concise.
/// </summary>
internal static partial class LogMessages
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "JWT signing key source: {Source}")]
    public static partial void JwtKeySource(this ILogger logger, string source);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Applying database migrations (env: {Env})")]
    public static partial void ApplyingMigrations(this ILogger logger, string env);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Database migrations applied")]
    public static partial void MigrationsApplied(this ILogger logger);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Sliding renewal failed; continuing without cookie reissue.")]
    public static partial void SlidingRenewalFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Error, Message = "Unhandled exception. TraceId: {TraceId}")]
    public static partial void UnhandledException(this ILogger logger, Exception exception, string traceId);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Todo {Operation} by user {UserId}: todo {TodoId}")]
    public static partial void TodoMutation(this ILogger logger, string operation, Guid userId, Guid todoId);
}
