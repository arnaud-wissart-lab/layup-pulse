namespace LayupPulse.Desktop;

/// <summary>
/// Isole le remarshalement des changements de présentation sur le thread de l’interface.
/// </summary>
public interface IUiDispatcher
{
    public void Post(Action action);
}
