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

        private readonly Dictionary<string, Vector2> _locationPositions =
            new Dictionary<string, Vector2>(StringComparer.Ordinal);

        private readonly Dictionary<string, GameObject> _armyObjects =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        private GameObject _mapRoot;

        private Sprite _locationSprite;
        private Sprite _armySprite;
        private Material _lineMaterial;

        private void Awake()
        {
            _controller = GetComponent<MatchController>();

            CreateRuntimeAssets();
            ConfigureCamera();
        }

        private void Update()
        {
            if (_controller == null || _controller.State == null)
                return;

            if (!ReferenceEquals(_presentedState, _controller.State))
            {
                Rebuild(_controller.State);
            }

            UpdateArmyPositions();
            UpdateArmySelectionVisuals();
        }

        private void Rebuild(MatchStateDto state)
        {
            _presentedState = state;

            if (_mapRoot != null)
                Destroy(_mapRoot);

            _mapRoot = new GameObject("RuntimeMap");
            _mapRoot.transform.SetParent(transform, false);

            _armyObjects.Clear();
            _locationPositions.Clear();

            Dictionary<string, Vector2> positions =
                MapLayout.Build(state.locations);

            foreach (KeyValuePair<string, Vector2> pair in positions)
            {
                _locationPositions[pair.Key] = pair.Value;
            }

            CreateConnections(state);
            CreateLocations(state);
            CreateArmies(state);
        }

        private void CreateConnections(MatchStateDto state)
        {
            if (state.connections == null)
                return;

            foreach (ConnectionDto connection in state.connections)
            {
                if (connection == null)
                    continue;

                Vector2 from;
                if (!_locationPositions.TryGetValue(
                        connection.fromLocationId,
                        out from))
                {
                    continue;
                }

                Vector2 to;
                if (!_locationPositions.TryGetValue(
                        connection.toLocationId,
                        out to))
                {
                    continue;
                }

                GameObject connectionObject =
                    new GameObject("Connection");

                connectionObject.transform.SetParent(
                    _mapRoot.transform,
                    false);

                LineRenderer line =
                    connectionObject.AddComponent<LineRenderer>();

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

                Color lineColor =
                    new Color(0.55f, 0.65f, 0.75f);

                line.startColor = lineColor;
                line.endColor = lineColor;
            }
        }

        private void CreateLocations(MatchStateDto state)
        {
            if (state.locations == null)
                return;

            foreach (LocationDto location in state.locations)
            {
                if (location == null)
                    continue;

                Vector2 position;

                if (!_locationPositions.TryGetValue(
                        location.id,
                        out position))
                {
                    continue;
                }

                GameObject locationObject =
                    new GameObject(
                        "Location - " + location.name);

                locationObject.transform.SetParent(
                    _mapRoot.transform,
                    false);

                locationObject.transform.position =
                    new Vector3(
                        position.x,
                        position.y,
                        0f);

                locationObject.transform.localScale =
                    Vector3.one * 1.25f;

                SpriteRenderer renderer =
                    locationObject.AddComponent<SpriteRenderer>();

                renderer.sprite = _locationSprite;
                renderer.color =
                    new Color(0.75f, 0.82f, 0.9f);

                renderer.sortingOrder = 0;

                // Click target
                CircleCollider2D collider =
                    locationObject.AddComponent<CircleCollider2D>();

                collider.radius = 0.5f;

                LocationView locationView =
                    locationObject.AddComponent<LocationView>();

                locationView.Initialize(location.id);

                // Label
                GameObject labelObject =
                    new GameObject("Label");

                labelObject.transform.SetParent(
                    locationObject.transform,
                    false);

                labelObject.transform.localPosition =
                    new Vector3(0f, 0.75f, 0f);

                labelObject.transform.localScale =
                    Vector3.one / 1.25f;

                TextMesh label =
                    labelObject.AddComponent<TextMesh>();

                label.text = location.name;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.fontSize = 42;
                label.characterSize = 0.08f;
                label.color = Color.white;

                MeshRenderer textRenderer =
                    label.GetComponent<MeshRenderer>();

                textRenderer.sortingOrder = 10;

                if (HasBattle(state, location.id))
                {
                    CreateBattleLabel(locationObject);
                }
            }
        }

        private void CreateBattleLabel(
            GameObject locationObject)
        {
            GameObject battleObject =
                new GameObject("BattleLabel");

            battleObject.transform.SetParent(
                locationObject.transform,
                false);

            battleObject.transform.localPosition =
                new Vector3(0f, -0.8f, 0f);

            battleObject.transform.localScale =
                Vector3.one / 1.25f;

            TextMesh label =
                battleObject.AddComponent<TextMesh>();

            label.text = "BATTLE";
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 40;
            label.characterSize = 0.075f;
            label.color =
                new Color(1f, 0.3f, 0.25f);

            MeshRenderer renderer =
                label.GetComponent<MeshRenderer>();

            renderer.sortingOrder = 20;
        }

        private void CreateArmies(MatchStateDto state)
        {
            if (state.armies == null)
                return;

            foreach (ArmyDto army in state.armies)
            {
                if (army == null || army.isDestroyed)
                    continue;

                GameObject armyObject =
                    new GameObject(
                        "Army - " + ShortId(army.id));

                armyObject.transform.SetParent(
                    _mapRoot.transform,
                    false);

                armyObject.transform.localScale =
                    Vector3.one * 0.42f;

                SpriteRenderer renderer =
                    armyObject.AddComponent<SpriteRenderer>();

                renderer.sprite = _armySprite;
                renderer.color =
                    OwnerColor(army.ownerId);

                renderer.sortingOrder = 30;

                // Click target
                CircleCollider2D collider =
                    armyObject.AddComponent<CircleCollider2D>();

                collider.radius = 0.5f;

                ArmyView armyView =
                    armyObject.AddComponent<ArmyView>();

                armyView.Initialize(army.id);

                _armyObjects[army.id] = armyObject;
            }

            UpdateArmyPositions();
        }

        private void UpdateArmyPositions()
        {
            MatchStateDto state = _presentedState;

            if (state == null || state.armies == null)
                return;

            foreach (ArmyDto army in state.armies)
            {
                if (army == null || army.isDestroyed)
                    continue;

                GameObject armyObject;

                if (!_armyObjects.TryGetValue(
                        army.id,
                        out armyObject))
                {
                    continue;
                }

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
                {
                    continue;
                }

                Vector2 locationPosition;

                if (!_locationPositions.TryGetValue(
                        army.locationId,
                        out locationPosition))
                {
                    continue;
                }

                Vector2 offset =
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
            Vector2 from;

            if (!_locationPositions.TryGetValue(
                    movement.fromLocationId,
                    out from))
            {
                return Vector3.zero;
            }

            Vector2 to;

            if (!_locationPositions.TryGetValue(
                    movement.toLocationId,
                    out to))
            {
                return new Vector3(
                    from.x,
                    from.y,
                    -0.1f);
            }

            DateTimeOffset startedAt;

            if (!DateTimeOffset.TryParse(
                    movement.startedAt,
                    out startedAt))
            {
                return new Vector3(
                    from.x,
                    from.y,
                    -0.1f);
            }

            DateTimeOffset arriveAt;

            if (!DateTimeOffset.TryParse(
                    movement.arriveAt,
                    out arriveAt))
            {
                return new Vector3(
                    from.x,
                    from.y,
                    -0.1f);
            }

            double totalSeconds =
                (arriveAt - startedAt)
                .TotalSeconds;

            if (totalSeconds <= 0)
            {
                return new Vector3(
                    to.x,
                    to.y,
                    -0.1f);
            }

            double elapsedSeconds =
                (_controller.PresentationServerTime -
                 startedAt)
                .TotalSeconds;

            float progress =
                Mathf.Clamp01(
                    (float)(
                        elapsedSeconds /
                        totalSeconds));

            Vector2 position =
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
            ArmyDto[] armiesAtLocation =
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

            int index =
                Array.FindIndex(
                    armiesAtLocation,
                    other =>
                        string.Equals(
                            other.id,
                            army.id,
                            StringComparison.Ordinal));

            if (index < 0)
                return Vector2.zero;

            int count =
                armiesAtLocation.Length;

            if (count == 1)
                return new Vector2(0f, -0.05f);

            float angle =
                Mathf.PI *
                2f *
                index /
                count;

            const float radius = 0.48f;

            return new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) *
                radius;
        }

        private void UpdateArmySelectionVisuals()
{
    ArmyDto selectedArmy =
        _controller.SelectedArmy;

    foreach (
        KeyValuePair<string, GameObject> pair
        in _armyObjects)
    {
        GameObject armyObject =
            pair.Value;

        if (armyObject == null)
            continue;

        SpriteRenderer renderer =
            armyObject.GetComponent<SpriteRenderer>();

        ArmyDto army = null;

        if (_presentedState != null &&
            _presentedState.armies != null)
        {
            army =
                _presentedState.armies
                    .FirstOrDefault(
                        candidate =>
                            candidate != null &&
                            string.Equals(
                                candidate.id,
                                pair.Key,
                                StringComparison.Ordinal));
        }

        if (army == null)
            continue;

        // Пока ничего не выбрано —
        // показываем обычный цвет владельца.
        if (selectedArmy == null)
        {
            armyObject.transform.localScale =
                Vector3.one * 0.42f;

            if (renderer != null)
            {
                renderer.color =
                    OwnerColor(army.ownerId);
            }

            continue;
        }

        ArmyRelation relation =
            ArmyRelations.Between(
                _presentedState,
                selectedArmy,
                army);

        switch (relation)
        {
            case ArmyRelation.Selected:
                armyObject.transform.localScale =
                    Vector3.one * 0.64f;

                if (renderer != null)
                {
                    renderer.color =
                        new Color(
                            1f,
                            0.85f,
                            0.15f);
                }

                break;

            case ArmyRelation.SameOwner:
                armyObject.transform.localScale =
                    Vector3.one * 0.48f;

                if (renderer != null)
                {
                    renderer.color =
                        new Color(
                            0.25f,
                            0.75f,
                            1f);
                }

                break;

            case ArmyRelation.Ally:
                armyObject.transform.localScale =
                    Vector3.one * 0.48f;

                if (renderer != null)
                {
                    renderer.color =
                        new Color(
                            0.25f,
                            0.95f,
                            0.4f);
                }

                break;

            case ArmyRelation.Hostile:
                armyObject.transform.localScale =
                    Vector3.one * 0.48f;

                if (renderer != null)
                {
                    renderer.color =
                        new Color(
                            1f,
                            0.25f,
                            0.25f);
                }

                break;

            default:
                armyObject.transform.localScale =
                    Vector3.one * 0.42f;

                if (renderer != null)
                {
                    renderer.color =
                        Color.white;
                }

                break;
        }
    }
}

        private static bool HasBattle(
            MatchStateDto state,
            string locationId)
        {
            if (state.activeBattles == null)
                return false;

            return state.activeBattles.Any(
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

            Shader shader =
                Shader.Find("Sprites/Default");

            _lineMaterial =
                new Material(shader);
        }

        private static Sprite CreateCircleSprite(
            int size)
        {
            Texture2D texture =
                new Texture2D(
                    size,
                    size,
                    TextureFormat.RGBA32,
                    false);

            texture.filterMode =
                FilterMode.Bilinear;

            Color[] pixels =
                new Color[size * size];

            float center =
                (size - 1) * 0.5f;

            float radius =
                size * 0.46f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;

                    bool inside =
                        dx * dx + dy * dy <=
                        radius * radius;

                    pixels[y * size + x] =
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
            Guid id;

            if (!Guid.TryParse(
                    ownerId,
                    out id))
            {
                return Color.white;
            }

            byte[] bytes =
                id.ToByteArray();

            float hue =
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
                : id.Substring(0, 6);
        }

        private static void ConfigureCamera()
        {
            Camera camera = Camera.main;

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