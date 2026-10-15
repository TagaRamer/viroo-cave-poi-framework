#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VirooCaveTemplate.Interactions.Visuals
{
    /// <summary>
    /// A UI line through a list of points (in this graphic's local space), drawn only up to
    /// <see cref="Progress"/> of its total length so it can animate "drawing itself". Once fully
    /// drawn, a small square terminal dot is added at the last point.
    /// </summary>
    public class PolylineGraphic : MaskableGraphic
    {
        [SerializeField] private float thickness = 2f;
        [SerializeField] private float endDotSize = 7f;

        private readonly List<Vector2> points = new();
        private float progress = 1f;

        public float EndDotSize
        {
            get => endDotSize;
            set { endDotSize = value; SetVerticesDirty(); }
        }

        public float Thickness
        {
            get => thickness;
            set { thickness = value; SetVerticesDirty(); }
        }

        public float Progress
        {
            get => progress;
            set
            {
                value = Mathf.Clamp01(value);
                if (!Mathf.Approximately(value, progress))
                {
                    progress = value;
                    SetVerticesDirty();
                }
            }
        }

        public void SetPoints(params Vector2[] newPoints)
        {
            points.Clear();
            points.AddRange(newPoints);
            SetVerticesDirty();
        }

        /// <summary>Uses <paramref name="count"/> points of <paramref name="source"/> from <paramref name="start"/>.</summary>
        public void SetPoints(Vector2[] source, int start, int count)
        {
            points.Clear();
            for (int i = start; i < start + count && i < source.Length; i++)
            {
                points.Add(source[i]);
            }

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points.Count < 2 || progress <= 0f)
            {
                return;
            }

            float total = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                total += Vector2.Distance(points[i - 1], points[i]);
            }

            Color32 col = color;
            float remaining = total * progress;
            float half = thickness * 0.5f;

            for (int i = 1; i < points.Count && remaining > 0f; i++)
            {
                Vector2 a = points[i - 1];
                Vector2 segment = points[i] - a;
                float length = segment.magnitude;
                if (length <= Mathf.Epsilon)
                {
                    continue;
                }

                Vector2 dir = segment / length;
                Vector2 normal = new Vector2(-dir.y, dir.x) * half;
                float drawn = Mathf.Min(length, remaining);
                remaining -= length;

                // Extend each segment by half the thickness at both ends so elbows meet cleanly.
                Vector2 from = a - (dir * half);
                Vector2 to = a + (dir * (drawn + half));
                AddQuad(vh, from + normal, to + normal, to - normal, from - normal, col);
            }

            if (progress >= 0.999f && endDotSize > 0f)
            {
                Vector2 p = points[^1];
                float d = endDotSize * 0.5f;
                AddQuad(vh, p + new Vector2(-d, d), p + new Vector2(d, d), p + new Vector2(d, -d), p + new Vector2(-d, -d), col);
            }
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, col, Vector4.zero);
            vh.AddVert(b, col, Vector4.zero);
            vh.AddVert(c, col, Vector4.zero);
            vh.AddVert(d, col, Vector4.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
