# Interactions & CAVE Synchronization

How grab/ray interactions and cross-screen sync work in this project, and how to reuse the
pattern for new objects. Written after building the two example cubes in `Assets/Scenes/Viroo
Studio.unity`: `GrabbableCube` (hand grab) and `RaySelectCube` (ray-select from a distance,
disappears on click).

## Mental model: one session, many screens, one leader

Every CAVE wall runs its own instance of the VIROO Player, and every instance connects to the
same VIROO session as its own client. There is no separate "server" project to build — the
first client to connect a given session automatically becomes the **leader**
(`Virtualware.Networking.TaskRunner.Conditional.ConditionalRunner.IsLeader`, exposed as
`VirooApi.Instance.Networking().ConditionalRunner`). This is what the CAVE setup calls the
"main user" — it's automatic, not something you configure per screen.

Two networked primitives cover almost everything:

- **`NetworkObject`** (`Virtualware.Networking.Client.NetworkObject`) + **`NetworkTransform`** -
  gives a GameObject a stable network identity and syncs its position/rotation/scale. Required
  for anything a user grabs and moves.
- **`NetworkVariable<T>`** (`Virtualware.Networking.Client.Variables`) - a generic, network-synced
  value with `.Value`, `OnValueChanged`, `OnInitialized`. Created via
  `VirooApi.Instance.Networking().CreateNetworkVariable(id, defaultValue)`. This is the right tool
  for *any* other piece of shared state (a score, a timer, whether something is visible) - it does
  **not** require a `NetworkObject` on the same GameObject.

`VirooApi.Instance` finishes initializing asynchronously. Any script that touches
`.Networking()` must wait for `VirooApi.Instance.OnInitialized` first (see both components below,
and VIROO's own `Score.cs` sample in `com.viroo.studio`'s `VirooLab` sample).

## Example 1 - GrabbableCube (near/direct grab)

Straight instance of VIROO's own ready-made prefab -
`Packages/com.viroo/Runtime/Viroo.Unity/Interactions/XRInteractionToolKit/Prefabs/GrabbableCube.prefab`.
No custom code. Its component stack is the reference recipe for *any* grabbable networked object:

| Component | Role |
|---|---|
| `XRGrabInteractable` (stock XRI) | Physical grab/throw behaviour |
| `NetworkObject` | Network identity + authority (`objectId` must be unique per object) |
| `VirooXRGrabInteractable` (+ its `Forwarder`) | Bridges XRI grab events into VIROO's authority/network system; exposes `allowDirectInteraction` / `allowRayInteraction` / `allowDistanceSelectInteraction` toggles |
| `NetworkTransformSettings` + `NetworkTransform` | Position/rotation sync tuning and sync itself |

`NetworkObject` itself now assigns authority automatically (first-connected/leader client claims
ownership of unowned objects, `SetAuthorityIfFirst` in `NetworkObject.cs`). The prefab used to also
carry a `NetworkAuthorityEnsurer` component for this, but that component is `[Obsolete]` as of the
installed `com.virtualware.networking` version - removed from both example cubes here. Don't
re-add it to new objects; `NetworkObject` alone is sufficient.

To add another grabbable object: instantiate this same prefab, swap the mesh, and set a unique
`NetworkObject.objectId`. That's it - full grab + cross-screen sync for free.

## Example 2 - RaySelectCube (ray-select from a distance, disappears on click)

Same base prefab as above (for the proven interaction stack and default **white** ray-line
visual - `Packages/com.viroo/Runtime/Viroo.Unity/Interactions/Materials/LineVisual.mat`, which is
what every VIROO ray interaction already uses, UI included), but:

- `VirooXRGrabInteractable.allowDirectInteraction = false` - can't be picked up by hand, only
  selected at a distance via ray (`allowRayInteraction` / `allowDistanceSelectInteraction` stay
  `true`).
- Two extra components, both generic and reusable (see below).

### `NetworkedVisibilityToggle` (`Assets/Scripts/Networking/NetworkedVisibilityToggle.cs`)

Generic building block: keeps a bool "visible" state synced to every connected screen via a
`NetworkVariable<bool>`, and applies it by enabling/disabling this object's `Renderer`s and
`Collider`s - **not** `GameObject.SetActive`, because deactivating the object would also disable
this component and drop its network subscription, so it could never come back. Public API:
`Show()`, `Hide()`, `Toggle()`.

