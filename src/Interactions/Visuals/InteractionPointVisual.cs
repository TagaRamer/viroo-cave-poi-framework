#nullable enable
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VirooCaveTemplate.Networking;

namespace VirooCaveTemplate.Interactions.Visuals
{
    /// <summary>
    /// Replaces an <see cref="InteractionPoint"/>'s placeholder sphere with a sci-fi hex marker in
    /// the GUI_Sci_FI popup's cyan/navy style:
    ///
    /// - Idle: a slowly turning bracket ring around a pulsing hex core.
    /// - Hover: the marker grows and brightens, a leader line draws itself out from the marker
    ///   and a label (point letter + English/Greek title) slides in at its end.
    /// - Select: a hex shockwave bursts outward from the marker (the popup itself is still opened
    ///   by InteractionPoint).
    /// - Open: while this point's popup is showing (on every screen - networked state), it stays
    ///   highlighted in gold: expanded, with an extra counter-rotating bracket ring, a periodic sonar ping,
    ///   an inverted letter badge and its label pinned open.
    /// - Visited: once a point's popup has been opened (on any screen - driven by the networked
    ///   InteractionPointManager state, so every client agrees), the marker switches to the
    ///   visited colour so trainees can see which points they've already covered.
    ///
    /// Everything is built at runtime as a world-space UI canvas that billboards toward the
    /// viewer and keeps a roughly constant apparent size. Hover is local to whoever is pointing at
    /// the marker; it isn't synchronized.
    ///
    /// Visibility follows the point's own collider, which NetworkedVisibilityToggle switches on
    /// and off together with the renderers - so this never fights the toggle over who owns
    /// renderer.enabled. The original sphere is hidden with forceRenderingOff for the same reason.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractionPoint))]
    public class InteractionPointVisual : MonoBehaviour
    {
        // Canvas design units: the marker's bracket ring is this many units across.
        private const float MarkerUnits = 60f;

        [Header("Colours")]
        [SerializeField] private Color accent = new(0f, 0.97f, 1f, 1f);
        [SerializeField] private Color accentDim = new(0.17f, 0.48f, 0.62f, 1f);
        [SerializeField] private Color visitedColor = new(0.36f, 1f, 0.62f, 1f);
        [Tooltip("Highlight colour of the point whose popup is currently open.")]
        [SerializeField] private Color openColor = new(1f, 0.76f, 0.2f, 1f);
        [SerializeField] private Color panelFill = new(0.02f, 0.06f, 0.11f, 0.9f);

        [Header("Label")]
        [Tooltip("Defaults to Unity's built-in LegacyRuntime font, same as the popup.")]
        [SerializeField] private Font? font;
        [SerializeField] private string hint = "SELECT FOR DETAILS";
        [Tooltip("Hint shown instead while this point's popup is open.")]
        [SerializeField] private string openHint = "VIEWING · SELECT AGAIN TO CLOSE";

        [Header("Size & placement")]
        [Tooltip("Marker diameter in metres when seen from Reference Distance.")]
        [SerializeField] private float markerWorldSize = 0.07f;
        [Tooltip("The marker scales with distance from here so it keeps a similar on-screen size.")]
        [SerializeField] private float referenceDistance = 2f;
        [SerializeField] private Vector2 distanceScaleClamp = new(0.6f, 2.5f);
        [Tooltip("Label size relative to the marker, so text stays readable from across the room.")]
        [SerializeField] private float labelScale = 1.7f;
        [Tooltip("Pulls the marker this far (m) toward the viewer so it isn't swallowed by the surface it's placed on.")]
        [SerializeField] private float pushTowardViewer = 0.05f;

        [Header("Behaviour")]
        [Tooltip("Draw the marker through other geometry - points on the patient otherwise sink into the body mesh.")]
        [SerializeField] private bool markerAlwaysOnTop = true;
        [SerializeField] private bool labelAlwaysOnTop = true;
        [SerializeField] private bool markVisited = true;
        [Tooltip("Label height (canvas units, + up / - down) while this point's popup is open. Hover labels rise up-and-out; the open label sits a little below instead so it stays clear of the popup above the patient.")]
        [SerializeField] private float openLabelRise = -40f;
        [SerializeField] private bool hideSourceRenderer = true;
        [Tooltip("UI material that ignores depth (Materials/POI_UIOverlay, shader VirooCaveTemplate/UI Overlay), used for the 'always on top' options. Referenced here so the shader is included in builds.")]
        [SerializeField] private Material? overlayMaterial;

