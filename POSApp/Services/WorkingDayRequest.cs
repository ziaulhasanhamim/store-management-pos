namespace POSApp.Services;

public static class WorkingDayRequest
{
    private static readonly AsyncLocal<DateOnly?> Expected = new();

    public static DateOnly? Date
    {
        get => Expected.Value;
        set => Expected.Value = value;
    }
}
