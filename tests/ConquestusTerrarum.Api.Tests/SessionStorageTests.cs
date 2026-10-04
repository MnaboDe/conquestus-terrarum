using ConquestusTerrarum.Api.Sessions;
using ConquestusTerrarum.Client;

namespace ConquestusTerrarum.Api.Tests;

public sealed class SessionStorageTests
{
    private sealed class Records : ISessionRecordStore
    {
        public string? Value;
        public bool HasRecord => Value != null;
        public string Read() => Value!;
        public void Write(string record) => Value = record;
        public void Clear() => Value = null;
    }

    private static CreateMatchDto Created() => new()
    {
        matchId = Guid.NewGuid().ToString(), playerId = Guid.NewGuid().ToString(),
        playerToken = PlayerCredentials.GenerateToken(), joinCode = "ABC234"
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IssuedCreateOrJoinSessionSurvivesNewStorageInstanceAndRestoresOwnership(bool join)
    {
        var dto = Created();
        var issued = join
            ? MultiplayerSession.FromJoin(new JoinMatchDto { matchId = dto.matchId, playerId = dto.playerId, playerToken = dto.playerToken }, dto.joinCode)
            : MultiplayerSession.FromCreate(dto);
        var records = new Records();
        new SessionStorage(records).Save(issued);
        var reopened = new SessionStorage(records);
        Assert.True(reopened.HasSavedSession);
        var saved = reopened.Load();
        Assert.Equal(issued.MatchId, saved.MatchId);
        Assert.Equal(issued.PlayerId, saved.PlayerId);
        Assert.Equal(issued.PlayerToken, saved.PlayerToken);
        Assert.Equal(issued.JoinCode, saved.JoinCode);
        Assert.True(saved.IsLocalArmy(new ArmyDto { ownerId = dto.playerId }));
        Assert.False(saved.IsLocalArmy(new ArmyDto { ownerId = Guid.NewGuid().ToString() }));
        Assert.Equal(5, records.Value!.Split('\n').Length);
        Assert.DoesNotContain("cursor", records.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("armies", records.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(dto.playerToken, saved.ToString());
    }

    [Fact]
    public void EmptyStorageHasNoSession_AndClearIsIdempotent()
    {
        var store = new SessionStorage(new Records());
        Assert.False(store.HasSavedSession);
        Assert.Null(store.Load());
        store.Clear();
        Assert.False(store.HasSavedSession);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("2\nmatch\nplayer\ntoken\nABC234")]
    [InlineData("1\nmatch\nplayer\ntoken\nABC234\nextra")]
    public void MalformedRecordRemainsUntilExplicitForget(string record)
    {
        var records = new Records { Value = record };
        var store = new SessionStorage(records);
        Assert.True(store.HasSavedSession);
        Assert.Null(store.Load());
        Assert.Throws<InvalidOperationException>(() => store.Save(MultiplayerSession.FromCreate(Created())));
        Assert.Equal(record, records.Value);
        store.Clear();
        Assert.False(store.HasSavedSession);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void InvalidSavedTokenIsRejectedWithoutDeletingTheRecord(string token)
    {
        var dto = Created();
        var records = new Records { Value = $"1\n{dto.matchId}\n{dto.playerId}\n{token}\n{dto.joinCode}" };
        var store = new SessionStorage(records);
        Assert.Null(store.Load());
        Assert.True(store.HasSavedSession);
    }

    [Fact]
    public void RecoveryDoesNotRequireJoinCode()
    {
        var dto = Created();
        var restored = MultiplayerSession.Restore(dto.matchId, dto.playerId, dto.playerToken, null);
        var store = new SessionStorage(new Records());
        store.Save(restored);
        Assert.Equal("", store.Load().JoinCode);
        Assert.Equal(dto.playerToken, store.Load().PlayerToken);
    }

    [Fact]
    public void AnotherCredentialCannotOverwriteSavedSession_ExplicitForgetAllowsReplacement()
    {
        var records = new Records();
        var store = new SessionStorage(records);
        var first = MultiplayerSession.FromCreate(Created());
        store.Save(first);
        store.Save(first); // Retry a failed flush of exactly the same record is safe.
        var second = MultiplayerSession.FromCreate(Created());
        Assert.Throws<InvalidOperationException>(() => store.Save(second));
        Assert.Equal(first.PlayerToken, store.Load().PlayerToken);
        store.Clear();
        store.Save(second);
        Assert.Equal(second.PlayerId, store.Load().PlayerId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(401)]
    [InlineData(404)]
    public void SnapshotFailuresNeverDeleteCredential_RecoveryMessageDistinguishesRetryFromForget(int status)
    {
        var records = new Records();
        var store = new SessionStorage(records);
        var issued = MultiplayerSession.FromCreate(Created());
        store.Save(issued);
        var failure = new ApiFailure(status, "Backend unavailable.");
        Assert.Equal(issued.PlayerToken, new SessionStorage(records).Load().PlayerToken);
        if (status == 401 || status == 404) Assert.Contains("cannot be resumed", failure.RecoveryMessage);
        else Assert.Contains("Retry", failure.RecoveryMessage);
        Assert.DoesNotContain(issued.PlayerToken, failure.RecoveryMessage);
    }
}
