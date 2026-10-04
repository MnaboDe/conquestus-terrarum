using UnityEngine;
using UnityEngine.InputSystem;

namespace ConquestusTerrarum.Client
{
    public sealed class PointerInput : MonoBehaviour
    {
        private MatchController _controller;

        private void Awake()
        {
            _controller = GetComponent<MatchController>();
        }

        private void Update()
        {
            if (_controller == null || !_controller.IsConnected || _controller.IsBusy)
                return;

            Vector2? screenPosition = null;

            if (Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
            }
            else if (Touchscreen.current != null &&
                     Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition =
                    Touchscreen.current.primaryTouch.position.ReadValue();
            }

            if (screenPosition == null)
                return;

            // IMGUI uses a top-left origin. HUD interaction must not also command the map behind it.
            var guiPosition = new Vector2(screenPosition.Value.x, Screen.height - screenPosition.Value.y);
            if (DebugHud.PanelRect.Contains(guiPosition)) return;

            var camera = Camera.main;

            if (camera == null)
                return;

            var worldPosition =
                (Vector2)camera.ScreenToWorldPoint(screenPosition.Value);

            var hits =
                Physics2D.OverlapPointAll(worldPosition);

            // Армия имеет приоритет над location,
            // потому что её маркер находится поверх location.
            foreach (var hit in hits)
            {
                var armyView =
                    hit.GetComponent<ArmyView>();

                if (armyView == null)
                    continue;

                _controller.SelectArmy(armyView.ArmyId);
                return;
            }

            foreach (var hit in hits)
            {
                var locationView =
                    hit.GetComponent<LocationView>();

                if (locationView == null)
                    continue;

                _controller.ClickLocation(
                    locationView.LocationId);

                return;
            }
        }
    }
}
