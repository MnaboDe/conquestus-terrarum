#nullable disable
namespace ConquestusTerrarum.Client
{
    public static class ClientInputValidation
    {
        public static string JoinCodeError(string input) =>
            MultiplayerSession.ValidJoinCode(MultiplayerSession.NormalizeJoinCode(input)) ? null :
                "Enter a six-character join code, using the letters and digits shared by the other player.";
    }
}
