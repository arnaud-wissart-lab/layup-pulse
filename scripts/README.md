# Scripts

This directory is reserved for repeatable repository automation. Add a script only when it replaces a documented manual sequence and can be validated in the supported Windows environment.

## `run-simulator.ps1`

Lance le processus gRPC autonome en environnement de développement. Le script accepte `Endpoint`, `Seed` et `TelemetryRateHz`; ses valeurs par défaut correspondent à la configuration versionnée du simulateur.
