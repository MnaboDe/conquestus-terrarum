using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ConquestusTerrarum.Client
{
    public sealed class MapPresenter : MonoBehaviour
    {
        private MatchController _controller;

        private MatchStateDto _presentedState;

        private readonly Dictionary<string, Vector2>
            _locationPositions =
                new(StringComparer.Ordinal);

        private readonly Dictionary<string, GameObject>
            _armyObjects =
                new(StringComparer.Ordinal);

        private GameObject _mapRoot;

        private Sprite _locationSprite;
        private Sprite _armySprite;
        private Material _lineMaterial;

        private void Awake()
        {
            _controller =
                GetComponent<MatchController>();

            CreateRuntimeAssets();
            ConfigureCamera();
        }

        private void Update()
        {
            if (_controller == null ||
                _controller.State == null)
                return;

            if (!ReferenceEquals(
                    _presentedState,
                    _controller.State))
            {
                Rebuild(_controller.State);
            }

            UpdateArmyPositions();
        }

        private void Rebuild(MatchStateDto state)
        {
            _presentedState = state;

            if (_mapRoot != null)
                Destroy(_mapRoot);

            _mapRoot =
                new GameObject("RuntimeMap");

            _mapRoot.transform.SetParent(
                transform,
                false);

            _armyObjects.Clear();
            _locationPositions.Clear();

            var positions =
                MapLayout.Build(state.locations);

            foreach (var pair in positions)
                _locationPositions[pair.Key] = pair.Value;

            CreateConnections(state);
            CreateLocations(state);
            CreateArmies(state);
        }

        private void CreateConnections(MatchStateDto state)
        {
            if (state.connections == null)
                return;

            foreach (var connection in state.connections)
            {
                if (!_locationPositions.TryGetValue(
                        connection.fromLocationId,
                        out var from))
                    continue;

                if (!_locationPositions.TryGetValue(
                        connection.toLocationId,
                        out var to))
                    continue;

                var gameObject =
                    new GameObject("Connection");

                gameObject.transform.SetParent(
                    _mapRoot.transform,
                    false);

                var line =
                    gameObject.AddComponent<LineRenderer>();

                line.material = _lineMaterial;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.startWidth = 0.08f;
                line.endWidth = 0.08f;
                line.sortingOrder = -10;

                line.SetPosition(
                    0,
                    new Vector3(from.x, from.y, 0f));

                line.SetPosition(
                    1,
                    new Vector3(to.x, to.y, 0f));

                line.startColor =
                    new Color(0.55f, 0.65f, 0.75f);

                line.endColor = line.startColor;
            }
        }

        private void CreateLocations(MatchStateDto state)
        {
            if (state.locations == null)
                return;

            foreach (var location in state.locations)
            {
                if (!_locationPositions.TryGetValue(
                        location.id,
                        out var position))
                    continue;

                var gameObject =
                    new GameObject(
                        $"Location - {location.name}");

                gameObject.transform.SetParent(
                    _mapRoot.transform,
                    false);

                gameObject.transform.position =
                    new Vector3(
                        position.x,
                        position.y,
                        0f);

                gameObject.transform.localScale =
                    Vector3.one * 1.25f;

                var renderer =
                    gameObject.AddComponent<SpriteRenderer>();

                renderer.sprite = _locationSprite;
                renderer.color =
                    new Color(
                        0.75f,
                        0.82f,
                        0.9f);

                renderer.sortingOrder = 0;

                var labelObject =
                    new GameObject("Label");

                labelObject.transform.SetParent(
                    gameObject.transform,
                    false);

                labelObject.transform.localPosition =
                    new Vector3(0f, 0.75f, 0f);

                labelObject.transform.localScale =
                    Vector3.one / 1.25f;

                var label =
                    labelObject.AddComponent<TextMesh>();

                label.text = location.name;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.fontSize = 42;
                label.characterSize = 0.08f;
                label.color = Color.white;

                var textRenderer =
                    label.GetComponent<MeshRenderer>();

                textRenderer.sortingOrder = 10;

                if (HasBattle(state, location.id))
                    CreateBattleLabel(gameObject);
            }
        }

        private void CreateBattleLabel(
            GameObject locationObject)
        {
            var battleObject =
                new GameObject("BattleLabel");

            battleObject.transform.SetParent(
                locationObject.transform,
                false);

            battleObject.transform.localPosition =
                new Vector3(0f, -0.8f, 0f);

            battleObject.transform.localScale =
                Vector3.one / 1.25f;

            var label =
                battleObject.AddComponent<TextMesh>();

            label.text = "BATTLE";
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 40;
            label.characterSize = 0.075f;
            label.color = new Color(1f, 0.3f, 0.25f);

            label.GetComponent<MeshRenderer>()
                .sortingOrder = 20;
        }

        private void CreateArmies(MatchStateDto state)
        {
            if (state.armies == null)
                return;

            foreach (var army in state.armies)
            {
                if (army == null ||
                    army.isDestroyed)
                    continue;

                var gameObject =
                    new GameObject(
                        $"Army - {ShortId(army.id)}");

                gameObject.transform.SetParent(
                    _mapRoot.transform,
                    false);

                gameObject.transform.localScale =
                    Vector3.one * 0.42f;

                var renderer =
                    gameObject.AddComponent<SpriteRenderer>();

                renderer.sprite = _armySprite;
                renderer.color =
                    OwnerColor(army.ownerId);

                renderer.sortingOrder = 30;

                _armyObjects[army.id] = gameObject;
            }

            UpdateArmyPositions();
        }

        private void UpdateArmyPositions()
        {
            var state = _presentedState;

            if (state?.armies == null)
                return;

            foreach (var army in state.armies)
            {
                if (army == null ||
                    army.isDestroyed ||
                    !_armyObjects.TryGetValue(
                        army.id,
                        out var armyObject))
                    continue;

                if (army.isMoving &&
                    army.movement != null)
                {
                    armyObject.transform.position =
                        GetMovingPosition(
                            army.movement);

                    continue;
                }

                if (string.IsNullOrWhiteSpace(
                        army.locationId))
                    continue;

                if (!_locationPositions.TryGetValue(
                        army.locationId,
                        out var locationPosition))
                    continue;

                var offset =
                    GetStationaryArmyOffset(
                        state,
                        army);

                armyObject.transform.position =
                    new Vector3(
                        locationPosition.x + offset.x,
                        locationPosition.y + offset.y,
                        -0.1f);
            }
        }

        private Vector3 GetMovingPosition(
            MovementDto movement)
        {
            if (!_locationPositions.TryGetValue(
                    movement.fromLocationId,
                    out var from))
                return Vector3.zero;

            if (!_locationPositions.TryGetValue(
                    movement.toLocationId,
                    out var to))
                return new Vector3(from.x, from.y, -0.1f);

            if (!DateTimeOffset.TryParse(
                    movement.startedAt,
                    out var startedAt))
                return new Vector3(from.x, from.y, -0.1f);

            if (!DateTimeOffset.TryParse(
                    movement.arriveAt,
                    out var arriveAt))
                return new Vector3(from.x, from.y, -0.1f);

            var totalSeconds =
                (arriveAt - startedAt).TotalSeconds;

            if (totalSeconds <= 0)
                return new Vector3(to.x, to.y, -0.1f);

            var elapsedSeconds =
                (_controller.PresentationServerTime -
                 startedAt).TotalSeconds;

            var progress =
                Mathf.Clamp01(
                    (float)(
                        elapsedSeconds /
                        totalSeconds));

            var position =
                Vector2.Lerp(
                    from,
                    to,
                    progress);

            return new Vector3(
                position.x,
                position.y,
                -0.1f);
        }

        private static Vector2 GetStationaryArmyOffset(
            MatchStateDto state,
            ArmyDto army)
        {
            var armiesAtLocation =
                state.armies
                    .Where(other =>
                        other != null &&
                        !other.isDestroyed &&
                        !other.isMoving &&
                        string.Equals(
                            other.locationId,
                            army.locationId,
                            StringComparison.Ordinal))
                    .OrderBy(
                        other => other.id,
                        StringComparer.Ordinal)
                    .ToArray();

            var index =
                Array.FindIndex(
                    armiesAtLocation,
                    other =>
                        string.Equals(
                            other.id,
                            army.id,
                            StringComparison.Ordinal));

            if (index < 0)
                return Vector2.zero;

            var count =
                armiesAtLocation.Length;

            if (count == 1)
                return new Vector2(0f, -0.05f);

            var angle =
                Mathf.PI * 2f *
                index / count;

            const float radius = 0.48f;

            return new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) *
                radius;
        }

        private static bool HasBattle(
            MatchStateDto state,
            string locationId)
        {
            return state.activeBattles != null &&
                   state.activeBattles.Any(
                       battle =>
                           battle != null &&
                           string.Equals(
                               battle.locationId,
                               locationId,
                               StringComparison.Ordinal));
        }

        private void CreateRuntimeAssets()
        {
            _locationSprite =
                CreateCircleSprite(64);

            _armySprite =
                CreateCircleSprite(32);

            _lineMaterial =
                new Material(
                    Shader.Find("Sprites/Default"));
        }

        private static Sprite CreateCircleSprite(
            int size)
        {
            var texture =
                new Texture2D(
                    size,
                    size,
                    TextureFormat.RGBA32,
                    false);

            texture.filterMode =
                FilterMode.Bilinear;

            var pixels =
                new Color[size * size];

            var center =
                (size - 1) * 0.5f;

            var radius =
                size * 0.46f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;

                    var inside =
                        dx * dx + dy * dy <=
                        radius * radius;

                    pixels[
                        y * size + x] =
                        inside
                            ? Color.white
                            : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(
                texture,
                new Rect(
                    0,
                    0,
                    size,
                    size),
                new Vector2(0.5f, 0.5f),
                size);
        }

        private static Color OwnerColor(
            string ownerId)
        {
            if (!Guid.TryParse(
                    ownerId,
                    out var id))
            {
                return Color.white;
            }

            var bytes =
                id.ToByteArray();

            var hue =
                bytes[0] / 255f;

            return Color.HSVToRGB(
                hue,
                0.7f,
                0.95f);
        }

        private static string ShortId(
            string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "?";

            return id.Length <= 6
                ? id
                : id[..6];
        }

        private static void ConfigureCamera()
        {
            var camera =
                Camera.main;

            if (camera == null)
                return;

            camera.orthographic = true;
            camera.orthographicSize = 6.5f;

            camera.transform.position =
                new Vector3(
                    2.5f,
                    0f,
                    -10f);
        }
    }
}