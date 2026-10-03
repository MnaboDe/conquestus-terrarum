using System.Text.Json;
using System.Text.Json.Serialization;
using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Api.Endpoints;
using ConquestusTerrarum.Api.ErrorHandling;
using ConquestusTerrarum.Api.Sessions;
using ConquestusTerrarum.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter<MovementKindResponse>(JsonNamingPolicy.CamelCase)));
builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres must be configured.")));
builder.Services.AddSingleton<IMatchRepository, PostgresMatchRepository>();
builder.Services.AddSingleton<IMatchSessionRepository, PostgresMatchSessionRepository>();
builder.Services.AddSingleton<MatchSessionService>();
builder.Services.AddScoped<MatchPlayer>();

var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseStatusCodePages(async context => await Results.Problem(statusCode: context.HttpContext.Response.StatusCode)
    .ExecuteAsync(context.HttpContext));
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapMatchEndpoints();
app.Run();

public partial class Program;
