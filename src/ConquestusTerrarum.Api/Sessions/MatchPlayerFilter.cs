using System.Net.Http.Headers;
using ConquestusTerrarum.Api.ErrorHandling;
using ConquestusTerrarum.Persistence;

namespace ConquestusTerrarum.Api.Sessions;

public sealed class MatchPlayer
{
    public Guid PlayerId { get; internal set; }
}

public sealed class MatchPlayerFilter(IMatchSessionRepository sessions, MatchPlayer player) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        var headers = http.Request.Headers.Authorization;
        if (headers.Count != 1 || !AuthenticationHeaderValue.TryParse(headers[0], out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            header.Parameter is not { } token || !PlayerCredentials.IsValidTokenFormat(token))
            throw new PlayerAuthenticationException();

        if (!Guid.TryParse(http.Request.RouteValues["matchId"]?.ToString(), out var matchId) || matchId == Guid.Empty)
            throw new InvalidApiRequestException("matchId must be a nonempty GUID.");

        player.PlayerId = await sessions.ResolvePlayerAsync(matchId, PlayerCredentials.HashToken(token),
            http.RequestAborted) ?? throw new PlayerAuthenticationException();
        return await next(context);
    }
}
