using LayupPulse.Desktop;
using Xunit;

namespace LayupPulse.Tests;

public sealed class DesktopSingleInstanceCoordinatorTests
{
    [Fact]
    public void SecondaryInstanceSignalsPrimaryAndMutexIsReleasedOnDispose()
    {
        string qualifier = Guid.NewGuid().ToString("N");
        DesktopSingleInstanceCoordinator primary = new(qualifier);
        Assert.True(primary.IsPrimaryInstance);

        int activationCount = 0;
        primary.StartListening(() =>
        {
            Interlocked.Increment(ref activationCount);
            return true;
        });

        bool secondaryWasPrimary = true;
        bool activationAcknowledged = false;
        Exception? secondaryFailure = null;
        Thread secondaryThread = new(() =>
        {
            try
            {
                using DesktopSingleInstanceCoordinator secondary = new(qualifier);
                secondaryWasPrimary = secondary.IsPrimaryInstance;
                activationAcknowledged = secondary.TryActivateExistingInstance(
                    TimeSpan.FromSeconds(2));
            }
            catch (Exception exception)
            {
                secondaryFailure = exception;
            }
        });

        secondaryThread.Start();
        Assert.True(secondaryThread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(secondaryFailure);
        Assert.False(secondaryWasPrimary);
        Assert.True(activationAcknowledged);
        Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref activationCount) == 1,
            TimeSpan.FromSeconds(2)));

        primary.Dispose();
        using DesktopSingleInstanceCoordinator replacement = new(qualifier);
        Assert.True(replacement.IsPrimaryInstance);
    }
}
