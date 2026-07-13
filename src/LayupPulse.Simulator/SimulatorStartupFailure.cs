using System.Net.Sockets;

namespace LayupPulse.Simulator;

/// <summary>
/// Reconnaît les échecs de démarrage qui peuvent être présentés sans trace technique.
/// </summary>
public static class SimulatorStartupFailure
{
    public static bool IsAddressInUse(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socketException &&
                socketException.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                return true;
            }

            if (current.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
