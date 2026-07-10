namespace LayupPulse.Application;

public sealed class MachineSessionStateChangedEventArgs : EventArgs
{
    public MachineSessionStateChangedEventArgs(MachineSessionState state)
    {
        State = state;
    }

    public MachineSessionState State { get; }
}
