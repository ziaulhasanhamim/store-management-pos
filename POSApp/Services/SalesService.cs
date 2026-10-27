using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class SalesService(IServiceScopeFactory scopes, IConfiguration? configuration = null)
{
    public async Task<SalesOptions> OptionsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        var locations = await db.StockLocations.AsNoTracking().Where(location => !location.IsDeleted && location.CanSell)
            .OrderBy(location => location.Name).Select(location => new Lookup(location.Id, location.Name)).ToArrayAsync(ct);
        var customers = await db.Customers.AsNoTracking().Where(customer => !customer.IsDeleted)
            .OrderBy(customer => customer.Name).Select(customer => new CustomerOption(customer.Id, customer.Name,
                customer.Balance == null ? 0 : customer.Balance.CurrentBalance)).ToArrayAsync(ct);
        var drafts = await db.Sales.AsNoTracking().Where(sale => !sale.IsDeleted && sale.CashierId == actorId && sale.Status == SaleStatus.Draft)
            .OrderByDescending(sale => sale.Id).Take(30)
            .Select(sale => new { sale.ReceiptNumber, Customer = sale.Customer == null ? null : sale.Customer.Name, sale.TotalAmount })
            .ToListAsync(ct);
        return new(locations, customers, drafts.Select(draft => new SalesDraftRow(
            Guid.ParseExact(draft.ReceiptNumber[4..], "N"), draft.ReceiptNumber, draft.Customer, draft.TotalAmount)).ToArray())
        {
            WorkingDate = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct),
            CanSetOpeningBalance = await db.UserRoles.AnyAsync(membership => membership.UserId == actorId
                && db.Roles.Any(role => role.Id == membership.RoleId && (role.Name == AppRoles.Admin || role.Name == AppRoles.InventoryManager)), ct)
        };
    }

    public async Task<IReadOnlyList<SalesProduct>> SearchAsync(ClaimsPrincipal actor, int locationId, string? search, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        var term = search?.Trim() ?? "";
        var normalizedTerm = NameNormalization.Normalize(term);
        var query = db.Products.AsNoTracking().Where(product => !product.IsDeleted);
        if (term.Length > 0)
        {
            int.TryParse(term, out var productId);
            query = query.Where(product => product.Barcode == term
                || product.NormalizedName.Contains(normalizedTerm)
                || (product.Barcode != null && product.Barcode.ToLower().Contains(term.ToLower()))
                || product.Id == productId);
        }
        var products = await query.OrderByDescending(product => product.Barcode != null && product.Barcode.ToLower() == term.ToLower())
            .ThenBy(product => product.Name).Take(30)
            .Select(product => new SalesProduct(product.Id, product.Name, product.Barcode, product.Unit, product.SellingPrice,
                product.InventoryBalances.Where(balance => balance.StockLocationId == locationId)
                    .Select(balance => (decimal?)balance.QuantityOnHand).Sum() ?? 0, product.AllowFractionUnit, product.IsNonStocked)).ToListAsync(ct);
        var productIds = products.Select(product => product.Id).ToArray();
        var attributes = await db.ProductAttributeValues.AsNoTracking()
            .Where(value => productIds.Contains(value.ProductId) && !value.Definition!.IsDeleted && value.Definition.ShowInSales)
            .Select(value => new
            {
                value.ProductId,
                Attribute = new ItemAttributeDisplay(value.Definition!.Key, value.Definition.DisplayName,
                    value.Definition.Kind == ProductAttributeKind.Checkbox
                        ? value.Value == "true" ? "Yes" : "No"
                        : value.DisplayValue ?? (value.Definition.Kind == ProductAttributeKind.Select
                            ? value.Definition.Options.Where(option => option.Value == value.Value)
                                .Select(option => option.DisplayName).FirstOrDefault() ?? value.Value
                            : value.Value),
                    value.Definition.ShowInItems, value.Definition.ShowInSales, value.Definition.ShowInReceiving,
                    value.Definition.ShowInStockAdjustment, value.Value)
            }).ToListAsync(ct);
        var byProduct = attributes.GroupBy(value => value.ProductId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ItemAttributeDisplay>)group.Select(value => value.Attribute).ToArray());
        return products.Select(product => product with { Attributes = byProduct.GetValueOrDefault(product.Id, []) }).ToArray();
    }

    public async Task<int> AddCustomerAsync(ClaimsPrincipal actor, CustomerInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200 || input.Phone?.Length > 64)
            {
                throw new ValidationException("Enter a customer name (up to 200 characters) and a valid phone number.");
            }
            StoreAccess.Money(input.OpeningBalance, "Opening balance", negative: true);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
            // Only inventory managers/admins can create opening dues; sales associates can add customers with zero opening balance.
            if (input.OpeningBalance != 0)
            {
                await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            }
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var at = AccountingCalendar.At(date, configuration);
            var balance = new CustomerBalance
            {
                CustomerId = 0,
                Customer = new Customer
                {
                    Name = input.Name.Trim(),
                    NormalizedName = NameNormalization.Normalize(input.Name),
                    Phone = input.Phone?.Trim(),
                    CreatedByUserId = actorId
                },
                OpeningBalance = input.OpeningBalance,
                CurrentBalance = input.OpeningBalance,
                OpenedAt = at
            };
            balance.Entries.Add(new CustomerBalanceEntry
            {
                CustomerBalanceId = 0,
                CustomerBalance = balance,
                Type = BalanceEntryType.OpeningBalance,
                Amount = input.OpeningBalance,
                BalanceAfter = input.OpeningBalance,
                OccurredAt = at
            });
            db.CustomerBalances.Add(balance);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return balance.CustomerId;
        }, ct);
    }

    public async Task<SalesResult> SaveAsync(ClaimsPrincipal actor, SalesCommand command, bool complete, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<SalesResult>(async () =>
        {
            if (command.RequestId == Guid.Empty || command.Lines.Length is < 1 or > 100
                || command.Lines.Select(line => (line.ProductId, Return: line.Quantity < 0)).Distinct().Count() != command.Lines.Length
                || !Enum.IsDefined(command.Method) || command.Notes?.Length > 2000)
            {
                throw new ValidationException("Add between 1 and 100 distinct items and choose a payment method.");
            }
            foreach (var line in command.Lines)
            {
                if (line.Quantity == 0 || Math.Abs(line.Quantity) > 999999999999999.999m || decimal.Round(line.Quantity, 3) != line.Quantity)
                {
                    throw new ValidationException("Sales quantities must be nonzero with up to three decimal places.");
                }
                StoreAccess.Money(line.UnitPrice, "Selling price");
                if (line.UnitPrice != 0 && Math.Abs(line.Quantity) > 9999999999999999.99m / line.UnitPrice)
                {
                    throw new ValidationException("The line total exceeds the supported amount.");
                }
            }
            var pricing = POSApp.Services.Calculations.SalePricingCalculator.Calculate(
                command.Lines, command.Discount, command.Payments, command.Payment, command.Method,
                allowChange: true, allowRefund: true);
            var subtotal = pricing.Subtotal;
            var unitDiscounts = pricing.UnitDiscounts;
            var discount = pricing.DiscountAmount;
            var total = pricing.TotalAmount;
            var paymentPlan = pricing.PaymentPlan;
            var paidAmount = pricing.PaidAmount;
            if (complete && command.CustomerId is null && paidAmount != total)
            {
                throw new ValidationException("Select a customer for due or partial payment.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var businessDate = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(businessDate, WorkingDayRequest.Date);
            var hash = StoreOperation.Fingerprint(new
            {
                command.RequestId, command.LocationId, command.CustomerId, command.Lines,
                command.Discount, command.Payment, command.Method, command.Notes, command.Payments,
                Complete = complete
            });
            var number = $"SAL-{command.RequestId:N}";
            var sale = await db.Sales.Include(item => item.Items).Include(item => item.Payments).Include(item => item.DraftPayments)
                .SingleOrDefaultAsync(item => item.ReceiptNumber == number, ct);
            if (sale?.IsDeleted == true)
            {
                throw new ValidationException("This sale was deleted by an administrator. Start a new sale.");
            }
            if (sale is not null && sale.CashierId != actorId)
            {
                throw new UnauthorizedAccessException("This draft belongs to another user.");
            }
            if (sale is not null && sale.RequestHash == hash)
            {
                var previousPaid = sale.Payments.Sum(payment => payment.Amount);
                return new(sale.Id, sale.ReceiptNumber, sale.Status == SaleStatus.Completed,
                    sale.TotalAmount, previousPaid, sale.TotalAmount - previousPaid) { Revision = sale.Revision };
            }
            if (sale?.Status == SaleStatus.Completed)
            {
                if (sale.RequestHash is not null)
                {
                    throw new ValidationException("This sale was already completed with different values. Start a new sale.");
                }
                var paid = sale.Payments.Sum(payment => payment.Amount);
                return new(sale.Id, sale.ReceiptNumber, true, sale.TotalAmount, paid, sale.TotalAmount - paid);
            }
            if (sale is not null && sale.Revision != command.Revision)
            {
                throw new ValidationException("This sale draft changed in another tab. Your entries are kept; resume the latest draft before saving.");
            }
            AccountingCalendar.ValidateExpected(businessDate, command.WorkingDate);
            await AccountingCalendar.EnsureOpenAsync(db, businessDate, ct);
            var effectiveAt = AccountingCalendar.At(businessDate, configuration);
            if (!await db.StockLocations.AnyAsync(location => location.Id == command.LocationId && !location.IsDeleted && location.CanSell, ct)
                || (command.CustomerId is { } validationCustomerId && !await db.Customers.AnyAsync(customer => customer.Id == validationCustomerId && !customer.IsDeleted, ct)))
            {
                throw new ValidationException("Choose an active stock location and customer.");
            }
            var ids = command.Lines.Select(line => line.ProductId).Distinct().ToArray();
            var products = await db.Products.Where(product => ids.Contains(product.Id) && !product.IsDeleted).ToDictionaryAsync(product => product.Id, ct);
            if (products.Count != ids.Length)
            {
                throw new ValidationException("An item is no longer available.");
            }
            var stock = await db.InventoryBalances.Where(balance => balance.StockLocationId == command.LocationId && ids.Contains(balance.ProductId))
                .ToDictionaryAsync(balance => balance.ProductId, ct);
            var now = DateTimeOffset.UtcNow;
            if (sale is null)
            {
                sale = new Sale { ReceiptNumber = number, CashierId = actorId, StockLocationId = command.LocationId, CreatedByUserId = actorId };
                db.Sales.Add(sale);
            }
            else
            {
                db.SaleItems.RemoveRange(sale.Items);
                sale.Items.Clear();
            }
            sale.Revision++;
            sale.RequestHash = hash;
            sale.StockLocationId = command.LocationId;
            sale.CustomerId = command.CustomerId;
            sale.Subtotal = subtotal;
            sale.TotalAmount = total;
            sale.Notes = command.Notes?.Trim();
            sale.TenderedAmount = complete ? paymentPlan.Sum(payment => payment.Tendered) : 0;
            sale.DraftPaymentAmount = 0;
            db.RemoveRange(sale.DraftPayments);
            sale.DraftPayments.Clear();
            if (!complete)
            {
                foreach (var payment in paymentPlan)
                {
                    sale.DraftPayments.Add(new SaleDraftPayment
                    {
                        SaleId = sale.Id,
                        Sale = sale,
                        Method = payment.Method,
                        Amount = payment.Tendered
                    });
                }
            }
            sale.DraftPaymentMethod = command.Method;
            sale.UpdatedAt = now;
            sale.UpdatedByUserId = actorId;
            sale.SoldAt = effectiveAt;
            sale.Status = complete ? SaleStatus.Completed : SaleStatus.Draft;
            foreach (var lineIndex in Enumerable.Range(0, command.Lines.Length).OrderBy(index => command.Lines[index].Quantity > 0))
            {
                var line = command.Lines[lineIndex];
                var product = products[line.ProductId];
                if (!product.AllowFractionUnit && decimal.Truncate(line.Quantity) != line.Quantity)
                {
                    throw new ValidationException($"{product.Name} only allows whole quantities.");
                }
                if (complete && !product.IsNonStocked && !stock.ContainsKey(product.Id))
                {
                    var returnedStock = new InventoryBalance
                    {
                        ProductId = product.Id,
                        StockLocationId = command.LocationId,
                        QuantityOnHand = 0,
                        CreatedByUserId = actorId
                    };
                    db.InventoryBalances.Add(returnedStock);
                    stock.Add(product.Id, returnedStock);
                }
                var item = new SaleItem
                {
                    SaleId = sale.Id,
                    Sale = sale,
                    ProductId = product.Id,
                    ProductName = product.Name,
                    ProductSku = product.Barcode ?? product.Id.ToString(),
                    Unit = product.Unit,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    UnitCost = product.CostPrice,
                    UnitDiscount = unitDiscounts[lineIndex]
                };
                sale.Items.Add(item);
                if (complete && !product.IsNonStocked)
                {
                    var resultingQuantity = stock[product.Id].QuantityOnHand - line.Quantity;
                    if (resultingQuantity is < -999999999999999.999m or > 999999999999999.999m)
                    {
                        throw new ValidationException($"Stock quantity for {product.Name} exceeds the supported range.");
                    }
                    stock[product.Id].QuantityOnHand = resultingQuantity;
                    stock[product.Id].UpdatedAt = now;
                    stock[product.Id].UpdatedByUserId = actorId;
                    db.InventoryMovements.Add(new InventoryMovement
                    {
                        ProductId = product.Id,
                        StockLocationId = command.LocationId,
                        SaleItem = item,
                        Type = line.Quantity < 0 ? InventoryMovementType.SaleReturn : InventoryMovementType.Sale,
                        Quantity = -line.Quantity,
                        UnitCost = product.CostPrice,
                        OccurredAt = effectiveAt,
                        CreatedByUserId = actorId
                    });
                }
            }
            sale.DiscountAmount = decimal.Round(sale.Items.Sum(item => item.UnitDiscount * item.Quantity),
                2, MidpointRounding.AwayFromZero);
            if (complete)
            {
                CustomerBalance? balance = null;
                if (command.CustomerId is { } customerId)
                {
                    balance = await db.CustomerBalances.SingleOrDefaultAsync(item => item.CustomerId == customerId, ct);
                    if (balance is null)
                    {
                        balance = new CustomerBalance { CustomerId = customerId, OpenedAt = effectiveAt };
                        db.CustomerBalances.Add(balance);
                        balance.Entries.Add(new CustomerBalanceEntry
                        {
                            CustomerBalanceId = 0,
                            CustomerBalance = balance,
                            Type = BalanceEntryType.OpeningBalance,
                            OccurredAt = effectiveAt
                        });
                    }
                    balance.CurrentBalance += total;
                    StoreAccess.Money(balance.CurrentBalance, "Customer balance", negative: true);
                    balance.Entries.Add(new CustomerBalanceEntry
                    {
                        CustomerBalanceId = balance.Id,
                        CustomerBalance = balance,
                        Type = BalanceEntryType.Invoice,
                        Amount = total,
                        BalanceAfter = balance.CurrentBalance,
                        Sale = sale,
                        OccurredAt = effectiveAt,
                        CreatedByUserId = actorId
                    });
                    balance.UpdatedAt = now;
                    balance.UpdatedByUserId = actorId;
                }
                for (var paymentIndex = 0; paymentIndex < paymentPlan.Length; paymentIndex++)
                {
                    var allocation = paymentPlan[paymentIndex];
                    var payment = new Payment
                    {
                        Sale = sale,
                        CustomerBalance = balance,
                        Amount = allocation.Amount,
                        TenderedAmount = allocation.Tendered,
                        Method = allocation.Method,
                        Reference = $"SALE-PAY-{command.RequestId:N}-{paymentIndex}",
                        PaidAt = effectiveAt,
                        CreatedByUserId = actorId
                    };
                    sale.Payments.Add(payment);
                    db.Payments.Add(payment);
                    await FinancialPosting.PostAsync(db, actorId, allocation.Method, allocation.Amount,
                        CashMovementKind.Sale, $"Sale · {number}", businessDate, ct, payment: payment, cashLocationId: command.LocationId);
                    if (balance is not null)
                    {
                        balance.CurrentBalance -= allocation.Amount;
                        balance.Entries.Add(new CustomerBalanceEntry
                        {
                            CustomerBalanceId = balance.Id,
                            CustomerBalance = balance,
                            Type = BalanceEntryType.Payment,
                            Amount = -allocation.Amount,
                            BalanceAfter = balance.CurrentBalance,
                            Payment = payment,
                            OccurredAt = effectiveAt,
                            CreatedByUserId = actorId
                        });
                    }
                }
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(sale.Id, number, complete, total, complete ? paidAmount : 0, complete ? total - paidAmount : total) { Revision = sale.Revision };
        }, ct);
    }

    public async Task<SalesDraft?> DraftAsync(ClaimsPrincipal actor, Guid requestId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        var number = $"SAL-{requestId:N}";
        var sale = await db.Sales.AsNoTracking().Include(item => item.DraftPayments).Include(item => item.Items).ThenInclude(item => item.Product)
            .SingleOrDefaultAsync(item => !item.IsDeleted && item.ReceiptNumber == number && item.CashierId == actorId && item.Status == SaleStatus.Draft, ct);
        if (sale is null)
        {
            return null;
        }
        var ids = sale.Items.Select(item => item.ProductId).ToArray();
        var stock = await db.InventoryBalances.Where(balance => balance.StockLocationId == sale.StockLocationId && ids.Contains(balance.ProductId))
            .ToDictionaryAsync(balance => balance.ProductId, balance => balance.QuantityOnHand, ct);
        return new(requestId, sale.StockLocationId, sale.CustomerId,
            sale.Items.Select(item => new DraftLine(new SalesProduct(item.ProductId, item.ProductName, item.ProductSku, item.Unit,
                item.UnitPrice, stock.GetValueOrDefault(item.ProductId), item.Product!.AllowFractionUnit, item.Product.IsNonStocked), item.Quantity, item.UnitPrice)).ToArray(),
            sale.DiscountAmount, sale.DraftPayments.Count > 0 ? sale.DraftPayments.Sum(payment => payment.Amount) : sale.DraftPaymentAmount, sale.DraftPaymentMethod, sale.Notes)
        {
            Revision = sale.Revision,
            Payments = sale.DraftPayments.Count > 0
                ? sale.DraftPayments.OrderBy(payment => payment.Id).Select(payment => new PaymentInput(payment.Method, payment.Amount)).ToArray()
                : sale.DraftPaymentAmount > 0 ? [new(sale.DraftPaymentMethod, sale.DraftPaymentAmount)] : []
        };
    }

    public async Task<SaleBill?> BillAsync(ClaimsPrincipal actor, int id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        var canViewOthers = await db.UserRoles.AnyAsync(membership => membership.UserId == actorId && db.Roles.Any(role =>
            role.Id == membership.RoleId && (role.Name == AppRoles.Admin || role.Name == AppRoles.InventoryManager)), ct);
        var sale = await db.Sales.AsNoTracking().Include(item => item.Items).Include(item => item.Payments)
            .Include(item => item.Customer).Include(item => item.StockLocation).Include(item => item.Cashier)
            .SingleOrDefaultAsync(item => !item.IsDeleted && item.Id == id && item.Status == SaleStatus.Completed && (canViewOthers || item.CashierId == actorId), ct);
        return sale is null ? null : new(sale.Id, sale.ReceiptNumber, sale.SoldAt, sale.Customer?.Name,
            sale.StockLocation!.Name, sale.Cashier!.UserName!, sale.Items.Select(item => new BillLine(item.ProductName, item.ProductSku,
                item.Unit, item.Quantity, item.UnitPrice,
                decimal.Round(item.UnitPrice * item.Quantity, 2, MidpointRounding.AwayFromZero))).ToArray(), sale.Subtotal, sale.DiscountAmount,
            sale.TotalAmount, sale.Payments.Sum(payment => payment.Amount), sale.TenderedAmount, sale.Notes)
        {
            Payments = sale.Payments.OrderBy(payment => payment.Id).Select(payment =>
                new PaymentDetail(payment.Method, payment.Amount, payment.TenderedAmount == 0 ? payment.Amount : payment.TenderedAmount)).ToArray()
        };
    }
}
