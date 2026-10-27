using Microsoft.AspNetCore.Identity;

namespace POSApp.Persistence.Entities;

public sealed class User : IdentityUser<int>
{
    public int? CashLocationId { get; set; }
    public StockLocation? CashLocation { get; set; }
}
