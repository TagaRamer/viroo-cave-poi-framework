#nullable enable
using UnityEngine;
using UnityEngine.UI;
using VirooCaveTemplate.Interactions.Visuals;
using VirooCaveTemplate.Networking;

namespace VirooCaveTemplate.Scenario
{
    /// <summary>
    /// End-of-scenario banner, shown above the bed on every screen once the clinical course in
    /// <see cref="ScenarioState"/> reaches an outcome: green when the patient has recovered after
    /// methylene blue, red after a cardiac arrest (delayed a few seconds so the flat line on the
    /// monitor is seen first). Each carries a one-line debrief - the diagnosis and its treatment.
    /// Built at runtime in the same sci-fi panel style as the point-of-interest labels; billboards
    /// toward the viewer, and closes any open popup since it sits in the same place.
    /// </summary>
    public class ScenarioOutcomeBanner : MonoBehaviour
    {
        private const float Width = 1200f;
        private const float Height = 240f;

        [SerializeField] private float widthMetres = 2.3f;
        [SerializeField] private float arrestDelaySeconds = 4f;
        [SerializeField] private Material? overlayMaterial;
        [SerializeField] private Font? font;
        [SerializeField] private Color successColor = new(0.36f, 1f, 0.62f, 1f);
        [SerializeField] private Color failureColor = new(1f, 0.3f, 0.25f, 1f);
        [SerializeField] private Color panelFill = new(0.02f, 0.06f, 0.11f, 0.94f);

        private RectTransform root = default!;
        private CanvasGroup group = default!;
        private ChamferPanelGraphic panel = default!;
        private Text title = default!;
        private Text subtitle = default!;
        private Text debrief = default!;
        private float shownAt = -1f;
        private ClinicalCourse shownFor = ClinicalCourse.Stable;

        protected void Awake()
        {
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            Build();
        }

        protected void LateUpdate()
        {
            ScenarioState? state = ScenarioState.Instance;
            ClinicalCourse course = state?.Course ?? ClinicalCourse.Stable;
            bool outcome = course is ClinicalCourse.Recovered or ClinicalCourse.Arrest;

            if (course != shownFor)
            {
                shownFor = course;
                shownAt = outcome ? Time.time + (course == ClinicalCourse.Arrest ? arrestDelaySeconds : 0f) : -1f;
                if (outcome && state != null)
                {
                    Fill(course, state);
                }
            }

            bool visible = outcome && Time.time >= shownAt;
            if (visible && !root.gameObject.activeSelf)
            {
                // The banner takes the popup's place in front of the patient. Closing is
                // idempotent, so it doesn't matter that every client does it.
                InteractionPointManager.Instance?.Close();
            }

            root.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            float t = Mathf.Clamp01((Time.time - shownAt) / 0.6f);
            group.alpha = t;
            root.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, 1f - ((1f - t) * (1f - t)));

            Camera? cam = Camera.main;
            if (cam != null)
            {
                Vector3 away = transform.position - cam.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f)
                {
                    transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                }
            }
        }

        private void Fill(ClinicalCourse course, ScenarioState state)
        {
            bool success = course == ClinicalCourse.Recovered;
            Color color = success ? successColor : failureColor;
            panel.BorderColor = color;
            title.color = color;

            if (success)
            {
                title.text = "Ο ΑΣΘΕΝΗΣ ΣΤΑΘΕΡΟΠΟΙΗΘΗΚΕ";
                subtitle.text = $"SpO2 {state.SpO2}%  ·  Ανταπόκριση σε methylene blue 1 mg/kg IV";
            }
            else
            {
                title.text = "ΚΑΡΔΙΑΚΗ ΑΝΑΚΟΠΗ";
                subtitle.text = "Η αιτία της υποξαιμίας δεν αναγνωρίστηκε έγκαιρα";
            }

            debrief.text = $"Διάγνωση: Μεθαιμοσφαιριναιμία (MetHb {state.MetHbPercent}%)  ·  Θεραπεία: Methylene blue 1–2 mg/kg IV";
        }

        private void Build()
        {
            var canvasGo = new GameObject("OutcomeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.layer = gameObject.layer;
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.SetParent(transform, false);
            canvasRect.sizeDelta = new Vector2(Width, Height);
            canvasRect.localScale = Vector3.one * (widthMetres / Width);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;

            root = NewRect("Root", canvasRect);
            root.sizeDelta = new Vector2(Width, Height);
            group = root.gameObject.AddComponent<CanvasGroup>();

            panel = NewRect("Panel", root).gameObject.AddComponent<ChamferPanelGraphic>();
            Stretch(panel.rectTransform);
            panel.color = panelFill;
            panel.Border = 5f;
            panel.Chamfer = 34f;
            panel.raycastTarget = false;
            if (overlayMaterial != null)
            {
                panel.material = overlayMaterial;
            }

            title = NewText("Title", root, 50, FontStyle.Bold, 64f);
            subtitle = NewText("Subtitle", root, 26, FontStyle.Normal, -2f);
            debrief = NewText("Debrief", root, 22, FontStyle.Normal, -64f);
            subtitle.color = new Color(0.85f, 0.92f, 1f, 1f);
            debrief.color = new Color(0.17f, 0.75f, 0.9f, 1f);

            root.gameObject.SetActive(false);
        }

        private Text NewText(string name, RectTransform parent, int size, FontStyle style, float y)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(Width - 40f, size * 1.4f);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            if (overlayMaterial != null)
            {
                text.material = overlayMaterial;
            }

            return text;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
