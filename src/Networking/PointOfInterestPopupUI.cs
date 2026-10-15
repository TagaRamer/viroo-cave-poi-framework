#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VirooCaveTemplate.Interactions;

namespace VirooCaveTemplate.Networking
{
    /// <summary>
    /// Displays whichever InteractionPoint is currently open, according to
    /// InteractionPointManager, in a world-space popup panel positioned near that point. Hides
    /// itself when no point is open. One instance per scene, shared by every InteractionPoint -
    /// not one popup per point.
    ///
    /// If the open point has an <see cref="IPointActionProvider"/>, the popup also shows its
    /// action buttons (in rows above the OK button, shrinking the text area to make room) and its
    /// dynamic body text, and re-renders whenever the provider reports a change - so e.g. the O2
    /// panel updates on every screen as soon as anyone changes the flow.
    /// </summary>
    public class PointOfInterestPopupUI : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot = default!;
        [SerializeField] private Text contentText = default!;
        [Tooltip("Where the popup is shown - e.g. a point fixed above the patient. Every point's popup appears here, regardless of which point was clicked.")]
        [SerializeField] private Transform displayAnchor = default!;

        [Header("Actions")]
        [Tooltip("Pre-built button slots; filled from the open point's IPointActionProvider, unused ones hidden.")]
        [SerializeField] private PopupActionButton[] actionButtons = Array.Empty<PopupActionButton>();
        [Tooltip("Vertical centre of the lowest button row, in panel units (the OK button's top edge is at about -162).")]
        [SerializeField] private float lowestRowY = -128f;
        [SerializeField] private float rowHeight = 64f;
        [SerializeField] private float buttonSpacing = 14f;

        private readonly List<PointAction> actions = new();
        private InteractionPoint? current;
        private IPointActionProvider? provider;
        private RectTransform textRect = default!;
        private float textTop;
        private float textBottom;

        protected void Awake()
        {
            textRect = contentText.rectTransform;
            textTop = textRect.anchoredPosition.y + (textRect.sizeDelta.y * 0.5f);
            textBottom = textRect.anchoredPosition.y - (textRect.sizeDelta.y * 0.5f);
            contentText.supportRichText = true;

            // Shrinks long content (e.g. a results report above two button rows) to fit the
            // text area rather than letting it run under the buttons.
            contentText.resizeTextForBestFit = true;
            contentText.resizeTextMaxSize = contentText.fontSize;
            contentText.resizeTextMinSize = Mathf.Min(14, contentText.fontSize);
            contentText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // Subscribing in Start (not OnEnable/Awake) guarantees InteractionPointManager.Instance is
        // already set, since every Awake in the scene runs before the first Start.
        protected void Start()
        {
            panelRoot.SetActive(false);

            if (InteractionPointManager.Instance != null)
            {
                InteractionPointManager.Instance.ActivePointChanged += OnActivePointChanged;
            }
        }

        protected void OnDestroy()
        {
            SetProvider(null);

            if (InteractionPointManager.Instance != null)
            {
                InteractionPointManager.Instance.ActivePointChanged -= OnActivePointChanged;
            }
        }

        private void OnActivePointChanged(object sender, string id)
        {
            if (string.IsNullOrEmpty(id)
                || InteractionPointManager.Instance == null
                || !InteractionPointManager.Instance.TryGetPoint(id, out InteractionPoint? point)
                || point == null)
            {
                current = null;
                SetProvider(null);
                panelRoot.SetActive(false);
                return;
            }

            current = point;
            SetProvider(point.GetComponent<IPointActionProvider>());
            transform.position = displayAnchor.position;
            panelRoot.SetActive(true);
            Render();
        }

        private void SetProvider(IPointActionProvider? next)
        {
            if (provider != null)
            {
                provider.Changed -= OnProviderChanged;
            }

            provider = next;

            if (provider != null)
            {
                provider.Changed += OnProviderChanged;
            }
        }

        private void OnProviderChanged(object sender, EventArgs e)
        {
            if (current != null)
            {
                Render();
            }
        }

        private void Render()
        {
            if (current == null)
            {
                return;
            }

            string body = provider?.ComposeBody(current.Body) ?? current.Body;
            contentText.text = $"<b>{current.Title}</b>\n\n{body}";

            actions.Clear();
            provider?.GetActions(actions);
            int rows = LayoutButtons();

            // Text fills the space between its original top and the top button row.
            float bottom = rows > 0 ? lowestRowY + ((rows - 0.5f) * rowHeight) + 6f : textBottom;
            textRect.sizeDelta = new Vector2(textRect.sizeDelta.x, textTop - bottom);
            textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, (textTop + bottom) * 0.5f);
        }

        // Returns the number of rows used.
        private int LayoutButtons()
        {
            int rows = 0;
            foreach (PointAction action in actions)
            {
                rows = Mathf.Max(rows, action.Row + 1);
            }

            int slot = 0;
            for (int row = 0; row < rows; row++)
            {
                int count = 0;
                foreach (PointAction action in actions)
                {
                    if (action.Row == row)
                    {
                        count++;
                    }
                }

                float y = lowestRowY + ((rows - 1 - row) * rowHeight);
                int column = 0;
                for (int i = 0; i < actions.Count && slot < actionButtons.Length; i++)
                {
                    if (actions[i].Row != row)
                    {
                        continue;
                    }

                    PopupActionButton button = actionButtons[slot++];
                    float width = button.Root.sizeDelta.x;
                    float x = (column++ - ((count - 1) * 0.5f)) * (width + buttonSpacing);
                    button.Root.anchoredPosition = new Vector2(x, y);

                    PointAction action = actions[i];
                    button.Configure(action, action.OnSelect);
                }
            }

            for (; slot < actionButtons.Length; slot++)
            {
                actionButtons[slot].Hide();
            }

            return rows;
        }
    }
}
