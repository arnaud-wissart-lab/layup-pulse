using LayupPulse.Application;
using LayupPulse.Domain;
using Xunit;

namespace LayupPulse.Tests;

public sealed class MachineCommandAvailabilityTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(MachineState.Ready, MachineCommandType.LoadRecipe, true)]
    [InlineData(MachineState.Ready, MachineCommandType.Pause, false)]
    [InlineData(MachineState.Running, MachineCommandType.Pause, true)]
    [InlineData(MachineState.Running, MachineCommandType.Stop, true)]
    [InlineData(MachineState.Paused, MachineCommandType.Resume, true)]
    [InlineData(MachineState.Paused, MachineCommandType.Stop, true)]
    [InlineData(MachineState.Completed, MachineCommandType.Reset, true)]
    [InlineData(MachineState.Disconnected, MachineCommandType.LoadRecipe, false)]
    public void AvailabilityFollowsMachineStateRules(
        MachineState state,
        MachineCommandType commandType,
        bool expected)
    {
        MachineSnapshot snapshot = new(state, Timestamp, BuiltInRecipes.WingPanelDemo);

        bool actual = MachineCommandAvailability.CanExecute(snapshot, commandType);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StartRequiresAValidLoadedRecipe()
    {
        MachineSnapshot withoutRecipe = new(MachineState.Ready, Timestamp);
        MachineSnapshot withRecipe = withoutRecipe with { LoadedRecipe = BuiltInRecipes.WingPanelDemo };

        Assert.False(MachineCommandAvailability.CanExecute(withoutRecipe, MachineCommandType.Start));
        Assert.True(MachineCommandAvailability.CanExecute(withRecipe, MachineCommandType.Start));
    }

    [Fact]
    public void ResetFromFaultedRequiresAllFaultsToBeCleared()
    {
        MachineSnapshot faulted = new(
            MachineState.Faulted,
            Timestamp,
            activeFaults: [FaultType.HighTemperature]);
        MachineSnapshot cleared = faulted with { ActiveFaults = [] };

        Assert.False(MachineCommandAvailability.CanExecute(faulted, MachineCommandType.Reset));
        Assert.True(MachineCommandAvailability.CanExecute(cleared, MachineCommandType.Reset));
    }
}
