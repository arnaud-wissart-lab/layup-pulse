# Repository guide

LayupPulse is an independent portfolio demonstrator for supervising a simulated composite layup cell. It must never be represented as safe or suitable for controlling real industrial hardware.

## Start here

Before changing code:

1. Read `docs/product-spec.md`, `docs/architecture.md`, and `docs/implementation-plan.md`.
2. Inspect existing code, tests, naming, and project conventions.
3. Use `docs/ui-specification.md` for operator-interface work.
4. Record material architecture choices under `docs/decisions/`.

## Repository map

- `src/LayupPulse.Domain`: technology-independent business rules; no solution-project references.
- `src/LayupPulse.Application`: use cases and ports; may depend only on Domain.
- `src/LayupPulse.Contracts`: transport contracts; no WPF or Infrastructure dependency.
- `src/LayupPulse.Infrastructure`: gRPC, persistence, and other concrete adapters.
- `src/LayupPulse.Simulator`: separate machine-simulation process.
- `src/LayupPulse.Desktop`: WPF UI and composition root; ViewModels never access a `DbContext` directly.
- `tests/LayupPulse.Tests`: business-rule and architecture tests.
- `docs`: product, architecture, delivery, UI, and decision documentation.
- `scripts`: repeatable repository automation.

## Working rules

- Keep Domain and Application independent from UI and infrastructure technologies.
- Make the smallest coherent change required by the task and avoid speculative abstractions.
- Prefer asynchronous APIs for I/O and propagate `CancellationToken` across boundaries.
- Never use `.Result` or `.Wait()`, unbounded queues, or unbounded UI collections.
- Keep UI-thread work bounded; sample or aggregate high-frequency data before presentation.
- Add meaningful tests for business rules and observable failure behavior.
- Inspect project dependency directions when adding a reference.
- Never commit generated binaries, SQLite databases, logs, publish output, or machine-specific settings.
- Run formatting, build, and tests before declaring completion.
- Report every validation that could not be performed. Never claim a command passed unless it was actually executed successfully.

## Validation commands

Run these exact commands from the repository root:

```powershell
dotnet restore LayupPulse.sln
dotnet format LayupPulse.sln --verify-no-changes --no-restore
dotnet build LayupPulse.sln -c Release --no-restore
dotnet test LayupPulse.sln -c Release --no-build
git diff --check
```
