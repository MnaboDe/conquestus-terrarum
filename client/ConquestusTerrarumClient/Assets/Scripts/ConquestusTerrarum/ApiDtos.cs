using System;
namespace ConquestusTerrarum.Client
{
    [Serializable]
    public class MatchStateDto
    {
        public string matchId;
        public long version;
        public string serverTime;
        public string checkpointTime;

        public LocationDto[] locations;
        public ConnectionDto[] connections;
        public ArmyDto[] armies;
        public AllianceDto[] alliances;
        public BattleDto[] activeBattles;
    }

    [Serializable]
    public class LocationDto
    {
        public string id;
        public string name;
        public string ownerId;
        public int combatSlotCount;
    }

    [Serializable]
    public class ConnectionDto
    {
        public string fromLocationId;
        public string toLocationId;
        public long travelTimeTicks;
    }

    [Serializable]
    public class ArmyDto
    {
        public string id;
        public string ownerId;
        public string locationId;
        public string enteredLocationAt;
        public string previousLocationId;
        public string retreatLocationId;

        public bool isMoving;
        public bool isDestroyed;
        public bool isInBattle;
        public bool canRetreat;

        public MovementDto movement;
        public UnitDto[] units;
    }

    [Serializable]
    public class MovementDto
    {
        public string fromLocationId;
        public string toLocationId;
        public string startedAt;
        public string arriveAt;
        public string kind;
    }

    [Serializable]
    public class UnitDto
    {
        public string id;
        public double maxHitPoints;
        public double currentHitPoints;
        public double damagePerSecond;
        public bool isAlive;
        public bool isWounded;
    }

    [Serializable]
    public class AllianceDto
    {
        public string firstPlayerId;
        public string secondPlayerId;
    }

    [Serializable]
    public class BattleDto
    {
        public string locationId;
        public string startedAt;
        public CombatSlotDto[] combatSlots;
    }

    [Serializable]
    public class CombatSlotDto
    {
        public int index;

        public string firstArmyId;
        public string firstUnitId;
        public string secondArmyId;
        public string secondUnitId;

        public string pairedAt;
        public string firstDeathAt;
        public string secondDeathAt;
    }

    [Serializable]
    public class MoveArmyRequestDto
    {
        public string destinationLocationId;
    }

    [Serializable]
    public class ProblemDetailsDto
    {
        public string type;
        public string title;
        public int status;
        public string detail;
        public string code;
        public string traceId;
    }
}