#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace VirooCaveTemplate.Interactions.Visuals
{
    /// <summary>
    /// Pointy-top hexagon drawn as UI geometry (no texture), matching the hex motif of the
    /// GUI_Sci_FI pack. Filled when <see cref="Thickness"/> is 0, otherwise an outline of that
    /// width. <see cref="CornerFraction"/> &gt; 0 draws only that fraction of each edge, centred on
    /// the corners - a "bracket" ring. <see cref="FillAmount"/> &lt; 1 draws only that fraction of
    /// the outline, clockwise from the top corner - a progress ring.
    /// </summary>
    public class HexagonGraphic : MaskableGraphic
    {
        [SerializeField] private float thickness;
        [Range(0f, 1f)]
        [SerializeField] private float cornerFraction;
        [Range(0f, 1f)]
        [SerializeField] private float fillAmount = 1f;

        public float Thickness
        {
            get => thickness;
            set { thickness = value; SetVerticesDirty(); }
        }

        public float CornerFraction
        {
            get => cornerFraction;
            set { cornerFraction = value; SetVerticesDirty(); }
        }

        public float FillAmount
        {
            get => fillAmount;
            set { fillAmount = Mathf.Clamp01(value); SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float outer = Mathf.Min(rect.width, rect.height) * 0.5f;
            Color32 col = color;

            if (thickness <= 0f || thickness >= outer)
            {
                vh.AddVert(center, col, Vector4.zero);
                for (int i = 0; i < 6; i++)
                {
                    vh.AddVert(Corner(center, i, outer), col, Vector4.zero);
                }

                for (int i = 0; i < 6; i++)
                {
                    vh.AddTriangle(0, 1 + i, 1 + ((i + 1) % 6));
                }

                return;
            }

            // Inner radius chosen so the band's perpendicular width equals thickness.
            float inner = outer - (thickness / Mathf.Cos(30f * Mathf.Deg2Rad));
            bool partial = fillAmount < 1f;
            bool brackets = !partial && cornerFraction > 0f && cornerFraction < 1f;
            float half = cornerFraction * 0.5f;

            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;
                Vector2 o0 = Corner(center, i, outer), o1 = Corner(center, next, outer);
                Vector2 i0 = Corner(center, i, inner), i1 = Corner(center, next, inner);

                if (partial)
                {
                    // Each edge is one sixth of the outline.
                    float to = Mathf.Clamp01((fillAmount * 6f) - i);
                    if (to <= 0f)
                    {
                        break;
                    }

                    EdgeQuad(vh, o0, o1, i0, i1, 0f, to, col);
                }
                else if (brackets)
                {
                    EdgeQuad(vh, o0, o1, i0, i1, 0f, half, col);
                    EdgeQuad(vh, o0, o1, i0, i1, 1f - half, 1f, col);
                }
                else
                {
                    EdgeQuad(vh, o0, o1, i0, i1, 0f, 1f, col);
                }
            }
        }

        private static Vector2 Corner(Vector2 center, int index, float radius)
        {
            // Clockwise from the top corner (matters only for FillAmount).
            float angle = (90f - (60f * index)) * Mathf.Deg2Rad;
            return center + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }

        private static void EdgeQuad(VertexHelper vh, Vector2 o0, Vector2 o1, Vector2 i0, Vector2 i1, float from, float to, Color32 col)
        {
            int start = vh.currentVertCount;
            vh.AddVert(Vector2.Lerp(o0, o1, from), col, Vector4.zero);
            vh.AddVert(Vector2.Lerp(o0, o1, to), col, Vector4.zero);
            vh.AddVert(Vector2.Lerp(i0, i1, to), col, Vector4.zero);
            vh.AddVert(Vector2.Lerp(i0, i1, from), col, Vector4.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
