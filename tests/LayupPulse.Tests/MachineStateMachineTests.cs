using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class MachineStateMachineTests
{
    private static readonly DateTimeOffset InitialTimestamp =
        new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid CommandId = new("3b3fde1b-6b57-4f95-86bc-738fba54d9f4");
    private static readonly Guid RunId = new("08d1b338-1427-43be-b9bf-760d5062cc89");

    [Fact]
    public void ConnectionRequestAndEstablishmentMoveMachineToReady()
    {
        // Préparation
        MachineSnapshot disconnected = MachineSnapshot.Disconnected(InitialTimestamp);

        // Action
        StateTransitionResult connecting = MachineStateMachine.Transition(
            disconnected,
            CreateCommand(MachineCommandType.ConnectRequested, 1));
        StateTransitionResult ready = MachineStateMachine.Transition(
            connecting.Snapshot,
            CreateCommand(MachineCommandType.ConnectionEstablished, 2));

        // Vérification
        Assert.True(connecting.IsAccepted);
        Assert.Equal(MachineState.Connecting, connecting.Snapshot.State);
        Assert.True(ready.IsAccepted);
        Assert.Equal(MachineState.Ready, ready.Snapshot.State);
        Assert.Equal(InitialTimestamp.AddSeconds(2), ready.Snapshot.Timestamp);
    }

    [Fact]
    public void ConnectionFailureReturnsMachineToDisconnected()
    {
        // Préparation
        MachineSnapshot connecting = ApplyAccepted(
            MachineSnapshot.Disconnected(InitialTimestamp),
            MachineCommandType.ConnectRequested,
            1);

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            connecting,
            CreateCommand(MachineCommandType.ConnectionFailed, 2));

        // Vérification
        Assert.True(result.IsAccepted);
        Assert.Equal(MachineState.Disconnected, result.Snapshot.State);
    }

    [Theory]
    [InlineData(MachineState.Connecting)]
    [InlineData(MachineState.Ready)]
    [InlineData(MachineState.Running)]
    [InlineData(MachineState.Paused)]
    [InlineData(MachineState.Faulted)]
    [InlineData(MachineState.Completed)]
    public void ConnectionRequestIsRejectedOutsideDisconnected(MachineState initialState)
    {
        // Préparation
        MachineSnapshot snapshot = CreateSnapshot(initialState);

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            snapshot,
            CreateCommand(MachineCommandType.ConnectRequested, 9));

        // Vérification
        Assert.False(result.IsAccepted);
        Assert.Equal(snapshot, result.Snapshot);
        Assert.Equal(
            StateTransitionRejectionCode.InvalidState,
            Assert.IsType<StateTransitionRejection>(result.Rejection).Code);
    }

    [Fact]
    public void StartWithoutLoadedRecipeIsRejected()
    {
        // Préparation
        MachineSnapshot ready = CreateReadySnapshot();

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            ready,
            CreateCommand(MachineCommandType.Start, 3));

        // Vérification
        Assert.False(result.IsAccepted);
        Assert.Equal(MachineState.Ready, result.Snapshot.State);
        StateTransitionRejection rejection = Assert.IsType<StateTransitionRejection>(result.Rejection);
        Assert.Equal(StateTransitionRejectionCode.RecipeRequired, rejection.Code);
    }

    [Fact]
    public void StartWithValidLoadedRecipeCreatesRunningProductionRun()
    {
        // Préparation
        MachineSnapshot ready = CreateReadySnapshotWithRecipe();

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            ready,
            CreateCommand(MachineCommandType.Start, 4));

        // Vérification
        Assert.True(result.IsAccepted);
        Assert.Equal(MachineState.Running, result.Snapshot.State);
        ProductionRun run = Assert.IsType<ProductionRun>(result.Snapshot.CurrentRun);
        Assert.Equal(RunId, run.Id);
        Assert.Equal(ProductionRunStatus.Running, run.Status);
        Assert.Equal(BuiltInRecipes.WingPanelDemo, run.Recipe);
        Assert.Equal(InitialTimestamp.AddSeconds(4), run.StartedAt);
    }

    [Fact]
    public void StartWhileAlreadyRunningIsRejectedWithoutReplacingCurrentRun()
    {
        // Préparation
        MachineSnapshot running = CreateRunningSnapshot();
        Guid currentRunId = Assert.IsType<ProductionRun>(running.CurrentRun).Id;

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            running,
            CreateCommand(MachineCommandType.Start, 5));

        // Vérification
        Assert.False(result.IsAccepted);
        Assert.Equal(MachineState.Running, result.Snapshot.State);
        Assert.Equal(currentRunId, Assert.IsType<ProductionRun>(result.Snapshot.CurrentRun).Id);
        Assert.Equal(
            StateTransitionRejectionCode.InvalidState,
            Assert.IsType<StateTransitionRejection>(result.Rejection).Code);
    }

    [Fact]
    public void PauseIsAcceptedOnlyWhileRunning()
    {
        // Préparation
        MachineSnapshot running = CreateRunningSnapshot();

        // Action
        StateTransitionResult firstPause = MachineStateMachine.Transition(
            running,
            CreateCommand(MachineCommandType.Pause, 5));
        StateTransitionResult secondPause = MachineStateMachine.Transition(
            firstPause.Snapshot,
            CreateCommand(MachineCommandType.Pause, 6));

        // Vérification
        Assert.True(firstPause.IsAccepted);
        Assert.Equal(MachineState.Paused, firstPause.Snapshot.State);
        Assert.Equal(
            ProductionRunStatus.Running,
            Assert.IsType<ProductionRun>(firstPause.Snapshot.CurrentRun).Status);
        Assert.False(secondPause.IsAccepted);
        Assert.Equal(MachineState.Paused, secondPause.Snapshot.State);
    }

    [Fact]
    public void ResumeIsAcceptedOnlyWhilePaused()
    {
        // Préparation
        MachineSnapshot running = CreateRunningSnapshot();
        MachineSnapshot paused = ApplyAccepted(running, MachineCommandType.Pause, 5);

        // Action
        StateTransitionResult rejectedFromRunning = MachineStateMachine.Transition(
            running,
            CreateCommand(MachineCommandType.Resume, 5));
        StateTransitionResult acceptedFromPaused = MachineStateMachine.Transition(
            paused,
            CreateCommand(MachineCommandType.Resume, 6));

        // Vérification
        Assert.False(rejectedFromRunning.IsAccepted);
        Assert.Equal(MachineState.Running, rejectedFromRunning.Snapshot.State);
        Assert.True(acceptedFromPaused.IsAccepted);
        Assert.Equal(MachineState.Running, acceptedFromPaused.Snapshot.State);
        Assert.Equal(
            ProductionRunStatus.Running,
            Assert.IsType<ProductionRun>(acceptedFromPaused.Snapshot.CurrentRun).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopFromActiveStateClosesRunAsAborted(bool pauseBeforeStop)
    {
        // Préparation
        MachineSnapshot snapshot = CreateRunningSnapshot();
        if (pauseBeforeStop)
        {
            snapshot = ApplyAccepted(snapshot, MachineCommandType.Pause, 5);
        }

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            snapshot,
            CreateCommand(MachineCommandType.Stop, 6));

        // Vérification
        Assert.True(result.IsAccepted);
        Assert.Equal(MachineState.Ready, result.Snapshot.State);
        ProductionRun run = Assert.IsType<ProductionRun>(result.Snapshot.CurrentRun);
        Assert.Equal(ProductionRunStatus.Aborted, run.Status);
        Assert.Equal(MachineState.Ready, run.TerminalMachineState);
        Assert.Equal(InitialTimestamp.AddSeconds(6), run.EndedAt);
    }

    [Fact]
    public void CycleCompletionClosesRunAndMovesMachineToCompleted()
    {
        // Préparation
        MachineSnapshot running = CreateRunningSnapshot();

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            running,
            CreateCommand(MachineCommandType.CycleCompleted, 5));

        // Vérification
        Assert.True(result.IsAccepted);
        Assert.Equal(MachineState.Completed, result.Snapshot.State);
        ProductionRun run = Assert.IsType<ProductionRun>(result.Snapshot.CurrentRun);
        Assert.Equal(ProductionRunStatus.Completed, run.Status);
        Assert.Equal(100, run.CompletionPercentage);
        Assert.Equal(MachineState.Completed, run.TerminalMachineState);
        Assert.Equal(InitialTimestamp.AddSeconds(5), run.EndedAt);
    }

    [Fact]
    public void ResetAfterCompletedCycleMovesMachineToReady()
    {
        // Préparation
        MachineSnapshot completed = CreateCompletedSnapshot();

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            completed,
            CreateCommand(MachineCommandType.Reset, 6));

        // Vérification
        Assert.True(result.IsAccepted);
        Assert.Equal(MachineState.Ready, result.Snapshot.State);
        Assert.Equal(
            ProductionRunStatus.Completed,
            Assert.IsType<ProductionRun>(result.Snapshot.CurrentRun).Status);
    }

    [Theory]
    [InlineData(MachineState.Ready)]
    [InlineData(MachineState.Running)]
    [InlineData(MachineState.Paused)]
    [InlineData(MachineState.Faulted)]
    [InlineData(MachineState.Completed)]
    public void CriticalFaultFromConnectedStateMovesMachineToFaulted(MachineState initialState)
    {
        // Préparation
        MachineSnapshot snapshot = CreateSnapshot(initialState);

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            snapshot,
            CreateCommand(MachineCommandType.CriticalFaultRaised, 8, fault: FaultType.HeadPositionError));

        // Vérification
        Assert.True(result.IsAccepted);
        Assert.Equal(MachineState.Faulted, result.Snapshot.State);
        Assert.Contains(FaultType.HeadPositionError, result.Snapshot.ActiveFaults);

        if (initialState is MachineState.Running or MachineState.Paused)
        {
            Assert.Equal(
                ProductionRunStatus.Faulted,
                Assert.IsType<ProductionRun>(result.Snapshot.CurrentRun).Status);
        }
    }

    [Fact]
    public void ResetWhileCriticalFaultRemainsActiveIsRejected()
    {
        // Préparation
        MachineSnapshot faulted = CreateFaultedSnapshot();

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            faulted,
            CreateCommand(MachineCommandType.Reset, 6));

        // Vérification
        Assert.False(result.IsAccepted);
        Assert.Equal(MachineState.Faulted, result.Snapshot.State);
        Assert.Equal(
            StateTransitionRejectionCode.ActiveFaultsRemain,
            Assert.IsType<StateTransitionRejection>(result.Rejection).Code);
    }

    [Fact]
    public void ResetAfterFaultClearanceMovesMachineToReady()
    {
        // Préparation
        MachineSnapshot faulted = CreateFaultedSnapshot();
        MachineSnapshot cleared = ApplyAccepted(
            faulted,
            MachineCommandType.FaultCleared,
            6,
            fault: FaultType.HighTemperature);

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            cleared,
            CreateCommand(MachineCommandType.Reset, 7));

        // Vérification
        Assert.Empty(cleared.ActiveFaults);
        Assert.True(result.IsAccepted);
        Assert.Equal(MachineState.Ready, result.Snapshot.State);
    }

    [Theory]
    [InlineData(MachineState.Disconnected)]
    [InlineData(MachineState.Connecting)]
    [InlineData(MachineState.Ready)]
    [InlineData(MachineState.Running)]
    [InlineData(MachineState.Paused)]
    [InlineData(MachineState.Faulted)]
    [InlineData(MachineState.Completed)]
    public void DisconnectedEventMovesAnyStateToDisconnected(MachineState initialState)
    {
        // Préparation
        MachineSnapshot snapshot = CreateSnapshot(initialState);

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            snapshot,
            CreateCommand(MachineCommandType.Disconnected, 9));

        // Vérification
        Assert.True(result.IsAccepted);
        Assert.Equal(MachineState.Disconnected, result.Snapshot.State);
        Assert.Null(result.Snapshot.LoadedRecipe);
        Assert.Empty(result.Snapshot.ActiveFaults);

        if (initialState is MachineState.Running or MachineState.Paused)
        {
            Assert.Equal(
                ProductionRunStatus.Aborted,
                Assert.IsType<ProductionRun>(result.Snapshot.CurrentRun).Status);
        }
    }

    [Fact]
    public void InvalidLoadedRecipeProducesStructuredStartRejection()
    {
        // Préparation
        ProductionRecipe invalidRecipe = BuiltInRecipes.WingPanelDemo with { FeedRateMillimetersPerSecond = 0 };
        MachineSnapshot ready = CreateReadySnapshot() with { LoadedRecipe = invalidRecipe };

        // Action
        StateTransitionResult result = MachineStateMachine.Transition(
            ready,
            CreateCommand(MachineCommandType.Start, 3));

        // Vérification
        Assert.False(result.IsAccepted);
        StateTransitionRejection rejection = Assert.IsType<StateTransitionRejection>(result.Rejection);
        Assert.Equal(StateTransitionRejectionCode.InvalidRecipe, rejection.Code);
        RecipeValidationError error = Assert.Single(rejection.ValidationErrors);
        Assert.Equal(RecipeValidationErrorCode.FeedRateMustBePositive, error.Code);
    }

    private static MachineSnapshot CreateSnapshot(MachineState state) => state switch
    {
        MachineState.Disconnected => MachineSnapshot.Disconnected(InitialTimestamp),
        MachineState.Connecting => ApplyAccepted(
            MachineSnapshot.Disconnected(InitialTimestamp),
            MachineCommandType.ConnectRequested,
            1),
        MachineState.Ready => CreateReadySnapshot(),
        MachineState.Running => CreateRunningSnapshot(),
        MachineState.Paused => ApplyAccepted(CreateRunningSnapshot(), MachineCommandType.Pause, 5),
        MachineState.Faulted => CreateFaultedSnapshot(),
        MachineState.Completed => CreateCompletedSnapshot(),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static MachineSnapshot CreateReadySnapshot()
    {
        MachineSnapshot connecting = ApplyAccepted(
            MachineSnapshot.Disconnected(InitialTimestamp),
            MachineCommandType.ConnectRequested,
            1);
        return ApplyAccepted(connecting, MachineCommandType.ConnectionEstablished, 2);
    }

    private static MachineSnapshot CreateReadySnapshotWithRecipe() =>
        ApplyAccepted(
            CreateReadySnapshot(),
            MachineCommandType.LoadRecipe,
            3,
            BuiltInRecipes.WingPanelDemo);

    private static MachineSnapshot CreateRunningSnapshot() =>
        ApplyAccepted(CreateReadySnapshotWithRecipe(), MachineCommandType.Start, 4);

    private static MachineSnapshot CreateCompletedSnapshot() =>
        ApplyAccepted(CreateRunningSnapshot(), MachineCommandType.CycleCompleted, 5);

    private static MachineSnapshot CreateFaultedSnapshot() =>
        ApplyAccepted(
            CreateRunningSnapshot(),
            MachineCommandType.CriticalFaultRaised,
            5,
            fault: FaultType.HighTemperature);

    private static MachineSnapshot ApplyAccepted(
        MachineSnapshot snapshot,
        MachineCommandType commandType,
        int seconds,
        ProductionRecipe? recipe = null,
        FaultType? fault = null)
    {
        StateTransitionResult result = MachineStateMachine.Transition(
            snapshot,
            CreateCommand(commandType, seconds, recipe, fault));
        Assert.True(result.IsAccepted, result.Rejection?.Message);
        return result.Snapshot;
    }

    private static MachineCommand CreateCommand(
        MachineCommandType type,
        int seconds,
        ProductionRecipe? recipe = null,
        FaultType? fault = null) =>
        new(
            CommandId,
            type,
            InitialTimestamp.AddSeconds(seconds),
            recipe,
            RunId,
            fault);
}
