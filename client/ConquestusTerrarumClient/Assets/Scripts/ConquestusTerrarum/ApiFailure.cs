#nullable disable
namespace ConquestusTerrarum.Client
{
    public sealed class ApiFailure
    {
        public long Status { get; }
        public string Message { get; }
        public ApiFailure(long status, string message) { Status = status; Message = message; }
        public string RecoveryMessage => Status == 401
            ? "This saved game cannot be resumed: the credential was rejected (401). Choose Forget Saved Game to remove it."
            : Status == 404
                ? "This saved game cannot be resumed: the match no longer exists (404). Choose Forget Saved Game to remove it."
                : Message + " Your saved session is kept. Retry when the backend is available.";
    }
}
