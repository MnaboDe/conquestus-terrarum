using ConquestusTerrarum.Api.Contracts;
using ConquestusTerrarum.Api.ErrorHandling;
using ConquestusTerrarum.Persistence;

namespace ConquestusTerrarum.Api.Sessions;

public sealed class MatchSessionService(IMatchSessionRepository sessions)
{
    public async Task<CreateMatchResponse> CreateAsync(CancellationToken cancellationToken)
    {
        var matchId = Guid.NewGuid();
        var firstPlayer = Guid.NewGuid();
        var secondPlayer = Guid.NewGuid();
        var token = PlayerCredentials.GenerateToken();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var joinCode = PlayerCredentials.GenerateJoinCode();
            try
            {
                await sessions.CreateAsync(new MatchSessionCreation(matchId, firstPlayer, secondPlayer,
                        PlayerCredentials.HashToken(token), joinCode),
                    time => MultiplayerWorldFactory.Create(time, firstPlayer, secondPlayer), cancellationToken);
                return new CreateMatchResponse(matchId, firstPlayer, token, joinCode);
            }
            catch (JoinCodeCollisionException) { }
        }
        throw new InvalidOperationException("Could not allocate a join code.");
    }

    public async Task<JoinMatchResponse> JoinAsync(JoinMatchRequest request, CancellationToken cancellationToken)
    {
        var code = PlayerCredentials.NormalizeJoinCode(request.JoinCode)
            ?? throw new InvalidApiRequestException("joinCode must contain six valid letters or digits.");
        var token = PlayerCredentials.GenerateToken();
        var identity = await sessions.JoinAsync(code, PlayerCredentials.HashToken(token), cancellationToken);
        return new JoinMatchResponse(identity.MatchId, identity.PlayerId, token);
    }
}
