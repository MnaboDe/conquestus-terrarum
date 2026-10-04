using System.Text.Json;
using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Api.Sessions;
using ConquestusTerrarum.Client;

namespace ConquestusTerrarum.Api.Tests;

public sealed class MultiplayerSessionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    [Fact]
    public void ActualCreateAndJoinContractsYieldIndependentSessions_AndOwnershipUsesAuthenticatedIdentity()
    {
        var matchId = Guid.NewGuid();
        var first = new CreateMatchResponse(matchId, Guid.NewGuid(), PlayerCredentials.GenerateToken(), "ABC234");
        var second = new JoinMatchResponse(matchId, Guid.NewGuid(), PlayerCredentials.GenerateToken());
        var creator = MultiplayerSession.FromCreate(JsonSerializer.Deserialize<CreateMatchDto>(JsonSerializer.Serialize(first, JsonOptions), JsonOptions));
        var joiner = MultiplayerSession.FromJoin(JsonSerializer.Deserialize<JoinMatchDto>(JsonSerializer.Serialize(second, JsonOptions), JsonOptions), "ABC234");
        Assert.NotNull(creator);
        Assert.NotNull(joiner);
        Assert.Equal(creator.MatchId, joiner.MatchId);
        Assert.NotEqual(creator.PlayerId, joiner.PlayerId);
        Assert.NotEqual(creator.PlayerToken, joiner.PlayerToken);
        var army = new ArmyDto { id = Guid.NewGuid().ToString(), ownerId = first.PlayerId.ToString().ToUpperInvariant() };
        Assert.True(creator.IsLocalArmy(army));
        Assert.False(joiner.IsLocalArmy(army));
        Assert.False(creator.IsLocalArmy(null));
        Assert.True(creator.Matches(matchId.ToString().ToUpperInvariant()));
        Assert.False(creator.Matches(Guid.NewGuid().ToString()));
        Assert.DoesNotContain(first.PlayerToken, creator.ToString());
        Assert.DoesNotContain(typeof(MatchStateDto).GetFields(), field => field.Name == "playerToken");
    }

    [Theory]
    [InlineData("matchId")]
    [InlineData("playerId")]
    [InlineData("playerToken")]
    [InlineData("joinCode")]
    public void IncompleteCreateResponsesAreRejected(string missing)
    {
        var dto = new CreateMatchDto
        {
            matchId = Guid.NewGuid().ToString(), playerId = Guid.NewGuid().ToString(),
            playerToken = PlayerCredentials.GenerateToken(), joinCode = "ABC234"
        };
        typeof(CreateMatchDto).GetField(missing)!.SetValue(dto, null);
        Assert.Null(MultiplayerSession.FromCreate(dto));
    }

    [Theory]
    [InlineData("not-a-guid", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
    [InlineData("00000000-0000-0000-0000-000000000000", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
    [InlineData("10000000-0000-0000-0000-000000000000", "short")]
    [InlineData("10000000-0000-0000-0000-000000000000", "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void InvalidJoinCredentialsAreRejected(string playerId, string token)
    {
        Assert.Null(MultiplayerSession.FromJoin(new JoinMatchDto
            { matchId = Guid.NewGuid().ToString(), playerId = playerId, playerToken = token }, "ABC234"));
    }

    [Fact]
    public void JoinInputMatchesServerNormalizationWithoutAcceptingAmbiguousCodes()
    {
        Assert.Equal("ABC234", MultiplayerSession.NormalizeJoinCode("  abc234\r\n"));
        Assert.True(MultiplayerSession.ValidJoinCode(MultiplayerSession.NormalizeJoinCode(" abc234 ")));
        Assert.False(MultiplayerSession.ValidJoinCode("ABO123"));
        Assert.False(MultiplayerSession.ValidJoinCode("AB C23"));
        Assert.False(MultiplayerSession.ValidJoinCode(""));
        Assert.Null(MultiplayerSession.FromCreate(null));
        Assert.Null(MultiplayerSession.FromJoin(null, "ABC234"));
    }
}
