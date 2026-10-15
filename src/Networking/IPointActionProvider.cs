#nullable enable
using System;
using System.Collections.Generic;

namespace VirooCaveTemplate.Networking
{
    /// <summary>
    /// One button in a point-of-interest popup. <see cref="Row"/> groups buttons into rows (0 =
    /// top); <see cref="Active"/> marks the currently chosen option (e.g. the current O2 flow).
    /// </summary>
    public readonly struct PointAction
    {
        public PointAction(string label, Action onSelect, int row = 0, bool interactable = true, bool active = false)
        {
            Label = label;
            OnSelect = onSelect;
            Row = row;
            Interactable = interactable;
            Active = active;
        }

        public string Label { get; }

        public Action OnSelect { get; }

        public int Row { get; }

        public bool Interactable { get; }

        public bool Active { get; }
    }

    /// <summary>
    /// Optional component on an <see cref="InteractionPoint"/> that turns its popup from read-only
    /// into interactive: it can rewrite the popup body (e.g. to show results) and supply action
    /// buttons. Raise <see cref="Changed"/> whenever either would change, and the popup re-renders.
    ///
    /// Actions should only write networked state (e.g. ScenarioState) - the popup, markers and
    /// monitor on every screen then update from that state, not from the button press.
    /// </summary>
    public interface IPointActionProvider
    {
        event EventHandler? Changed;

        string ComposeBody(string staticBody);

        void GetActions(List<PointAction> actions);
    }
}
