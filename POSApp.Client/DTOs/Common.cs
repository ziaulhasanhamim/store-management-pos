namespace POSApp.Client.DTOs;

public sealed record Lookup(int Id, string Name);

public sealed record AntiforgeryResponse(string Token);

public sealed record InventoryApiError(string Message);
