#nullable enable
using UnityEngine;
using UnityEngine.UI;
using VirooCaveTemplate.Interactions.Visuals;

namespace VirooCaveTemplate.Scenario
{
    /// <summary>
    /// Point D's bedside monitor: a world-space display built at runtime on the monitor model's
    /// screen, showing ECG / pleth / respiration traces and the four numerics (HR, SpO2, NIBP,
    /// RR) from <see cref="ScenarioState"/>. SpO2 is shown large and, below the alarm threshold,
    /// flashes with an "SpO2 LOW" banner - as described for point D in the scenario. In cardiac
    /// arrest the traces go flat, SpO2/NIBP show no reading and the banner reads ASYSTOLE.
    ///
    /// Numbers come only from the networked scenario state, so every CAVE screen shows the same
    /// values; the trace animation is per-client (cosmetic). Traces are drawn sweep-style like a
    /// real monitor - a fixed trace overwritten by a moving cursor with a short gap ahead of it -
    /// so each sample stays put once drawn instead of shimmering as it scrolls.
    ///
    /// Place this on an object at the centre of the monitor's screen surface, facing out of it
    /// (+Z pointing into the screen, like any world-space canvas).
    /// </summary>
    public class PatientMonitorDisplay : MonoBehaviour
    {
        // Canvas design size; scaled to the physical screen.
        private const float Width = 1000f;
        private const float Height = 720f;

        [SerializeField] private Vector2 screenSizeMetres = new(0.46f, 0.33f);
        [SerializeField] private Font? font;

