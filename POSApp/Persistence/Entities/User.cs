using Microsoft.AspNetCore.Identity;

namespace POSApp.Persistence.Entities;

public sealed class User : IdentityUser<int>
{
    public bool MustChangePassword { get; set; }
    public int? CashLocationId { get; set; }
    public StockLocation? CashLocation { get; set; }
}
