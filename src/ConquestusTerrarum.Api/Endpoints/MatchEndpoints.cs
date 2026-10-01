using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Api.Development;
using ConquestusTerrarum.Api.ErrorHandling;
using ConquestusTerrarum.Api.Mapping;
using ConquestusTerrarum.Domain.World;
using ConquestusTerrarum.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ConquestusTerrarum.Api.Endpoints;

public static class MatchEndpoints
{
    public static void MapMatchEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Matches");
        api.AddEndpointFilter(async (context, next) =>
        {
            // No query parameters belong to this MVP contract (including command timestamps).
            if (context.HttpContext.Request.Query.Count != 0)
                throw new InvalidApiRequestException("Query parameters are not supported for this endpoint.");
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        Describe(api.MapGet("/matches/{matchId}", Get).WithName("GetMatch"));
        Describe(api.MapPost("/matches/{matchId}/armies/{armyId}/move", Move).WithName("MoveArmy"));
        Describe(api.MapPost("/matches/{matchId}/armies/{armyId}/retreat", Retreat).WithName("RetreatArmy"));
        if (app.Environment.IsDevelopment())
            Describe(api.MapPost("/dev/matches", CreateDevelopmentMatch).WithName("CreateDevelopmentMatch"));
    }

    private static void Describe(RouteHandlerBuilder route) => route
        .ProducesProblem(400).ProducesProblem(404).ProducesProblem(409)
        .ProducesProblem(500).ProducesProblem(503);

    private static async Task<Ok<MatchStateResponse>> Get(Guid matchId, IMatchRepository repository, CancellationToken cancellationToken)
    {
        ValidateId(matchId, "matchId");
        return TypedResults.Ok(await Execute(matchId, repository, null, cancellationToken));
    }

    private static async Task<Ok<MatchStateResponse>> Move(Guid matchId, Guid armyId, MoveArmyRequest request,
        IMatchRepository repository, CancellationToken cancellationToken)
    {
        ValidateId(matchId, "matchId");
        ValidateId(armyId, "armyId");
        ValidateId(request.DestinationLocationId, "destinationLocationId");
        return TypedResults.Ok(await Execute(matchId, repository,
            (world, time) => world.StartMovement(armyId, request.DestinationLocationId, time), cancellationToken));
    }

    private static async Task<Ok<MatchStateResponse>> Retreat(Guid matchId, Guid armyId, HttpRequest request,
        IMatchRepository repository, CancellationToken cancellationToken)
    {
        ValidateId(matchId, "matchId");
        ValidateId(armyId, "armyId");
        RequireNoBody(request);
        return TypedResults.Ok(await Execute(matchId, repository,
            (world, time) => world.Retreat(armyId, time), cancellationToken));
    }

    private static async Task<MatchStateResponse> Execute(Guid matchId, IMatchRepository repository,
        Action<WorldState, DateTimeOffset>? command, CancellationToken cancellationToken)
    {
        var result = await repository.ExecuteAsync(matchId, (world, databaseTime) =>
        {
            // Only domain-command failures become 409; unrelated InvalidOperationExceptions remain 500.
            try { command?.Invoke(world, databaseTime); }
            catch (InvalidOperationException ex) { throw new GameplayConflictException(ex.Message, ex); }
            return Task.FromResult(MatchStateMapper.Map(matchId, world, databaseTime, version: 0));
        }, cancellationToken);
        // The callback's detached DTO describes the same state that was saved by this transaction.
        return result.Value with { Version = result.Version, ServerTime = result.DatabaseTime };
    }

    private static async Task<Created<MatchStateResponse>> CreateDevelopmentMatch(HttpRequest request,
        IMatchRepository repository, CancellationToken cancellationToken)
    {
        RequireNoBody(request);
        var matchId = Guid.NewGuid();
        MatchStateResponse? response = null;
        await repository.CreateAsync(matchId, databaseTime =>
        {
            var world = DemoWorldFactory.Create(databaseTime);
            response = MatchStateMapper.Map(matchId, world, databaseTime, version: 0);
            return world;
        }, cancellationToken);
        return TypedResults.Created($"/api/v1/matches/{matchId}", response!);
    }

    private static void ValidateId(Guid id, string field)
    {
        if (id == Guid.Empty) throw new InvalidApiRequestException($"{field} cannot be empty.");
    }

    private static void RequireNoBody(HttpRequest request)
    {
        if (request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
            throw new InvalidApiRequestException("This endpoint does not accept a request body.");
    }
}
