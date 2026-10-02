using System;
using System.Linq;

namespace ConquestusTerrarum.Client
{
    public enum ArmyRelation
    {
        Unknown,
        Selected,
        SameOwner,
        Ally,
        Hostile
    }

    public static class ArmyRelations
    {
        public static ArmyRelation Between(
            MatchStateDto state,
            ArmyDto selected,
            ArmyDto other)
        {
            if (state == null ||
                selected == null ||
                other == null)
            {
                return ArmyRelation.Unknown;
            }

            if (string.Equals(
                    selected.id,
                    other.id,
                    StringComparison.Ordinal))
            {
                return ArmyRelation.Selected;
            }

            if (string.Equals(
                    selected.ownerId,
                    other.ownerId,
                    StringComparison.Ordinal))
            {
                return ArmyRelation.SameOwner;
            }

            if (AreAlliedOwners(
                    state,
                    selected.ownerId,
                    other.ownerId))
            {
                return ArmyRelation.Ally;
            }

            return ArmyRelation.Hostile;
        }

        public static bool AreAlliedOwners(
            MatchStateDto state,
            string firstOwnerId,
            string secondOwnerId)
        {
            if (string.IsNullOrWhiteSpace(firstOwnerId) ||
                string.IsNullOrWhiteSpace(secondOwnerId))
            {
                return false;
            }

            // Один владелец всегда считается союзником самому себе.
            if (string.Equals(
                    firstOwnerId,
                    secondOwnerId,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (state == null ||
                state.alliances == null)
            {
                return false;
            }

            return state.alliances.Any(
                alliance =>
                    alliance != null &&
                    (
                        string.Equals(
                            alliance.firstPlayerId,
                            firstOwnerId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            alliance.secondPlayerId,
                            secondOwnerId,
                            StringComparison.Ordinal)
                        ||
                        string.Equals(
                            alliance.firstPlayerId,
                            secondOwnerId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            alliance.secondPlayerId,
                            firstOwnerId,
                            StringComparison.Ordinal)
                    ));
        }

        public static string Label(
            ArmyRelation relation)
        {
            switch (relation)
            {
                case ArmyRelation.Selected:
                    return "SELECTED";

                case ArmyRelation.SameOwner:
                    return "SAME OWNER";

                case ArmyRelation.Ally:
                    return "ALLY";

                case ArmyRelation.Hostile:
                    return "HOSTILE";

                default:
                    return "UNKNOWN";
            }
        }
    }
}