        private static Camera? viewerCamera;
        private static PointOfInterestPopupUI? popup;

        private InteractionPoint point = default!;
        private XRBaseInteractable interactable = default!;
        private Collider? pointCollider;

        private RectTransform canvasRoot = default!;
        private RectTransform marker = default!;
        private HexagonGraphic glow = default!;
        private HexagonGraphic outerRing = default!;
        private HexagonGraphic innerRing = default!;
        private HexagonGraphic core = default!;
        private HexagonGraphic selectRing = default!;
        private HexagonGraphic burst = default!;
        private CanvasGroup callout = default!;
        private PolylineGraphic line = default!;
        private RectTransform label = default!;
        private ChamferPanelGraphic labelPanel = default!;
        private HexagonGraphic badgeBg = default!;
        private HexagonGraphic badgeRing = default!;
        private Text badgeText = default!;
        private Text titleText = default!;
        private Text hintText = default!;
        private HexagonGraphic progressTrack = default!;
        private HexagonGraphic progressRing = default!;
        private RectTransform chip = default!;
        private CanvasGroup chipGroup = default!;
        private ChamferPanelGraphic chipPanel = default!;
        private Text chipText = default!;

        private string status = "";
        private float progress = -1f;

        private string hintLine = "";
        private string openHintLine = "";

        private float hover;

        // Callout draw-in progress; separate from hover so it can replay without resizing the marker.
        private float reveal;
        private float open;
        private bool isOpen;
        private float side = 1f;
        private float rise = HoverLabelRise;
        private bool wasOpened;
        private float burstTime = float.MaxValue;
        private float burstStrength = 1f;
        private float flash;
        private float ringAngle;
        private float phase;
        private bool visited;

        private const float BurstDuration = 0.5f;
        private const float PingInterval = 1.6f;
        private const float HoverLabelRise = 78f;
        private static readonly Color BadgeDark = new(0.04f, 0.16f, 0.25f, 1f);

        protected void Awake()
        {
            point = GetComponent<InteractionPoint>();
            interactable = GetComponent<XRBaseInteractable>();
            pointCollider = GetComponent<Collider>();
            phase = Mathf.Abs(gameObject.name.GetHashCode() % 1000) * 0.01f;

            if (hideSourceRenderer && TryGetComponent(out MeshRenderer sphere))
            {
                sphere.forceRenderingOff = true;
            }

            Build();
        }

        // Subscribing in Start (not OnEnable/Awake) guarantees InteractionPointManager.Instance is
        // already set, since every Awake in the scene runs before the first Start.
        protected void Start()
        {
            if (InteractionPointManager.Instance != null)
            {
                InteractionPointManager.Instance.ActivePointChanged += OnActivePointChanged;
            }
        }

        protected void OnEnable() => interactable.selectEntered.AddListener(OnSelectEntered);

        protected void OnDisable() => interactable.selectEntered.RemoveListener(OnSelectEntered);

