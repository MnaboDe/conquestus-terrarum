using ConquestusTerrarum.Persistence;
using Npgsql;

namespace ConquestusTerrarum.Api.ErrorHandling;

public sealed class InvalidApiRequestException(string message) : Exception(message);

public sealed class GameplayConflictException(string message, Exception innerException)
    : Exception(message, innerException);

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var (status, code, title, detail) = exception switch
            {
                MatchNotFoundException => (404, "match_not_found", "Match not found",
                    "The requested match does not exist."),
                InvalidApiRequestException ex => (400, "invalid_request", "Invalid request", ex.Message),
                BadHttpRequestException ex => (ex.StatusCode, "invalid_request", "Invalid request",
                    "Check route identifiers, content type and JSON request fields."),
                GameplayConflictException ex => (409, "gameplay_conflict", "Command conflicts with current state",
                    ex.Message),
                DatabaseTimeBehindWorldException => (503, "database_time_behind_world", "Database time unavailable",
                    "The database clock is behind the saved world checkpoint."),
                InvalidWorldStateException => (500, "invalid_world_state", "Stored world is invalid",
                    "The stored match cannot be loaded."),
                NpgsqlException { IsTransient: true } => (503, "database_unavailable", "Database unavailable",
                    "The database is temporarily unavailable."),
                _ => (500, "internal_error", "Internal server error", "The request could not be completed.")
            };
            if (status >= 500) logger.LogError(exception, "Request failed with code {Code}", code);
            context.Response.Clear();
            await Results.Problem(statusCode: status, title: title, detail: detail,
                    type: $"urn:conquestus-terrarum:problem:{code}",
                    extensions: new Dictionary<string, object?>
                        { ["code"] = code, ["traceId"] = context.TraceIdentifier })
                .ExecuteAsync(context);
        }
    }
}