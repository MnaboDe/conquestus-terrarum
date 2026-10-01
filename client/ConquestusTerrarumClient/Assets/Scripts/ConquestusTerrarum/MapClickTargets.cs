using UnityEngine;

namespace ConquestusTerrarum.Client
{
    public sealed class ArmyView : MonoBehaviour
    {
        public string ArmyId { get; private set; }

        public void Initialize(string armyId)
        {
            ArmyId = armyId;
        }
    }

    public sealed class LocationView : MonoBehaviour
    {
        public string LocationId { get; private set; }

        public void Initialize(string locationId)
        {
            LocationId = locationId;
        }
    }
}