#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace VirooCaveTemplate.Interactions.Visuals
{
    /// <summary>
    /// Rectangle with its top-left and bottom-right corners cut off at 45 degrees, plus a border -
    /// the same silhouette as the GUI_Sci_FI popup frame, drawn as UI geometry so it stays crisp
    /// at any size. <see cref="Graphic.color"/> is the fill; <see cref="BorderColor"/> the outline.
    /// </summary>
    public class ChamferPanelGraphic : MaskableGraphic
    {
        [SerializeField] private float chamfer = 14f;
        [SerializeField] private float border = 2f;
        [SerializeField] private Color borderColor = Color.cyan;

        public float Border
        {
            get => border;
            set { border = value; SetVerticesDirty(); }
        }

        public float Chamfer
        {
            get => chamfer;
            set { chamfer = value; SetVerticesDirty(); }
        }

        public Color BorderColor
        {
            get => borderColor;
            set { borderColor = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            Vector2[] outer = Outline(rect, chamfer);
            Rect insetRect = new(rect.x + border, rect.y + border, rect.width - (2f * border), rect.height - (2f * border));
            Vector2[] inner = Outline(insetRect, Mathf.Max(0f, chamfer - (border * 0.414f)));

            Color32 fill = color;
            Color32 edge = borderColor;

            vh.AddVert(insetRect.center, fill, Vector4.zero);
            for (int i = 0; i < inner.Length; i++)
            {
                vh.AddVert(inner[i], fill, Vector4.zero);
            }

            for (int i = 0; i < inner.Length; i++)
            {
                vh.AddTriangle(0, 1 + i, 1 + ((i + 1) % inner.Length));
            }

            if (border <= 0f)
            {
                return;
            }

            for (int i = 0; i < outer.Length; i++)
            {
                int next = (i + 1) % outer.Length;
                int start = vh.currentVertCount;
                vh.AddVert(outer[i], edge, Vector4.zero);
                vh.AddVert(outer[next], edge, Vector4.zero);
                vh.AddVert(inner[next], edge, Vector4.zero);
                vh.AddVert(inner[i], edge, Vector4.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
        }

        private static Vector2[] Outline(Rect r, float c)
        {
            c = Mathf.Min(c, r.width * 0.5f, r.height * 0.5f);
            return new[]
            {
                new Vector2(r.xMin + c, r.yMax),
                new Vector2(r.xMax, r.yMax),
                new Vector2(r.xMax, r.yMin + c),
                new Vector2(r.xMax - c, r.yMin),
                new Vector2(r.xMin, r.yMin),
                new Vector2(r.xMin, r.yMax - c),
            };
        }
    }
}
