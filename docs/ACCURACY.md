# Pointer accuracy and responsiveness

Goal: the cursor should be where you point **now**, not where you pointed a Bluetooth round trip ago, while staying
steady when you hold still.

## Where lag comes from

| Source | Typical size | What WatchPointer does |
|---|---|---|
| Watch sampling | 10 ms (100 Hz, CoreMotion's rate on watchOS) | samples carry the watch's own timestamp |
| Bluetooth delivery | ~10–25 ms + waiting for the next connection event (15–45 ms intervals, chosen by the watch) | requests a short interval (Windows 11 22H2+); **predicts** across the delay |
| Smoothing (1€ filter) | grows when the hand is still, shrinks when it moves fast | tuned per preset |
| Glide between samples | τ = 2–5 ms at 240 Hz | |
| PC processing | measured p50 ≈ 2 ms | dedicated highest-priority thread, high-resolution timer |

## Prediction

Each cursor frame aims at the filtered position pushed forward along the hand's velocity by

`lead = measured one-way delay (RTT/2, needs this project's watch app) + 8 ms` — or the fixed *Prediction (ms)*
setting — `+ time since the newest sample (≤ 40 ms, fills the gaps between Bluetooth bursts)`.

Safeguards against overshoot and jitter:

- fades in between 150 and 500 px/s, so a still or slowly moving hand is never amplified;
- fades out as soon as the **unfiltered** samples show the hand slowing down;
- while decelerating, never leads further than the stopping distance `v² / 2a`;
- never more than 120 px ahead.

## Measurements

`watchpointer-cli track` runs the real engine against a simulated hand (figure-eight at ±18°, 100 Hz samples with
tremor, Bluetooth latency and burst delivery) and compares the cursor with where the hand points at the same instant.
Results on the development PC (deterministic, same seed):

| Bluetooth | Preset | Mean error | p95 error | Lag | Still-hand spread | Overshoot on a 1340 px / 250 ms flick |
|---|---|---|---|---|---|---|
| 12 ms + 15 ms interval | Smooth (original) | 35.7 px | 52.3 px | 33 ms | 8 px | 0 px |
| | **Balanced (default)** | **6.8 px** | **14.7 px** | **≈ 0 ms** | 9 px | 49 px |
| | Fast | 7.9 px | 16.3 px | ≈ 0 ms | 10 px | 52 px |
| 12 ms + 30 ms interval | Smooth (original) | 44.9 px | 66.3 px | 42 ms | 8 px | 0 px |
| | **Balanced (default)** | **8.2 px** | **17.5 px** | **≈ 0 ms** | 9 px | 49 px |
| | Fast | 9.8 px | 18.6 px | ≈ 0 ms | 10 px | 52 px |
| 25 ms + 45 ms interval | Smooth (original) | 65.1 px | 97.9 px | 62 ms | 8 px | 0 px |
| | **Balanced (default)** | **14.0 px** | **26.7 px** | **≈ 0 ms** | 9 px | 49 px |
| | Fast | 14.9 px | 27.2 px | ≈ 0 ms | 10 px | 52 px |

Reading it: with prediction the cursor follows the hand about 5× more closely and no longer trails it. The cost is
a brief overshoot on very fast flicks (~4 % of the move on a full-screen flick at ~10 000 px/s peak; it settles
within a few frames) and ~1 px more spread when holding still. *Smooth* keeps the original behaviour for anyone who
prefers zero overshoot.

These are simulation results. With a real watch the Bluetooth timing is measured live on the **Diagnostics** page
(arrival interval, delay above best sample, round trip, PC pipeline) and adaptive prediction uses the measured round
trip.

## "Exactly like a mouse" — what's possible and what isn't

- **Absolute (laser) mode** is pointing, not mousing: accuracy is limited by how steady a hand in the air is
  (tremor is ~0.1°, i.e. a few pixels at default speed). Lower speed = finer control, more wrist travel.
- **Relative (air mouse) mode** behaves most like a mouse: speed in px/degree, acceleration curve, dead zone.
  Raise acceleration for fast long moves with precise short ones, like Windows' "Enhance pointer precision".
- Bluetooth adds tens of milliseconds a wired mouse doesn't have. Prediction hides most of it for continuous
  movement; it can't know about a sudden change of direction before the samples arrive.
- Clicks are placed where you pointed when your thumb touched the watch, so the tap's jolt doesn't move them.
