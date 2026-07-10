using LayupPulse.Contracts.Grpc;

namespace LayupPulse.Simulator;

/// <summary>
/// Signale une requête de transport qui ne peut pas devenir une intention métier valide.
/// </summary>
public sealed class TransportMappingException : Exception
{
    public TransportMappingException(RejectionReason rejectionReason, string message)
        : base(message)
    {
        RejectionReason = rejectionReason;
    }

    public RejectionReason RejectionReason { get; }
}
