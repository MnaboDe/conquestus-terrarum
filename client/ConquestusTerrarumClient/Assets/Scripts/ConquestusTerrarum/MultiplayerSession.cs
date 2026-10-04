#nullable disable
using System;
using System.Linq;

namespace ConquestusTerrarum.Client
{
    // Credentials belong to the connection, never to a simulation DTO or persistent client settings.
    public sealed class MultiplayerSession
    {
        public string MatchId { get; }
        public string PlayerId { get; }
        public string PlayerToken { get; }
        public string JoinCode { get; }

        private MultiplayerSession(string matchId, string playerId, string playerToken, string joinCode)
        {
            MatchId = matchId;
            PlayerId = playerId;
            PlayerToken = playerToken;
            JoinCode = joinCode;
        }

        public static MultiplayerSession FromCreate(CreateMatchDto dto)
        {
            if (dto == null || !ValidCredentials(dto.matchId, dto.playerId, dto.playerToken) ||
                !ValidJoinCode(dto.joinCode)) return null;
            return new MultiplayerSession(dto.matchId, dto.playerId, dto.playerToken, dto.joinCode);
        }

        public static MultiplayerSession FromJoin(JoinMatchDto dto, string joinCode)
        {
            if (dto == null || !ValidCredentials(dto.matchId, dto.playerId, dto.playerToken) ||
                !ValidJoinCode(joinCode)) return null;
            return new MultiplayerSession(dto.matchId, dto.playerId, dto.playerToken, joinCode);
        }

        public bool IsLocalArmy(ArmyDto army) => army != null &&
            string.Equals(army.ownerId, PlayerId, StringComparison.OrdinalIgnoreCase);

        public bool Matches(string matchId) => string.Equals(MatchId, matchId, StringComparison.OrdinalIgnoreCase);

        public static string NormalizeJoinCode(string code) => (code ?? "").Trim().ToUpperInvariant();

        public static bool ValidJoinCode(string code) => code != null && code.Length == 6 &&
            code.All(c => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".IndexOf(c) >= 0);

        private static bool ValidCredentials(string matchId, string playerId, string token) =>
            Guid.TryParse(matchId, out var match) && match != Guid.Empty &&
            Guid.TryParse(playerId, out var player) && player != Guid.Empty &&
            token != null && token.Length == 43 &&
            token.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_');
    }
}
