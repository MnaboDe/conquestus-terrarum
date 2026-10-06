using ConquestusTerrarum.Client.UI;
using UnityEngine;

namespace ConquestusTerrarum.Client
{
    // Scene composition only; networking, map rendering and UI keep separate responsibilities.
    public sealed class ClientBootstrap : MonoBehaviour
    {
        [SerializeField] private MatchController controller;
        [SerializeField] private UiFlowController ui;
        [SerializeField] private MapPresenter map;

        private void Start() => ui.Initialize(controller, map);
    }
}
