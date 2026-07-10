using LayupPulse.Application;
using LayupPulse.Desktop;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class OverviewViewModelTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CommandAvailabilityReflectsTheCurrentSnapshot()
    {
        TestSessionService service = new(CreateState(MachineState.Ready, BuiltInRecipes.WingPanelDemo));
        OverviewViewModel viewModel = new(service, TimeProvider.System);

        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.LoadDemoRecipeCommand.CanExecute(null));
        Assert.False(viewModel.PauseCommand.CanExecute(null));
        Assert.False(viewModel.ResumeCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task RejectedCommandProducesVisibleFeedbackAndReenablesCommands()
    {
        MachineSessionState state = CreateState(MachineState.Ready, BuiltInRecipes.WingPanelDemo);
        TestSessionService service = new(state)
        {
            CommandResultFactory = command => new MachineCommandExecutionResult(
                MachineCommandExecutionStatus.Rejected,
                "Démarrage rejeté par le simulateur.",
                CreateRejectedDomainResult(command)),
        };
        OverviewViewModel viewModel = new(service, TimeProvider.System);

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Equal("Démarrage rejeté par le simulateur.", viewModel.LastCommandFeedback);
        Assert.Equal("Danger", viewModel.FeedbackTone);
        Assert.False(viewModel.IsOperationInProgress);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConnectionFailureLeavesARecoverableDisconnectedViewModel()
    {
        TestSessionService service = new(CreateDisconnectedState())
        {
            ConnectResult = MachineSessionOperationResult.Failed(
                "Le simulateur est indisponible.",
                MachineGatewayFailureKind.Unavailable),
        };
        OverviewViewModel viewModel = new(service, TimeProvider.System);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Le simulateur est indisponible.", viewModel.LastCommandFeedback);
        Assert.Equal("Danger", viewModel.FeedbackTone);
        Assert.True(viewModel.ConnectCommand.CanExecute(null));
    }

    private static CommandResult CreateRejectedDomainResult(MachineCommand command)
    {
        MachineSnapshot snapshot = new(MachineState.Ready, Timestamp, BuiltInRecipes.WingPanelDemo);
        return new CommandResult(
            command.CorrelationId,
            StateTransitionResult.Rejected(
                snapshot,
                new StateTransitionRejection(
                    StateTransitionRejectionCode.InvalidState,
                    "Démarrage rejeté par le simulateur.")));
    }

    private static MachineSessionState CreateState(
        MachineState machineState,
        ProductionRecipe? recipe) =>
        new(
            MachineConnectionStatus.Connected,
            new MachineSnapshot(machineState, Timestamp, recipe),
            null,
            Guid.NewGuid(),
            Timestamp,
            Timestamp,
            0,
            null,
            null,
            Array.Empty<MachineDiagnosticMessage>(),
            TelemetryPipelineMetrics.Empty(3_000),
            null,
            Array.Empty<AlarmEvent>(),
            Array.Empty<AlarmEvent>());

    private static MachineSessionState CreateDisconnectedState() =>
        new(
            MachineConnectionStatus.Disconnected,
            MachineSnapshot.Disconnected(Timestamp),
            null,
            null,
            null,
            null,
            0,
            null,
            null,
            Array.Empty<MachineDiagnosticMessage>(),
            TelemetryPipelineMetrics.Empty(3_000),
            null,
            Array.Empty<AlarmEvent>(),
            Array.Empty<AlarmEvent>());

    private sealed class TestSessionService : IMachineSessionService
    {
        public TestSessionService(MachineSessionState state)
        {
            State = state;
        }

        public event EventHandler<MachineSessionStateChangedEventArgs>? StateChanged;

        public MachineSessionState State { get; private set; }

        public MachineSessionOperationResult ConnectResult { get; set; } =
            MachineSessionOperationResult.Successful("Connexion établie.");

        public Func<MachineCommand, MachineCommandExecutionResult>? CommandResultFactory { get; set; }

        public Task<MachineSessionOperationResult> ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ConnectResult);
        }

        public Task<MachineSessionOperationResult> DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MachineSessionOperationResult.Successful("Déconnexion terminée."));
        }

        public Task<MachineCommandExecutionResult> ExecuteCommandAsync(
            MachineCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MachineCommandExecutionResult result = CommandResultFactory?.Invoke(command)
                ?? new MachineCommandExecutionResult(MachineCommandExecutionStatus.Accepted, "Commande acceptée.");
            return Task.FromResult(result);
        }

        public Task<MachineSessionOperationResult> SetDemoFaultAsync(
            FaultType fault,
            bool active,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MachineSessionOperationResult.Successful("Défaut simulé modifié."));
        }

        public bool AcknowledgeAlarm(Guid alarmId) => false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Publish(MachineSessionState state)
        {
            State = state;
            StateChanged?.Invoke(this, new MachineSessionStateChangedEventArgs(state));
        }
    }
}
