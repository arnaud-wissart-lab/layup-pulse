# LayupPulse product specification

## 1. Purpose

LayupPulse is an independent industrial desktop software demonstrator for supervising a simulated automated composite layup cell. Its purpose is to present a credible portfolio example of a responsive WPF operator interface, a separate deterministic simulator, asynchronous process communication, operational diagnostics, and production traceability.

The demonstrator uses fictional equipment, recipes, operating values, branding, and visual language. It is not affiliated with an industrial company and must not be described as production-ready machine-control or safety software.

## 2. Intended users and roles

### Operator

The operator connects to the simulator, selects a simulated recipe, starts and controls a production cycle, watches process conditions, and responds to alarms.

### Production supervisor

The production supervisor reviews current progress, completed and interrupted runs, cycle outcomes, alarm history, and summary performance indicators.

### Maintenance or diagnostics user

The diagnostics user inspects connectivity, simulator status, telemetry freshness, process-health indicators, command results, and fault-injection behavior.

These roles describe user perspectives only. Authentication and authorization are explicit non-goals for the demonstrator.

## 3. Primary use cases

1. Connect the desktop application to the separate simulator process.
2. Load a fictional composite layup recipe.
3. Start, pause, resume, stop, and reset a simulated production cycle.
4. Observe live machine state, motion, process values, progress, and process health.
5. Identify, acknowledge, and observe the clearing of simulated alarms.
6. Inject deterministic faults to demonstrate degraded and faulted behavior.
7. Review production-run and alarm history after a cycle.
8. Diagnose communication state, data freshness, and simulator behavior.

## 4. Simulated cell

The fictional cell represents an automated composite layup process with:

- a three-axis layup head with X, Y, and Z positions;
- a material feed path with commanded and measured feed rate;
- a compaction roller with force measurement;
- a material heater with temperature measurement;
- a material supply with pressure measurement;
- a virtual work surface and recipe-defined layup path;
- a deterministic cycle controller;
- a fault-injection facility available only in the simulator.

The model is intentionally simplified. Values are synthetic engineering signals chosen to support software demonstrations, not to describe or reproduce real equipment.

## 5. Machine state model

The machine exposes exactly these top-level states:

| State | Meaning |
| --- | --- |
| `Disconnected` | No active communication session exists. |
| `Connecting` | A connection attempt is in progress and commands are restricted. |
| `Ready` | Communication is healthy, prerequisites are met, and a cycle may be prepared or started. |
| `Running` | The simulated layup cycle is advancing. |
| `Paused` | The current cycle is retained but process advancement is suspended. |
| `Faulted` | A blocking simulated fault prevents normal cycle execution. |
| `Completed` | The loaded cycle reached its deterministic end successfully. |

Transitions must be deterministic and command validity must depend on the current state and required context. Invalid commands must produce an explicit rejection result rather than silently changing state.

## 6. Commands

| Command | Intent |
| --- | --- |
| `Connect` | Establish a session with the simulator. |
| `Disconnect` | End the session and return the client to `Disconnected`. |
| `LoadRecipe` | Select and validate a fictional recipe for the next run. |
| `Start` | Begin the loaded recipe from an allowed state. |
| `Pause` | Suspend an active cycle without discarding progress. |
| `Resume` | Continue a paused cycle. |
| `Stop` | End an active or paused cycle in a controlled, non-completed outcome. |
| `Reset` | Clear resettable fault or terminal-cycle context and return to an allowed preparation state. |

Every command will carry a correlation identifier and produce a success or rejection result suitable for operator feedback and diagnostics. Command handling must not block the UI thread.

## 7. Telemetry

Each telemetry sample contains:

- timestamp;
- monotonically increasing sequence number within a simulator session;
- machine state;
- head X position;
- head Y position;
- head Z position;
- target feed rate;
- actual feed rate;
- compaction force;
- heater temperature;
- material pressure;
- cycle progress;
- process-health score.

Units, ranges, precision, freshness thresholds, and quality indicators will be defined with the domain model. The timestamp and sequence number allow the client to detect stale, missing, duplicated, or reordered samples.

### Profil initial du simulateur

La fréquence par défaut est de 20 Hz et peut être configurée de 1 à 50 Hz. À graine, séquence de commandes et nombre de ticks identiques, les valeurs synthétiques sont reproductibles. Le chemin suit huit passes raster fictives : X évolue approximativement de 100 à 900 mm, Y de 75 à 390 mm et Z autour de 25 mm. La progression n’avance qu’en état `Running` et atteint exactement 100 % avant la transition vers `Completed`.

