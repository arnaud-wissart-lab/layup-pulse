# LayupPulse implementation plan

## Planning principles

Each increment must remain buildable, testable, and demonstrable. New abstractions are introduced only with a concrete caller and implementation. Business behavior is established in Domain tests before UI wiring, and all high-frequency flows are bounded from their first implementation.

## P0 — Complete operational demonstrator

P0 produces a coherent local demonstration on Windows with a separate simulator and desktop process.

### Foundation

- Maintain the .NET 10 solution, central package versions, analyzers, formatting, and architecture boundary tests.
- Add a Windows GitHub Actions workflow for restore, format verification, Release build, and tests.
- Establish explicit composition roots without a service locator.

### Deterministic domain and application behavior

- Implement the seven machine states and validated command transitions.
- Model recipe loading, run lifecycle, command results, alarm lifecycle, and production outcomes.
- Add deterministic clocks, seeds, and fault profiles where controlled inputs are required.
- Define `IMachineGateway` and use-case services with end-to-end cancellation.
- Cover state, alarm, and run rules with focused unit tests.

### Separate simulator and gRPC transport

- Implement a deterministic simulation loop in the Simulator process.
- Add versioned gRPC contracts for commands, telemetry streaming, status, and diagnostics.
- Host the gRPC service in Simulator and implement the client gateway in Infrastructure.
- Add deadlines, cancellation, bounded buffering, reconnect behavior, and communication-timeout detection.
- Test contract mapping, sequencing, disconnects, and deterministic fault scenarios.

### Persistence and history

- Maintain EF Core and SQLite in Infrastructure with explicit migrations.
- Persist production-run summaries, alarms, and UTC-aligned one-second telemetry aggregates; never persist raw samples.
- Keep persistence ingestion bounded and history queries asynchronous, cancellable, and capped for the demonstrator.
- Test schema migration, round trips, idempotent updates, failure diagnostics, and reopening through a new context.

### Operator application

- Build Overview, Alarms, History, and Diagnostics pages from the UI specification.
- Implement small, typed ViewModels that consume application services and expose bounded collections.
- Add command availability, progress, connection freshness, alarm acknowledgment, and actionable error feedback.
- Add basic real-time trends and a functional simplified machine visualization without blocking the UI thread.
- Verify keyboard access, automation names, contrast, scaling, and degraded/disconnected states.

### P0 acceptance

- Demonstrate a complete normal cycle including pause and resume.
- Demonstrate all specified fault-injection scenarios and alarm lifecycle behavior.
- Restart the desktop application and review persisted production history.
- Show responsive cancellation when the simulator stops or the desktop closes.
- Pass format verification, Release build, automated tests, and the Windows CI workflow.

## P1 — Portfolio-quality visualization and packaging

P1 improves presentation and distribution without weakening the P0 boundaries.

- Livré en `0.4.0` : premier rapport borné du cycle sélectionné, avec aperçu
  WPF, pagination, impression Windows et export XPS depuis **Historique**.
- Refine the dark industrial design system, spacing, typography, iconography, states, and transitions.
- Upgrade charts with clear units, thresholds, cursor inspection, bounded time windows, and export-ready snapshots.
- Create a polished but fictional 3D cell visualization with measured frame time and a fallback reduced-motion mode.
- Add guided demonstration profiles, sample recipes, and resettable showcase data.
- Add structured logging, diagnostic export, and a concise support bundle with sensitive-path review.
- Add application iconography and independent LayupPulse visual assets.
- Produce a signed-build-ready packaging approach, self-contained Windows publish profile, and installation documentation.
- Add UI smoke coverage where stable automation provides meaningful value.
- Capture portfolio screenshots and architecture material that clearly repeat the non-production safety disclaimer.

## P2 — Future adapters and advanced analytics

P2 explores extensions after the demonstrator is complete and measured.

- Add an optional OPC UA adapter behind `IMachineGateway`; keep it disabled by default and simulator-focused.
- Define capability discovery so adapters expose supported commands and telemetry without UI assumptions.
- Add advanced process analytics such as trend envelopes, multi-run cycle
  comparison, anomaly scoring, and health-score explanation. Les comparaisons
  multi-cycles de rapports restent à réaliser.
- Evaluate longer-term storage and data export only with explicit retention and privacy requirements.
- Add replay of recorded simulated runs for deterministic diagnostics and UI regression tests.
- Explore recipe comparison and comparative production-summary reporting
  without expanding into CAD processing or a complete MES.

Any OPC UA work remains a software integration demonstration. It does not make LayupPulse suitable for real equipment or safety functions.