        [Header("Colours")]
        [SerializeField] private Color background = new(0.01f, 0.02f, 0.03f, 1f);
        [SerializeField] private Color ecgColor = new(0.25f, 1f, 0.35f, 1f);
        [SerializeField] private Color spo2Color = new(0.1f, 0.9f, 1f, 1f);
        [SerializeField] private Color nibpColor = new(0.92f, 0.92f, 0.95f, 1f);
        [SerializeField] private Color respColor = new(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private Color alarmColor = new(1f, 0.18f, 0.15f, 1f);

        private const int TracePoints = 300;
        private const float TraceLeft = 24f;
        private const float TraceWidth = 560f;
        private const float SweepSeconds = 3f;
        private const int GapPoints = 8;

        private readonly Vector2[] samples = new Vector2[TracePoints];

        // Each trace is two polylines: behind the sweep cursor (fresh) and ahead of it (old).
        private PolylineGraphic[] ecgTrace = default!;
        private PolylineGraphic[] plethTrace = default!;
        private PolylineGraphic[] respTrace = default!;
        private Text hrValue = default!;
        private Text spo2Value = default!;
        private Text nibpValue = default!;
        private Text respValue = default!;
        private Image spo2Tile = default!;
        private Image alarmBanner = default!;
        private Text alarmText = default!;

        protected void Awake()
        {
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            Build();
        }

        protected void Update()
        {
            ScenarioState? state = ScenarioState.Instance;
            int hr = state?.HeartRate ?? 0;
            int spo2 = state?.SpO2 ?? 0;
            int rr = state?.RespiratoryRate ?? 0;
            bool arrest = state != null && state.IsArrest;
            bool lowSpO2 = state != null && state.SpO2Alarm;
            bool alarm = arrest || lowSpO2;

            hrValue.text = hr.ToString();
            spo2Value.text = arrest ? "---" : spo2.ToString();
            respValue.text = rr.ToString();
            nibpValue.text = state == null || arrest ? "---/---" : $"{state.Systolic}/{state.Diastolic}";
            alarmText.text = arrest ? "ASYSTOLE" : hr > 120 ? "SpO2 LOW  \u00B7  HR HIGH" : "SpO2 LOW";

            float t = Time.time;
            UpdateTraces(t, hr, rr, arrest);

            // Alarm: banner + SpO2 tile flash, faster in arrest.
            bool on = alarm && Mathf.Repeat(t, arrest ? 0.5f : 1f) < (arrest ? 0.28f : 0.55f);
            alarmBanner.gameObject.SetActive(alarm);
            alarmBanner.color = on ? alarmColor : new Color(alarmColor.r * 0.35f, alarmColor.g * 0.35f, alarmColor.b * 0.35f, 1f);
            alarmText.color = on ? Color.white : alarmColor;
            spo2Tile.color = on && lowSpO2 ? new Color(alarmColor.r, alarmColor.g, alarmColor.b, 0.45f) : new Color(0f, 0f, 0f, 0f);
            spo2Value.color = lowSpO2 && on ? Color.white : spo2Color;
            hrValue.color = arrest && on ? alarmColor : ecgColor;
        }

        private void UpdateTraces(float t, int hr, int rr, bool arrest)
        {
            if (arrest)
            {
                // Asystole: a near-flat line with slight baseline wander; no pulse, no breathing.
                DrawSweep(ecgTrace, t, 530f, 70f, time => 0.025f * Mathf.Sin(time * 2.1f));
                DrawSweep(plethTrace, t, 330f, 70f, _ => -0.35f);
                DrawSweep(respTrace, t, 135f, 35f, _ => 0f);
                return;
            }

            float beat = hr > 0 ? 60f / hr : 1f;
            float breath = rr > 0 ? 60f / rr : 4f;

            DrawSweep(ecgTrace, t, 530f, 70f, time => Ecg(Mathf.Repeat(time, beat) / beat));
            DrawSweep(plethTrace, t, 330f, 70f, time => Pleth(Mathf.Repeat(time - 0.18f, beat) / beat));
            DrawSweep(respTrace, t, 135f, 35f, time => Mathf.Sin(time / breath * Mathf.PI * 2f));
        }

        // Point i always shows the most recent moment the cursor passed it, so samples are stable.
        private void DrawSweep(PolylineGraphic[] trace, float t, float baseline, float amplitude, System.Func<float, float> wave)
        {
            int cursor = Mathf.FloorToInt(Mathf.Repeat(t, SweepSeconds) / SweepSeconds * (TracePoints - 1));

            for (int i = 0; i < TracePoints; i++)
            {
                float offset = i / (float)(TracePoints - 1) * SweepSeconds;
                float sampleTime = t - Mathf.Repeat(t - offset, SweepSeconds);
                samples[i] = new Vector2(TraceLeft + (i / (float)(TracePoints - 1) * TraceWidth), baseline + (wave(sampleTime) * amplitude));
            }

            trace[0].SetPoints(samples, 0, cursor + 1);
            int oldStart = cursor + GapPoints;
            trace[1].SetPoints(samples, oldStart, Mathf.Max(0, TracePoints - oldStart));
        }

        // One heartbeat (phase 0..1): P wave, QRS complex, T wave.
        private static float Ecg(float p) =>
            (0.12f * Bump(p, 0.12f, 0.025f))
            - (0.15f * Bump(p, 0.215f, 0.012f))
            + (1.0f * Bump(p, 0.245f, 0.014f))
            - (0.25f * Bump(p, 0.275f, 0.012f))
            + (0.28f * Bump(p, 0.48f, 0.045f));

        // One pulse (phase 0..1): systolic upstroke and a dicrotic notch.
        private static float Pleth(float p) =>
            (1.0f * Bump(p, 0.22f, 0.07f)) + (0.35f * Bump(p, 0.45f, 0.08f)) - 0.35f;

        private static float Bump(float x, float centre, float width) => Mathf.Exp(-((x - centre) * (x - centre)) / (2f * width * width));

        private void Build()
        {
            var canvasGo = new GameObject("VitalsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.layer = gameObject.layer;
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.SetParent(transform, false);
            canvasRect.sizeDelta = new Vector2(Width, Height);
            canvasRect.localScale = new Vector3(screenSizeMetres.x / Width, screenSizeMetres.y / Height, 1f);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;

            // Everything below is laid out from the bottom-left corner.
            RectTransform root = Rect("Root", canvasRect, Vector2.zero, new Vector2(Width, Height));
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);

            Image bg = AddImage("Background", root, Vector2.zero, new Vector2(Width, Height), background);
            bg.rectTransform.pivot = Vector2.zero;
            bg.rectTransform.anchoredPosition = Vector2.zero;

            // Header
            Label("Header", root, new Vector2(24f, 690f), "ADULT  ·  BED 1", 22, nibpColor * 0.75f, TextAnchor.MiddleLeft, 400f);
            alarmBanner = AddImage("AlarmBanner", root, new Vector2(620f, 672f), new Vector2(360f, 38f), alarmColor);
            alarmText = Label("AlarmText", alarmBanner.rectTransform, new Vector2(0f, 0f), "SpO2 LOW", 24, Color.white, TextAnchor.MiddleCenter, 360f, FontStyle.Bold);
            alarmText.rectTransform.pivot = Vector2.zero;
            alarmText.rectTransform.sizeDelta = new Vector2(360f, 38f);

            // Divider between traces and numerics
            AddImage("Divider", root, new Vector2(600f, 20f), new Vector2(2f, 630f), new Color(1f, 1f, 1f, 0.15f));

            // Traces
            Label("EcgLabel", root, new Vector2(24f, 625f), "II", 20, ecgColor, TextAnchor.MiddleLeft, 100f);
            Label("PlethLabel", root, new Vector2(24f, 420f), "Pleth", 20, spo2Color, TextAnchor.MiddleLeft, 100f);
            Label("RespLabel", root, new Vector2(24f, 215f), "Resp", 20, respColor, TextAnchor.MiddleLeft, 100f);
            ecgTrace = new[] { Trace("EcgTrace", root, ecgColor), Trace("EcgTraceOld", root, ecgColor) };
            plethTrace = new[] { Trace("PlethTrace", root, spo2Color), Trace("PlethTraceOld", root, spo2Color) };
            respTrace = new[] { Trace("RespTrace", root, respColor), Trace("RespTraceOld", root, respColor) };

            // Numerics: HR (top), SpO2 (big, middle), NIBP + RR (bottom)
            Label("HrLabel", root, new Vector2(620f, 625f), "HR  bpm", 20, ecgColor, TextAnchor.MiddleLeft, 200f);
            hrValue = Label("HrValue", root, new Vector2(620f, 520f), "--", 96, ecgColor, TextAnchor.MiddleRight, 360f, FontStyle.Bold);

            spo2Tile = AddImage("Spo2Tile", root, new Vector2(608f, 250f), new Vector2(384f, 215f), new Color(0f, 0f, 0f, 0f));
            Label("Spo2Label", root, new Vector2(620f, 440f), "SpO2  %", 22, spo2Color, TextAnchor.MiddleLeft, 200f);
            spo2Value = Label("Spo2Value", root, new Vector2(620f, 330f), "--", 150, spo2Color, TextAnchor.MiddleRight, 360f, FontStyle.Bold);

            Label("NibpLabel", root, new Vector2(620f, 210f), "NIBP  mmHg", 20, nibpColor, TextAnchor.MiddleLeft, 200f);
            nibpValue = Label("NibpValue", root, new Vector2(620f, 150f), "--/--", 52, nibpColor, TextAnchor.MiddleLeft, 260f, FontStyle.Bold);
            Label("RespNumLabel", root, new Vector2(880f, 210f), "RR", 20, respColor, TextAnchor.MiddleLeft, 100f);
            respValue = Label("RespValue", root, new Vector2(880f, 150f), "--", 52, respColor, TextAnchor.MiddleLeft, 100f, FontStyle.Bold);

            Label("Footer", root, new Vector2(24f, 40f), "NIBP 12:10   ·   ALARMS ON", 18, nibpColor * 0.6f, TextAnchor.MiddleLeft, 500f);
        }

        private PolylineGraphic Trace(string name, RectTransform parent, Color color)
        {
            RectTransform rect = Rect(name, parent, Vector2.zero, Vector2.zero);
            PolylineGraphic trace = rect.gameObject.AddComponent<PolylineGraphic>();
            trace.raycastTarget = false;
            trace.color = color;
            trace.Thickness = 4.5f;
            trace.EndDotSize = 0f;
            return trace;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image AddImage(string name, RectTransform parent, Vector2 position, Vector2 size, Color color)
        {
            Image image = Rect(name, parent, position, size).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        // Positioned by its left edge (or right edge for right-aligned) and vertical centre.
        private Text Label(string name, RectTransform parent, Vector2 position, string content, int size, Color color, TextAnchor alignment, float width, FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = Rect(name, parent, position, new Vector2(width, size * 1.3f));
            rect.pivot = new Vector2(0f, 0.5f);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
