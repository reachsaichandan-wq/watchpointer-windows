# Controls

How every control works, and how it maps to the requested control scheme. Everything marked *profile* can be
changed per profile on the PC (**Gestures** page); motion gestures can be switched on/off on the watch
(**Settings › Gestures**). Pointing keeps tracking while you use buttons; only Pause, a drag, or lowering the watch
stop the cursor.

## 1. Air mouse

| On the watch | On the PC |
|---|---|
| Point left / right / up / down / diagonally | Cursor follows (laser pointing; or relative air-mouse mode, Pointer settings) |
| Small vs large wrist movement | Fine vs fast movement (relative mode adds acceleration; absolute is proportional) |
| Stop moving | Cursor stops (steady thanks to the 1€ filter) |
| Tap the pad | Left click, placed where you pointed at touch-down *(profile)* |
| Tap the pad twice | Double click |
| Touch and hold the pad, move, release | Drag and drop *(profile)* |
| Digital Crown | Scroll up / down *(profile: scroll, horizontal, zoom, volume, slides)* |
| ◎ Center button | Recenter |
| ⏸ Pause button | Freeze / resume cursor |

## 2. Motion gestures

| Gesture | Default action (Desktop) | Default state | Notes |
|---|---|---|---|
| **Shake** (quick back-and-forth) | Escape | **On** | Needs ≥4 alternating >1 g peaks within ~0.7 s; pointing never does this |
| **Pinch twice** (watchOS Double Tap; Series 9 / Ultra 2 and later, incl. Series 11) | Play / pause | On (system) | Watch Settings › Gestures › Double Tap must be on. This is the "two quick taps" gesture; tapping the *screen* twice stays double-click |
| **Flick left / right** (sharp turn and back) | Previous / next browser tab | Off | Spike >340°/s lasting <0.2 s; turn on in watch Settings |
| **Twist clockwise / anticlockwise** (roll past 35°, hold to repeat) | Volume up / down | Off | Rolling doesn't move the pointer |
| **Lower the watch** (point at the floor ~0.4 s) | Pause pointer | **On** | Raise to resume. Shows "Lowered" on both sides |
| Raise the watch | Resume pointer | — | Part of the same detector |
| Hold steady 2 s | Precision mode | Off | PC: Pointer settings › *Auto precision*; leaves precision on a fast move |
| Swipe left / right / up / down on the pad | Next / previous desktop, Task View, Show desktop | On | *(profile)* |

Not implemented, on purpose:

- **Three taps → right click**: telling 2 from 3 taps means delaying every click; and Windows treats three quick
  clicks as "select paragraph". Right click has its own button (and can be mapped to a swipe or tool button).
- **Double tap and hold → drag mode**: touch-and-hold already drags; for long drags use the **Drag** tool button
  (click-and-hold toggle).

## 3. Watch screens

Main screen: the pad, then **Right click · Center · Pause**, and at the bottom **Tools · Double Tap · Settings**.

**Tools** screen:

| Button | Function |
|---|---|
| Precision | Fine-cursor mode on/off (highlighted while on) |
| Drag | Click-and-hold on/off |
| Display | Move pointing to the next monitor (centres your current aim on it) |
| 8 profile buttons | Labelled live from the PC's active profile (below) |

*Pointer on/off* is the Pause button (and Lower-to-pause); *Settings* is the gear.

## 4. Desktop profile (default)

| Tool button | Action |
|---|---|
| Desktop | Win+D (show desktop) |
| App switch | Alt+Tab |
| Task View | Win+Tab |
| Screenshot | Win+Shift+S |
| Esc | Escape |
| Enter | Enter |
| Keyboard | Opens the Windows On-Screen Keyboard |
| Lock PC | Locks Windows (LockWorkStation; Win+L can't be sent as keys) |

Swipes: next desktop (Ctrl+Win+Right), previous desktop (Ctrl+Win+Left), Task View, show desktop. Window snapping
(Win+Left/Right) and anything else can be added to any input as a shortcut.

## 5. Presentation profile

| Control | Action |
|---|---|
| Tap / swipe left / flick right / Crown forward | Next slide (→) |
| Right-click button / swipe right / flick left / Crown back | Previous slide (←) |
| Swipe up, tool **Start** | F5 |
| Tool **From here** | Shift+F5 |
| Swipe down, shake, tool **End show** | Esc |
| Tool **Black** / **White** | B / W (toggle black or white screen) |
| Tool **PPT laser** | Ctrl+L (PowerPoint's own laser pointer) |
| Tool **Laser dot** | WatchPointer's red on-screen dot |
| Tool **Pen** | Ctrl+P (PowerPoint pen) |

Keys go to the focused window: click into the slideshow once.

## 6. Media profile

| Control | Action |
|---|---|
| Tap, Double Tap, tool **Play/Pause** | Play / pause (system media key, works in any app) |
| Swipe left, flick right, tool **Next** | Next track |
| Swipe right, flick left, tool **Previous** | Previous track |
| Crown, twist, swipe up/down | Volume up / down |
| Right-click button, tool **Mute** | Mute |
| Tool **Seek − / Seek +** | ← / → (YouTube, Netflix, VLC, Media Player when focused; there is no universal seek key) |
| Tool **Fullscreen**, **Stop** | F, media stop |

## 7. Speed modes

| Mode | What it does | Switch with |
|---|---|---|
| Normal | Standard sensitivity | — |
| Precision | Movement × 0.35 (adjustable) for small targets | Watch **Precision** tool, Ctrl+Alt+F, dashboard, auto precision |
| Fast | Movement × 2 (adjustable) for large screens | Dashboard |
| Drawing | Movement × 0.6, steadier filter, no prediction (no overshoot) | Dashboard |

Switching never makes the cursor jump: the new scale applies from where the cursor is, and returning to Normal
re-anchors laser pointing to the current position. Presentation and Media are **profiles** (Ctrl+Alt+N or the
dashboard), and combine with any speed mode.

## 8. PC hotkeys

| Keys | Action |
|---|---|
| Ctrl+Alt+P | Pause / resume |
| Ctrl+Alt+C | Recenter |
| Ctrl+Alt+F | Precision on/off |
| Ctrl+Alt+L | Laser dot |
| Ctrl+Alt+N | Next profile |

All configurable on **Keyboard shortcuts**.
