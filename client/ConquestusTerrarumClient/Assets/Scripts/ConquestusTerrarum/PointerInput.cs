using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ConquestusTerrarum.Client
{
    public sealed class PointerInput : MonoBehaviour
    {
        private MatchController _controller;

        public static bool IsUiBlocking(Vector2 screenPosition)
        {
            var system = EventSystem.current;
            if (system == null) return true; // Fail closed if scene wiring is missing.
            var hits = new List<RaycastResult>();
            system.RaycastAll(new PointerEventData(system) { position = screenPosition }, hits);
            foreach (var hit in hits)
                if (hit.gameObject.GetComponentInParent<Canvas>() != null) return true;
            return false;
        }

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

            HandlePointer(screenPosition.Value);
        }

        // Shared by the real pointer path and built-player raycast verification.
        public void HandlePointer(Vector2 screenPosition)
        {
            if (_controller == null || !_controller.IsConnected || _controller.IsBusy || IsUiBlocking(screenPosition)) return;
            var camera = Camera.main;
            if (camera == null || !camera.pixelRect.Contains(screenPosition)) return;

            var worldPosition =
                (Vector2)camera.ScreenToWorldPoint(screenPosition);

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
