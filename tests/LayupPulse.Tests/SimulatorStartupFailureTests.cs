using System.IO;
using System.Net.Sockets;
using LayupPulse.Simulator;
using Xunit;

namespace LayupPulse.Tests;

public sealed class SimulatorStartupFailureTests
{
    [Fact]
    public void AddressInUseIsDetectedThroughInnerException()
    {
        IOException failure = new(
            "Échec de liaison.",
            new SocketException((int)SocketError.AddressAlreadyInUse));

        Assert.True(SimulatorStartupFailure.IsAddressInUse(failure));
        Assert.False(SimulatorStartupFailure.IsAddressInUse(
            new IOException("Une autre erreur de démarrage.")));
    }
}
