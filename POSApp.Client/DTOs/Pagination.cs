namespace POSApp.Client.DTOs;

public sealed record PageInfo(int Number, int Size, int Total)
{
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)Size));
    public int First => Total == 0 ? 0 : (Number - 1) * Size + 1;
    public int Last => Math.Min(Number * Size, Total);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, PageInfo Pagination);

