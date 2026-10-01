namespace ConquestusTerrarum.Domain.World;

public sealed class PlayerAlliance
{
    public Guid FirstPlayerId { get; }
    public Guid SecondPlayerId { get; }

    public PlayerAlliance(
        Guid firstPlayerId,
        Guid secondPlayerId)
    {
        if (firstPlayerId == Guid.Empty)
        {
            throw new ArgumentException(
                "First player id cannot be empty.",
                nameof(firstPlayerId));
        }

        if (secondPlayerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Second player id cannot be empty.",
                nameof(secondPlayerId));
        }

        if (firstPlayerId == secondPlayerId)
        {
            throw new ArgumentException(
                "A player cannot have an alliance with itself.");
        }

        FirstPlayerId = firstPlayerId;
        SecondPlayerId = secondPlayerId;
    }

    public bool Connects(
        Guid firstPlayerId,
        Guid secondPlayerId)
    {
        return
            FirstPlayerId == firstPlayerId &&
            SecondPlayerId == secondPlayerId
            ||
            FirstPlayerId == secondPlayerId &&
            SecondPlayerId == firstPlayerId;
    }
}