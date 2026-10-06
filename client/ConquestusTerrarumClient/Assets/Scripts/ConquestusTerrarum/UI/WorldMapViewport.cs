using UnityEngine;

namespace ConquestusTerrarum.Client.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class WorldMapViewport : MonoBehaviour
    {
        private MapPresenter _map;
        private RectTransform _rect;
        private readonly Vector3[] _corners = new Vector3[4];
        public void Initialize(MapPresenter map) { _map = map; _rect = (RectTransform)transform; }
        private void OnEnable() => Canvas.willRenderCanvases += ApplyViewport;
        private void OnDisable() => Canvas.willRenderCanvases -= ApplyViewport;
        private void ApplyViewport()
        {
            if (_map == null || Screen.width <= 0 || Screen.height <= 0) return;
            _rect.GetWorldCorners(_corners);
            var min = RectTransformUtility.WorldToScreenPoint(null, _corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(null, _corners[2]);
            _map.SetViewport(Rect.MinMaxRect(min.x / Screen.width, min.y / Screen.height, max.x / Screen.width, max.y / Screen.height));
        }
    }
}
