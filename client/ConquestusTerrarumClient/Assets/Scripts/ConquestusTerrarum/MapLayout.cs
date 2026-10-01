using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ConquestusTerrarum.Client
{
    public static class MapLayout
    {
        public static Dictionary<string, Vector2> Build(
            LocationDto[] locations)
        {
            var result =
                new Dictionary<string, Vector2>(
                    StringComparer.Ordinal);

            if (locations == null || locations.Length == 0)
                return result;

            var ordered = locations
                .OrderBy(
                    location => location.id,
                    StringComparer.Ordinal)
                .ToArray();

            const float radius = 4.2f;

            // Немного сдвигаем карту вправо,
            // чтобы debug HUD не перекрывал её.
            var center = new Vector2(2.5f, 0f);

            for (var index = 0; index < ordered.Length; index++)
            {
                var angle =
                    Mathf.PI * 2f *
                    index / ordered.Length +
                    Mathf.PI * 0.5f;

                var position =
                    center +
                    new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle)) *
                    radius;

                result[ordered[index].id] = position;
            }

            return result;
        }
    }
}