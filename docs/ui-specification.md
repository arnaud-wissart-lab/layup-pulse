# LayupPulse UI specification

## 1. Experience goals

The desktop application presents a calm, information-dense industrial dashboard for a fictional simulated cell. Operators should understand connection state, machine state, active alarms, run progress, and the next valid action within a few seconds.

The interface must remain responsive under high telemetry rates. Visual urgency comes from hierarchy, concise language, and consistent state color—not animation, flashing surfaces, or decorative effects.

## 2. Visual language

- Dark neutral background with slightly raised panels and restrained borders.
- High-contrast off-white primary text and muted blue-gray secondary text.
- Cyan or blue accent for selection and neutral live data.
- Green for healthy or completed conditions.
- Amber for paused, degraded, or acknowledgment-needed conditions.
- Red for active faults and critical alarms.
- State is never communicated by color alone; pair color with text and an icon or shape.
- Use tabular numerals for live engineering values and consistent unit placement.
- All branding and visual assets are original to LayupPulse and fictional.

## 3. Primary shell

The target desktop layout is optimized for 1280×720 and above, remains usable at 100–200% Windows scaling, and defines four persistent regions around the selected page content.

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ Machine-status header: connection · state · recipe · progress · clock      │
├──────────────┬────────────────────────────────────────┬──────────────────────┤
│ Left         │ Central machine visualization / page  │ Right KPI panel      │
│ navigation   │ content                                │ live process values  │
│              │                                        │ and command summary  │
│ Overview     │                                        │                      │
│ Alarms       │                                        │                      │
│ History      │                                        │                      │
│ Diagnostics  │                                        │                      │
├──────────────┴────────────────────────────────────────┴──────────────────────┤
│ Bottom area: bounded telemetry trends and active/recent alarm strip         │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Machine-status header

The header contains:

- LayupPulse product name;
- simulator connection indicator and data-age text;
- current machine state as text plus a state glyph;
- loaded recipe name or “No recipe loaded”;
- current run progress when applicable;
- UTC or clearly labeled local timestamp;
- compact access to connection and primary cycle commands.

The header remains visible across pages. Commands show why they are unavailable through status text or an accessible tooltip; they do not silently ignore activation.

### Left navigation

The navigation contains Overview, Alarms, History, and Diagnostics. Each item has text and a distinct icon. The selected item has a persistent accent bar and focus styling. An alarm badge shows the bounded count of active unacknowledged alarms, using `99+` rather than growing width indefinitely.

### Central machine visualization

On Overview, the central region displays the fictional cell, current head position, simplified layup path, and active-process emphasis. The first operational version may be simplified; P1 adds portfolio-quality 3D. Rendering runs independently from telemetry acquisition and is capped to a measured refresh rate.

On other pages, this region becomes the primary page content while retaining the header and navigation.

### Right KPI panel

The fixed-width KPI panel presents:

- target and actual feed rate;
- compaction force;
- heater temperature;
- material pressure;
- cycle progress;
- process-health score;
- current command status or most recent rejection.

Each KPI shows label, value, unit, freshness or quality when degraded, and an optional compact threshold indicator. Values use stable widths to avoid layout movement.

### Bottom telemetry and alarm area

The bottom region can switch or split between:

- bounded telemetry trends for selected signals;
- active alarms ordered by severity and raised time;
- the most recent command and state-transition events.

Charts display a fixed time window and maximum point count. New samples are aggregated or coalesced before UI dispatch. The area can expand for investigation but must not obscure critical command feedback.

## 4. Pages

### Overview

Overview supports the primary operating sequence:

- connect or disconnect;
- select and load a fictional recipe;
- start, pause, resume, stop, or reset when valid;
- monitor machine visualization, state, progress, KPIs, telemetry trends, and active alarms;
- receive explicit pending, success, rejected, timeout, and disconnected command feedback.

Potentially disruptive commands such as Stop use confirmation only when accidental activation has a meaningful demonstration cost. Routine commands remain direct and state-gated.

### Alarms

The Alarms page contains:

- summary counts by lifecycle and severity;
- an active-alarm table with severity, alarm, message, raised time, age, acknowledgment state, and related signal;
- an acknowledgment action with clear selected scope;
- filters for active, acknowledged, cleared, severity, and time range;
- a detail panel showing lifecycle timestamps and related telemetry context;
- paged historical results rather than an unbounded collection.

Acknowledgment confirms awareness only. The UI must not imply that acknowledgment clears a physical or simulated condition.

### History

The History page contains:

- paged recent production runs;
- filters for outcome, fictional recipe, date range, and alarm presence;
- run summary with start, end, duration, outcome, completion, and process-health result;
- a selected-run timeline of commands, state transitions, and alarms;
- bounded or queried trend data for the selected run.

Empty, loading, failed, and no-result states are explicit. Database work is asynchronous through application services; no ViewModel references a `DbContext`.

### Diagnostics

The Diagnostics page contains:

- desktop and simulator version information;
- connection endpoint description without credentials;
- session identifier and connection duration;
- last telemetry timestamp, sequence number, age, and detected gaps;
- acquisition, UI refresh, and persistence rates shown separately;
- bounded-buffer capacity, utilization, and drop/coalescing counters;
- reconnect status and latest communication error;
- deterministic fault-injection controls, clearly marked “Simulation only”;
- a bounded recent diagnostic-event view.

Fault injection requires an explicit profile selection and clear active-state banner. Resetting demonstration faults must not be confused with a real safety reset.

## 5. Interaction and accessibility

- Support keyboard navigation for all pages and commands with visible focus indicators.
- Provide automation names for icons, state glyphs, charts, and visualization controls.
- Preserve readable contrast in default, hover, pressed, disabled, and selected states.
- Respect Windows text scaling and avoid truncating safety-relevant or fault text.
- Provide text summaries for charts and the machine visualization.
- Avoid rapid flashing and offer reduced motion for nonessential transitions.
- Announce state changes, command failures, and newly raised critical alarms through an accessible live region without flooding announcements at telemetry frequency.

## 6. Responsiveness and bounded updates

- Never bind raw acquisition-frequency events directly to the WPF dispatcher.
- Coalesce telemetry into a maximum UI refresh target and update only changed properties.
- Use ring buffers or fixed windows for chart points.
- Page or virtualize alarm and history results.
- Keep command execution asynchronous and cancellable.
- Measure dispatcher latency and visualization frame cost before increasing refresh rates.
- When data becomes stale, keep the last value visible only with an unmistakable stale indicator and age.

## 7. Application states

The shell defines explicit presentations for:

- simulator not running or unreachable;
- connecting;
- connected and ready;
- active cycle;
- paused cycle;
- faulted cycle;
- completed cycle;
- telemetry stale while transport appears connected;
- empty history;
- persistence or query failure;
- application shutdown in progress.

Every state includes a concise explanation and the safest available next demonstration action. No UI wording may imply that LayupPulse provides real machine safety or control authority.