Not tied to XRI at all - use it for any object whose visibility should be shared, from any
trigger (a UI button, a timer, a game-logic event).

Inspector fields:
- `networkId` - unique id for this object's synced state (auto-derived if left blank; give
  objects a real name if you'll ever want to inspect/debug the underlying variable).
- `startsVisible` - default state before the first sync.
- `leaderOnly` - when true (default), only the session leader's `Show`/`Hide`/`Toggle` calls
  actually write the new value (via `ConditionalRunner.RunIfLeader`); other clients' calls are
  ignored. Prevents two screens racing to write conflicting values for the same click. Turn this
  off only if you specifically want every client able to write independently.

### `HideOnSelect` (`Assets/Scripts/Interactions/HideOnSelect.cs`)

Thin adapter: requires a sibling `XRBaseInteractable` and `NetworkedVisibilityToggle`, and calls
`Hide()` on `selectEntered`. This is deliberately separate from `NetworkedVisibilityToggle` so the
sync primitive stays reusable - to trigger `Show()`/`Toggle()` instead, or from a different event
entirely (a button press, a trigger volume), write a similarly small adapter rather than editing
`NetworkedVisibilityToggle`.

### `NetworkedAnimatorSync` (`Assets/Scripts/Networking/NetworkedAnimatorSync.cs`)

Generic building block: keeps a looping `Animator` state (breathing, a spinning fan, any idle
loop) in phase across every connected screen. Without this, each CAVE screen runs its own local
`Update` loop and the same animation drifts out of sync between walls - visible as, e.g., a
patient's chest rising at a different moment on each screen.

The session leader periodically (`resyncInterval`, default 5s) broadcasts its own Animator's
`normalizedTime` for a given layer; every client (leader included) snaps its local Animator to
that position on receipt via `Animator.Play(stateHash, layer, normalizedTime)`. Re-syncing on an
interval rather than once at startup also corrects the slow drift that accumulates from
per-client frame-rate differences over a long session.

Requires the Animator to already be sitting in the state it should loop (its default state) when
`VirooApi.Instance.OnInitialized` fires - fine for a simple idle-loop Animator Controller with one
state on the base layer, which is the common case this is meant for. Not a general state-machine
transition sync.

Usage: add the component next to any `Animator` you want kept in phase across screens. No other
setup needed - same drop-in pattern as `NetworkedVisibilityToggle`.

### Networked one-shot triggers (see `LightFlicker.cs`)

For a *discrete* shared event (not a continuous loop) - a light flicker, a door chime, anything
that should happen at the same moment on every screen rather than each screen rolling its own
timer - the pattern is: the leader owns a `NetworkVariable<int>` counter, increments it whenever
the event should fire (`RunIfLeader` + its own local random/scheduled logic), and every client's
`OnValueChanged` handler plays the local one-shot effect in response. The counter's actual value
doesn't matter, only the fact that it changed - `LightFlicker` is a full worked example of this
pattern (it also shows how to skip the initial sync delivery, which is the starting baseline, not
a real trigger).

## Recipe for a new networked interactable

1. Instantiate `GrabbableCube.prefab` (or build the same component stack by hand for a non-cube
   mesh) and give its `NetworkObject.objectId` a unique value.
2. Decide direct vs. ray vs. distance interaction via the three `allowXInteraction` flags on
   `VirooXRGrabInteractable`.
3. For any shared state beyond position (visible/not, on/off, a counter, ...): add a small
   component using `VirooApi.Instance.Networking().CreateNetworkVariable<T>(...)` following the
   `NetworkedVisibilityToggle` pattern - wait for `VirooApi.Instance.OnInitialized`, keep the
   `NetworkVariable<T>` reference, apply its value via `OnValueChanged`/`OnInitialized`, gate
   writes through `RunIfLeader` unless every client should be able to write independently.
4. Wire the trigger (interaction event, UI, timer, ...) to that component's public methods via a
   small adapter component, the same way `HideOnSelect` wires `selectEntered` to
   `NetworkedVisibilityToggle.Hide()`.

## Known local limitation

VIROO's Dashboard "Application Builder" (publish-to-VIROO-Cloud flow) is a separate concern from
everything above and needs a VIROO Cloud account/login - not required for local Editor testing,
which is what this document covers.
