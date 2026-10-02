using System.Globalization;
using System.Text.Json;
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
    public static void MapMatchEndpoints(
        this WebApplication app)
    {
        // Existing MVP endpoints keep their strict "no query parameters" rule.
        var api =
            app.MapGroup("/api/v1")
                .WithTags("Matches");

        api.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.Request.Query.Count != 0)
                throw new InvalidApiRequestException(
                    "Query parameters are not supported for this endpoint.");

            context.HttpContext.Response.Headers.CacheControl =
                "no-store";

            return await next(context);
        });

        Describe(
            api.MapGet(
                    "/matches/{matchId}",
                    Get)
                .WithName("GetMatch"));

        Describe(
            api.MapPost(
                    "/matches/{matchId}/armies/{armyId}/move",
                    Move)
                .WithName("MoveArmy"));

        Describe(
            api.MapPost(
                    "/matches/{matchId}/armies/{armyId}/retreat",
                    Retreat)
                .WithName("RetreatArmy"));

        if (app.Environment.IsDevelopment())
            Describe(
                api.MapPost(
                        "/dev/matches",
                        CreateDevelopmentMatch)
                    .WithName("CreateDevelopmentMatch"));

        // Change feed intentionally accepts:
        // ?after=<cursor>&limit=<page size>
        //
        // Therefore it must not inherit the old "no query" filter.
        Describe(
            app.MapGet(
                    "/api/v1/matches/{matchId}/updates",
                    GetUpdates)
                .WithTags("Matches")
                .WithName("GetMatchUpdates"));
        Describe(
            api.MapGet(
                    "/matches/{matchId}/armies/{armyId}/details",
                    GetArmyDetails)
                .WithName("GetArmyDetails"));
    }

    private static void Describe(
        RouteHandlerBuilder route)
    {
        route
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(500)
            .ProducesProblem(503);
    }

    private static async Task<Ok<MatchStateResponse>> Get(
        Guid matchId,
        IMatchRepository repository,
        CancellationToken cancellationToken)
    {
        ValidateId(
            matchId,
            "matchId");

        return TypedResults.Ok(
            await Execute(
                matchId,
                repository,
                null,
                cancellationToken));
    }

    private static async Task<Ok<MatchUpdatesResponse>>
        GetUpdates(
            Guid matchId,
            HttpRequest request,
            IMatchRepository repository,
            CancellationToken cancellationToken)
    {
        ValidateId(
            matchId,
            "matchId");

        ValidateUpdateQueryKeys(
            request);

        var after =
            ReadLongQuery(
                request,
                "after",
                0,
                0);

        var limit =
            ReadIntQuery(
                request,
                "limit",
                100,
                1,
                500);

        request.HttpContext.Response.Headers.CacheControl =
            "no-store";

        var page =
            await repository.ReadUpdatesAsync(
                matchId,
                after,
                limit,
                cancellationToken);

        var updates =
            page.Updates
                .Select(MapUpdate)
                .ToArray();

        return TypedResults.Ok(
            new MatchUpdatesResponse(
                page.NextCursor,
                page.HasMore,
                updates));
    }

    private static async Task<Ok<ArmyDetailsResponse>> GetArmyDetails(
        Guid matchId,
        Guid armyId,
        IMatchRepository repository,
        CancellationToken cancellationToken)
    {
        ValidateId(
            matchId,
            "matchId");

        ValidateId(
            armyId,
            "armyId");

        var result =
            await repository.QueryCurrentAsync(
                matchId,
                (snapshot, databaseTime) =>
                {
                    var army =
                        snapshot.Armies.Single(candidate =>
                            candidate.Id == armyId);

                    var isDestroyed =
                        !army.Units.Any(unit =>
                            unit.CurrentHitPoints > 0m);

                    var canRetreat =
                        !isDestroyed &&
                        army.IsInBattle &&
                        army.Movement is null &&
                        army.RetreatLocationId is not null;

                    return new ArmyDetailsResponse(
                        databaseTime,
                        army.Id,
                        isDestroyed,
                        army.IsInBattle,
                        canRetreat,
                        army.RetreatLocationId,
                        army.Units
                            .OrderBy(unit => unit.Id)
                            .Select(unit =>
                                new UnitResponse(
                                    unit.Id,
                                    unit.MaxHitPoints,
                                    unit.CurrentHitPoints,
                                    unit.DamagePerSecond,
                                    unit.CurrentHitPoints > 0m,
                                    unit.CurrentHitPoints > 0m &&
                                    unit.CurrentHitPoints <
                                    unit.MaxHitPoints))
                            .ToArray());
                },
                cancellationToken);

        return TypedResults.Ok(
            result.Value with
            {
                ServerTime = result.DatabaseTime
            });
    }

    private static MatchUpdateResponse MapUpdate(
        MatchUpdateEntry update)
    {
        using var document =
            JsonDocument.Parse(
                update.PayloadJson);

        return new MatchUpdateResponse(
            update.Sequence,
            update.OccurredAt,
            update.Type,
            document.RootElement.Clone());
    }

    private static async Task<Ok<MatchStateResponse>> Move(
        Guid matchId,
        Guid armyId,
        MoveArmyRequest request,
        IMatchRepository repository,
        CancellationToken cancellationToken)
    {
        ValidateId(
            matchId,
            "matchId");

        ValidateId(
            armyId,
            "armyId");

        ValidateId(
            request.DestinationLocationId,
            "destinationLocationId");

        return TypedResults.Ok(
            await Execute(
                matchId,
                repository,
                (world, time) =>
                    world.StartMovement(
                        armyId,
                        request.DestinationLocationId,
                        time),
                cancellationToken));
    }

    private static async Task<Ok<MatchStateResponse>> Retreat(
        Guid matchId,
        Guid armyId,
        HttpRequest request,
        IMatchRepository repository,
        CancellationToken cancellationToken)
    {
        ValidateId(
            matchId,
            "matchId");

        ValidateId(
            armyId,
            "armyId");

        RequireNoBody(
            request);

        return TypedResults.Ok(
            await Execute(
                matchId,
                repository,
                (world, time) =>
                    world.Retreat(
                        armyId,
                        time),
                cancellationToken));
    }

    private static async Task<MatchStateResponse> Execute(
        Guid matchId,
        IMatchRepository repository,
        Action<WorldState, DateTimeOffset>? command,
        CancellationToken cancellationToken)
    {
        var result =
            await repository.ExecuteAsync(
                matchId,
                (world, databaseTime) =>
                {
                    try
                    {
                        command?.Invoke(
                            world,
                            databaseTime);
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new GameplayConflictException(
                            ex.Message,
                            ex);
                    }

                    return Task.FromResult(
                        MatchStateMapper.Map(
                            matchId,
                            world,
                            databaseTime,
                            0));
                },
                cancellationToken);

        return result.Value with
        {
            Version = result.Version,
            UpdateCursor = result.UpdateCursor,
            ServerTime = result.DatabaseTime
        };
    }

    private static async Task<Created<MatchStateResponse>>
        CreateDevelopmentMatch(
            HttpRequest request,
            IMatchRepository repository,
            CancellationToken cancellationToken)
    {
        RequireNoBody(
            request);

        var matchId =
            Guid.NewGuid();

        MatchStateResponse? response =
            null;

        await repository.CreateAsync(
            matchId,
            databaseTime =>
            {
                var world =
                    DemoWorldFactory.Create(
                        databaseTime);

                response =
                    MatchStateMapper.Map(
                        matchId,
                        world,
                        databaseTime,
                        0);

                return world;
            },
            cancellationToken);

        return TypedResults.Created(
            $"/api/v1/matches/{matchId}",
            response!);
    }

    private static void ValidateUpdateQueryKeys(
        HttpRequest request)
    {
        foreach (var key in request.Query.Keys)
        {
            if (string.Equals(
                    key,
                    "after",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    key,
                    "limit",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            throw new InvalidApiRequestException(
                $"Unsupported query parameter '{key}'.");
        }
    }

    private static long ReadLongQuery(
        HttpRequest request,
        string name,
        long defaultValue,
        long minimum)
    {
        if (!request.Query.TryGetValue(
                name,
                out var values))
            return defaultValue;

        if (values.Count != 1 ||
            !long.TryParse(
                values[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value) ||
            value < minimum)
            throw new InvalidApiRequestException(
                $"Query parameter '{name}' is invalid.");

        return value;
    }

    private static int ReadIntQuery(
        HttpRequest request,
        string name,
        int defaultValue,
        int minimum,
        int maximum)
    {
        if (!request.Query.TryGetValue(
                name,
                out var values))
            return defaultValue;

        if (values.Count != 1 ||
            !int.TryParse(
                values[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value) ||
            value < minimum ||
            value > maximum)
            throw new InvalidApiRequestException(
                $"Query parameter '{name}' is invalid.");

        return value;
    }

    private static void ValidateId(
        Guid id,
        string field)
    {
        if (id == Guid.Empty)
            throw new InvalidApiRequestException(
                $"{field} cannot be empty.");
    }

    private static void RequireNoBody(
        HttpRequest request)
    {
        if (request.HttpContext.Features
                .Get<IHttpRequestBodyDetectionFeature>()
                ?.CanHaveBody == true)
            throw new InvalidApiRequestException(
                "This endpoint does not accept a request body.");
    }
}