Pour la recette intégrée `Wing Panel Demo`, la consigne de débit est de 120 mm/s et le débit réel y converge avec une rampe de 80 mm/s². La température converge vers 145 °C au lieu de varier sans borne. En cycle normal, la pression reste autour de 6 bar (± 0,04 bar) et la force de compactage autour de 450 N (± 5 N). Ces plages sont des choix purement fictifs destinés aux tests et à la démonstration logicielle ; elles ne constituent pas des paramètres de procédé réels.

## 8. Alarms

The initial alarm catalog contains:

- high temperature;
- low material pressure;
- unstable compaction force;
- communication timeout;
- head-position error.

Each alarm includes a stable alarm identifier, type, severity, message, raised timestamp, current lifecycle state, and optional acknowledged and cleared timestamps.

### Alarm lifecycle

An alarm follows these lifecycle states:

1. `Raised`: the alarm condition is active and requires visibility.
2. `Acknowledged`: an operator has confirmed awareness; acknowledgment does not remove the underlying condition.
3. `Cleared`: the alarm condition is no longer active. A cleared alarm remains available in history.

If a condition clears before acknowledgment, the history must still preserve that it was raised and then cleared. Blocking behavior depends on alarm policy, not solely on lifecycle state.

## 9. Production run history

A production-run record will include:

- run identifier and fictional recipe identity;
- start and end timestamps;
- final outcome and terminal machine state;
- elapsed and active cycle duration;
- completion percentage;
- summary process values and process-health result;
- alarm occurrences and fault-injection context;
- interruption, stop, or failure reason when applicable.

History must remain queryable after application restart through local SQLite persistence. The history view will support recent-run browsing and inspection of a selected run; it is not a complete manufacturing execution system.

## 10. Demonstration scenarios

### Normal cycle

1. Start the simulator and desktop application.
2. Connect and observe a healthy `Ready` state.
3. Load a recipe and start the cycle.
4. Observe deterministic motion, process telemetry, progress, and charts.
5. Pause and resume without losing run context.
6. Reach `Completed` and review the persisted production record.

### High-temperature fault

Inject a rising heater-temperature condition. The application raises a high-temperature alarm, degrades process health, and applies the specified fault policy. The operator acknowledges the alarm, removes the injected condition, resets when allowed, and reviews the event history.

### Low-pressure fault

Inject low material pressure during a run. The application shows the signal trend, raises the corresponding alarm, and demonstrates deterministic recovery or fault behavior.

### Unstable-force fault

Inject bounded oscillation in compaction force. The diagnostic view exposes the unstable signal and the alarm lifecycle without allowing high-frequency data to grow UI memory without limit.

### Communication interruption

Suspend or stop the simulator while connected. The desktop detects stale telemetry, raises a communication-timeout alarm, exposes reconnect status, and remains responsive during cancellation and retry.

### Position-error fault

Inject a divergence between target and actual head position. The run transitions according to the fault policy and preserves the reason in production history.

Fault scenarios must be reproducible through explicit seeds or scripted profiles so automated tests and demonstrations observe the same state transitions.

### Noms du contrat d’injection

Le contrat gRPC expose exactement `OverTemperature`, `LowMaterialPressure`, `UnstableCompactionForce`, `HeadPositionError` et `CommunicationDrop`. Le mapping serveur relie explicitement `OverTemperature` au concept métier de température élevée et `CommunicationDrop` au concept métier de délai de communication dépassé. Cette différence de vocabulaire ne fuit pas dans les objets du domaine.

Tous les défauts de procédé injectés sont bloquants dans le premier profil : la télémétrie reflète la condition et la machine passe à `Faulted`. `CommunicationDrop` interrompt le flux avec un statut transport détectable, sans bloquer ni arrêter le serveur ; après `ClearFault`, un nouveau flux peut être ouvert puis la machine peut être réinitialisée. Aucune commande n’est présentée comme un arrêt d’urgence : `Stop` demeure un arrêt normal du procédé fictif.

## 11. Non-goals

LayupPulse does not provide:

- real machine control or connectivity to physical actuators;
- real safety functions, interlocks, risk reduction, or safety certification;
- production process guarantees or validated engineering limits;
- CAD import, CAD processing, path planning, or toolpath generation;
- user authentication, authorization, or enterprise identity integration;
- a complete MES, ERP, quality-management, or maintenance-management system;
- proprietary machine protocols, data, recipes, branding, or behavior;
- high-availability deployment or multi-site operations.
