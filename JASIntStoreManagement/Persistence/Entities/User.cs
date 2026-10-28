using Microsoft.AspNetCore.Identity;

namespace JASIntStoreManagement.Persistence.Entities;

public sealed class User : IdentityUser<int>
{
    public int? CashLocationId { get; set; }
    public StockLocation? CashLocation { get; set; }
}
