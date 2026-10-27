namespace POSApp.Client.Services;

public sealed class ImportActivity
{
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
}