        protected void OnDestroy()
        {
            if (InteractionPointManager.Instance != null)
            {
                InteractionPointManager.Instance.ActivePointChanged -= OnActivePointChanged;
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            burstTime = 0f;
            burstStrength = 1f;
            flash = 1f;
        }

        private void OnActivePointChanged(object sender, string activeId)
        {
            isOpen = activeId == point.PointId;
            if (markVisited && isOpen)
            {
                visited = true;
            }
        }

        protected void LateUpdate()
        {
            Camera? cam = ViewerCamera();
            if (cam == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            bool visible = pointCollider == null || pointCollider.enabled;
            bool opened = visible && isOpen;

            // "hover" drives the expanded marker + callout; an open point stays expanded.
            bool expanded = opened || (visible && interactable.isHovered);

            // Opening/closing moves the label (see rise), so redraw the callout into its new spot.
            if (opened != wasOpened)
            {
                wasOpened = opened;
                reveal = 0f;
            }

            if (expanded && reveal <= 0f)
            {
                ChooseSide(cam, opened);
                rise = opened ? openLabelRise : HoverLabelRise;
            }

            hover = Mathf.MoveTowards(hover, expanded ? 1f : 0f, dt / (expanded ? 0.45f : 0.25f));
            reveal = Mathf.MoveTowards(reveal, expanded ? 1f : 0f, dt / (expanded ? 0.45f : 0.25f));
            open = Mathf.MoveTowards(open, opened ? 1f : 0f, dt / 0.35f);
            flash = Mathf.MoveTowards(flash, 0f, dt / 0.4f);
            burstTime += dt;

            // Sonar ping while open, softer than the click burst.
            if (opened && burstTime > PingInterval)
            {
                burstTime = 0f;
                burstStrength = 0.55f;
            }

            PlaceCanvas(cam);
            AnimateMarker(visible, dt);
            AnimateCallout(visible);
            AnimateBurst();
            AnimateStatus(visible);
        }

        private void PlaceCanvas(Camera cam)
        {
            Vector3 viewer = cam.transform.position;
            Vector3 anchor = transform.position;
            Vector3 toViewer = viewer - anchor;
            float distance = toViewer.magnitude;
            if (distance < 0.01f)
            {
                return;
            }

            Vector3 position = anchor + (toViewer / distance * Mathf.Min(pushTowardViewer, distance * 0.5f));
            canvasRoot.SetPositionAndRotation(position, Quaternion.LookRotation(position - viewer, Vector3.up));

            float distanceScale = Mathf.Clamp(distance / referenceDistance, distanceScaleClamp.x, distanceScaleClamp.y);
            float worldPerUnit = markerWorldSize / MarkerUnits * distanceScale;
            float parentScale = Mathf.Max(transform.lossyScale.x, 1e-5f);
            canvasRoot.localScale = Vector3.one * (worldPerUnit / parentScale);
        }

        private void AnimateMarker(bool visible, float dt)
        {
            marker.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            float t = Time.time + phase;
            float e = Smooth(hover);
            Color baseColor = CurrentColor();

            ringAngle -= dt * Mathf.Lerp(18f, 120f, e);
            outerRing.rectTransform.localEulerAngles = new Vector3(0f, 0f, ringAngle);
            // Visited markers are drawn solid rather than semi-transparent so they read clearly.
            float solid = visited ? 1f - Smooth(open) : 0f;
            outerRing.color = WithAlpha(baseColor, Mathf.Lerp(Mathf.Lerp(0.75f, 1f, e), 1f, solid));
            // Open: brackets close into a solid ring.
            outerRing.CornerFraction = Mathf.Lerp(Mathf.Lerp(0.42f, 0.7f, e), 1f, Smooth(open));

            innerRing.rectTransform.localEulerAngles = new Vector3(0f, 0f, -ringAngle * 0.5f);
            innerRing.color = WithAlpha(baseColor, Mathf.Lerp(Mathf.Lerp(0.55f, 0.95f, e), 0.95f, solid));

            float pulse = 0.5f + (0.5f * Mathf.Sin(t * 2.4f));
            float o = Smooth(open);
            selectRing.gameObject.SetActive(o > 0f);
            if (o > 0f)
            {
                float breathe = 0.5f + (0.5f * Mathf.Sin(t * 3.2f));
                selectRing.rectTransform.localEulerAngles = new Vector3(0f, 0f, -ringAngle * 0.7f);
                selectRing.rectTransform.localScale = Vector3.one * (Mathf.Lerp(1.6f, 1f, o) + (0.06f * breathe));
                selectRing.color = WithAlpha(baseColor, o * Mathf.Lerp(0.7f, 1f, breathe));
            }

            core.color = Color.Lerp(baseColor, Color.white, Mathf.Max(e * 0.55f * (1f - o), flash, o * 0.35f));
            core.rectTransform.localScale = Vector3.one * (1f + (0.12f * pulse) + (0.25f * e));

            glow.color = WithAlpha(baseColor, 0.08f + (0.07f * pulse) + (0.17f * e) + (0.12f * o) + (0.14f * solid) + (0.3f * flash));
            glow.rectTransform.localScale = Vector3.one * (1f + (0.1f * pulse) + (0.3f * e));

            marker.localScale = Vector3.one * (1f + (0.3f * e) + (0.15f * flash));
        }

        private void AnimateCallout(bool visible)
        {
            bool show = visible && reveal > 0f;
            callout.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            // Line draws first, then the label fades/slides in at its end.
            float lineProgress = Smooth(Mathf.InverseLerp(0f, 0.55f, reveal));
            float labelIn = Smooth(Mathf.InverseLerp(0.45f, 1f, reveal));

            // 45-degree spoke out of the marker, then horizontal into the label.
            float up = Mathf.Sign(rise);
            float spoke = Mathf.Max(Mathf.Abs(rise), MarkerUnits * 0.5f * 1.3f + 12f);
            Vector2 dir = new Vector2(side, up).normalized;
            Vector2 start = dir * (MarkerUnits * 0.5f * 1.3f + 6f);
            Vector2 elbow = new(side * spoke, rise);
            Vector2 end = new(side * (spoke + 50f), rise);
            line.SetPoints(start, elbow, end);
            line.Progress = lineProgress;

            Color baseColor = CurrentColor();
            float o = Smooth(open);
            line.color = baseColor;
            labelPanel.BorderColor = baseColor;
            badgeRing.color = baseColor;
            titleText.color = baseColor;
            hintText.color = WithAlpha(baseColor, Mathf.Lerp(0.6f, 0.95f, o));
            hintText.text = isOpen ? openHintLine : hintLine;

            // Open: the letter badge inverts to a solid accent hex with a dark letter.
            badgeBg.color = Color.Lerp(BadgeDark, baseColor, o);
            badgeText.color = Color.Lerp(baseColor, BadgeDark, o);

            label.pivot = new Vector2(side > 0f ? 0f : 1f, 0.5f);
            label.anchoredPosition = end + new Vector2(side * (2f + ((1f - labelIn) * 14f)), 0f);
            callout.alpha = 1f;
            label.GetComponent<CanvasGroup>().alpha = labelIn;
        }

        private void AnimateBurst()
        {
            // Click bursts are quick and strong; open-state pings slower and softer.
            float duration = burstStrength >= 1f ? BurstDuration : BurstDuration * 2f;
            float s = burstTime / duration;
            bool active = s < 1f;
            burst.gameObject.SetActive(active);
            if (!active)
            {
                return;
            }

            float eased = 1f - ((1f - s) * (1f - s) * (1f - s));
            burst.rectTransform.localScale = Vector3.one * (1f + (2.2f * eased));
            burst.Thickness = Mathf.Lerp(6f, 1f, s) * burstStrength;
            burst.color = WithAlpha(CurrentColor(), (1f - s) * (1f - s) * burstStrength);
        }

        // An open point is always gold; the visited colour only applies once closed. The fade goes
        // via white, since a direct cyan->gold blend passes through a green that reads as "visited".
        private Color CurrentColor()
        {
            Color from = visited ? visitedColor : accent;
            float o = Smooth(open);
            return o < 0.5f ? Color.Lerp(from, Color.white, o * 2f) : Color.Lerp(Color.white, openColor, (o * 2f) - 1f);
        }

        // Put the label on the side facing the middle of the view so it doesn't run off-screen -
        // or, while open, on the side away from the popup so the popup doesn't cover it.
        private void ChooseSide(Camera cam, bool opened)
        {
            Vector3 offset = transform.position - cam.transform.position;
            float lateral = Vector3.Dot(cam.transform.right, offset);
            side = lateral > offset.magnitude * 0.3f ? -1f : 1f;

            if (opened)
            {
                if (popup == null)
                {
                    popup = FindFirstObjectByType<PointOfInterestPopupUI>(FindObjectsInactive.Include);
                }

                if (popup != null)
                {
                    float fromPopup = Vector3.Dot(cam.transform.right, transform.position - popup.transform.position);
                    if (Mathf.Abs(fromPopup) > 0.1f)
                    {
                        side = Mathf.Sign(fromPopup);
                    }
                }
            }
        }

        private void Build()
        {
            // Not ??= - an unassigned serialized field is Unity's fake null, not a C# null.
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            var canvasGo = new GameObject("POI_Visual", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.layer = gameObject.layer;
            canvasRoot = (RectTransform)canvasGo.transform;
            canvasRoot.SetParent(transform, false);
            canvasRoot.sizeDelta = Vector2.zero;
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 6f;

            Material? markerMaterial = markerAlwaysOnTop ? overlayMaterial : null;
            Material? labelMaterial = labelAlwaysOnTop ? overlayMaterial : null;

            // Marker
            marker = NewRect("Marker", canvasRoot, Vector2.zero);
            glow = NewHex("Glow", marker, MarkerUnits * 1.45f, 0f, 0f, markerMaterial);
            outerRing = NewHex("OuterRing", marker, MarkerUnits, 3f, 0.42f, markerMaterial);
            innerRing = NewHex("InnerRing", marker, MarkerUnits * 0.6f, 2.5f, 0f, markerMaterial);
            core = NewHex("Core", marker, MarkerUnits * 0.3f, 0f, 0f, markerMaterial);
            selectRing = NewHex("SelectRing", marker, MarkerUnits * 1.55f, 2f, 0.3f, markerMaterial);
            selectRing.gameObject.SetActive(false);
            progressTrack = NewHex("ProgressTrack", marker, MarkerUnits * 1.25f, 4f, 0f, markerMaterial);
            progressRing = NewHex("ProgressRing", marker, MarkerUnits * 1.25f, 4f, 0f, markerMaterial);
            progressTrack.gameObject.SetActive(false);
            progressRing.gameObject.SetActive(false);

            burst = NewHex("Burst", canvasRoot, MarkerUnits, 4f, 0f, markerMaterial);
            burst.gameObject.SetActive(false);

            // Callout: leader line + label
            RectTransform calloutRect = NewRect("Callout", canvasRoot, Vector2.zero);
            callout = calloutRect.gameObject.AddComponent<CanvasGroup>();

            line = NewGraphic<PolylineGraphic>("Line", calloutRect, Vector2.zero, labelMaterial);

            (string letter, string primary, string secondary) = SplitTitle(point.PointId, point.Title);

            label = NewRect("Label", calloutRect, Vector2.zero);
            label.gameObject.AddComponent<CanvasGroup>();

            const float pad = 12f, badge = 46f, height = 84f;
            float textX = pad + badge + 12f;

            labelPanel = NewGraphic<ChamferPanelGraphic>("Panel", label, Vector2.zero, labelMaterial);
            labelPanel.color = panelFill;

            badgeBg = NewHex("BadgeBg", label, badge, 0f, 0f, labelMaterial);
            badgeBg.color = BadgeDark;
            badgeRing = NewHex("BadgeRing", label, badge, 2.5f, 0f, labelMaterial);
            badgeText = NewText("BadgeText", label, letter, 24, FontStyle.Bold, TextAnchor.MiddleCenter, labelMaterial);

            titleText = NewText("Title", label, primary, 20, FontStyle.Bold, TextAnchor.MiddleLeft, labelMaterial);
            Text subText = NewText("Subtitle", label, secondary, 16, FontStyle.Normal, TextAnchor.MiddleLeft, labelMaterial);
            subText.color = accentDim;
            hintLine = $"› {hint}";
            openHintLine = $"› {openHint}";
            hintText = NewText("Hint", label, openHintLine, 12, FontStyle.Normal, TextAnchor.MiddleLeft, labelMaterial);
            float hintWidth = hintText.preferredWidth;
            hintText.text = hintLine;
            hintWidth = Mathf.Max(hintWidth, hintText.preferredWidth);

            float textWidth = Mathf.Max(titleText.preferredWidth, subText.preferredWidth, hintWidth);
            float width = textX + textWidth + pad + 8f;

            label.sizeDelta = new Vector2(width, height);
            label.localScale = Vector3.one * labelScale;
            Stretch(labelPanel.rectTransform);
            PlaceLeft(badgeBg.rectTransform, pad, 0f, badge, badge);
            PlaceLeft(badgeRing.rectTransform, pad, 0f, badge, badge);
            PlaceLeft(badgeText.rectTransform, pad, 0f, badge, badge);

            bool hasSubtitle = !string.IsNullOrEmpty(secondary);
            subText.gameObject.SetActive(hasSubtitle);
            PlaceLeft(titleText.rectTransform, textX, hasSubtitle ? 17f : 9f, textWidth + 8f, 26f);
            PlaceLeft(subText.rectTransform, textX, -4f, textWidth + 8f, 20f);
            PlaceLeft(hintText.rectTransform, textX, hasSubtitle ? -24f : -14f, textWidth + 8f, 16f);

            callout.gameObject.SetActive(false);

            // Status chip under the marker (e.g. current O2 flow), set by the point's scenario logic.
            chip = NewRect("StatusChip", canvasRoot, Vector2.zero);
            chip.pivot = new Vector2(0.5f, 1f);
            chip.anchoredPosition = new Vector2(0f, -(MarkerUnits * 0.5f * 1.3f) - 8f);
            chip.localScale = Vector3.one * (labelScale * 0.8f);
            chipGroup = chip.gameObject.AddComponent<CanvasGroup>();
            chipPanel = NewGraphic<ChamferPanelGraphic>("Panel", chip, Vector2.zero, labelMaterial);
            chipPanel.color = panelFill;
            Stretch(chipPanel.rectTransform);
            chipText = NewText("Text", chip, "", 14, FontStyle.Bold, TextAnchor.MiddleCenter, labelMaterial);
            Stretch(chipText.rectTransform);
            chip.gameObject.SetActive(false);
        }

        // "A - Πρόσωπο ασθενούς / Patient's Face" -> ("A", "PATIENT'S FACE", "Πρόσωπο ασθενούς")
        private static (string letter, string primary, string secondary) SplitTitle(string id, string title)
        {
            string rest = title;
            int dash = title.IndexOf(" - ", System.StringComparison.Ordinal);
            if (dash >= 0)
            {
                rest = title[(dash + 3)..];
            }

            string primary = rest, secondary = "";
            int slash = rest.LastIndexOf(" / ", System.StringComparison.Ordinal);
            if (slash >= 0)
            {
                secondary = rest[..slash];
                primary = rest[(slash + 3)..];
            }

            string letter = string.IsNullOrEmpty(id) ? "?" : id;
            return (letter, primary.ToUpperInvariant(), secondary);
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            return rect;
        }

        private static T NewGraphic<T>(string name, Transform parent, Vector2 size, Material? material) where T : Graphic
        {
            RectTransform rect = NewRect(name, parent, size);
            rect.gameObject.AddComponent<CanvasRenderer>();
            T graphic = rect.gameObject.AddComponent<T>();
            graphic.raycastTarget = false;
            if (material != null)
            {
                graphic.material = material;
            }

            return graphic;
        }

        private HexagonGraphic NewHex(string name, Transform parent, float size, float thickness, float cornerFraction, Material? material)
        {
            HexagonGraphic hex = NewGraphic<HexagonGraphic>(name, parent, new Vector2(size, size), material);
            hex.Thickness = thickness;
            hex.CornerFraction = cornerFraction;
            hex.color = accent;
            return hex;
        }

        private Text NewText(string name, Transform parent, string content, int size, FontStyle style, TextAnchor alignment, Material? material)
        {
            Text text = NewGraphic<Text>(name, parent, Vector2.zero, material);
            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.color = accent;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // Positions a child by its left edge (x from the label's left) and vertical centre offset.
        private static void PlaceLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Camera? ViewerCamera()
        {
            if (viewerCamera != null && viewerCamera.isActiveAndEnabled)
            {
                return viewerCamera;
            }

            viewerCamera = Camera.main;
            if (viewerCamera == null)
            {
                foreach (Camera candidate in Camera.allCameras)
                {
                    viewerCamera = candidate;
                    break;
                }
            }

            return viewerCamera;
        }

        /// <summary>Short text shown in a chip under the marker; empty hides it.</summary>
        public void SetStatus(string? text)
        {
            status = text ?? "";
            chipText.text = status;
            chip.sizeDelta = new Vector2(chipText.preferredWidth + 28f, 26f);
        }

        /// <summary>0..1 shows a progress ring around the marker; negative hides it.</summary>
        public void SetProgress(float value) => progress = value;

        // The chip hides while the callout label is out, which carries the same information.
        private void AnimateStatus(bool visible)
        {
            bool showChip = visible && status.Length > 0 && reveal < 1f;
            chip.gameObject.SetActive(showChip);
            if (showChip)
            {
                Color baseColor = CurrentColor();
                chipGroup.alpha = 1f - Smooth(reveal);
                chipPanel.BorderColor = baseColor;
                chipText.color = baseColor;
            }

            bool showProgress = visible && progress >= 0f;
            progressTrack.gameObject.SetActive(showProgress);
            progressRing.gameObject.SetActive(showProgress);
            if (showProgress)
            {
                Color ringColor = Color.Lerp(CurrentColor(), Color.white, 0.35f);
                progressTrack.color = WithAlpha(ringColor, 0.2f);
                progressRing.color = ringColor;
                progressRing.FillAmount = progress;
            }
        }

        private static float Smooth(float x) => x * x * (3f - (2f * x));

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
    }
}
