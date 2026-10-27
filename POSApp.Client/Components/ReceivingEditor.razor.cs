using POSApp.Client.Enums;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using POSApp.Client.DTOs;
using POSApp.Client.Services;

namespace POSApp.Client.Components;

public partial class ReceivingEditor
{
    private EditorDraft<ReceivingEditorState>? Recovery;
    private ReceivingEditorState DraftState => new(RequestId, SupplierId, LocationId, Options.WorkingDate,
        Invoice, AllowDuplicateInvoice, Notes, Payments.ToArray(), Lines.Select(line => new ReceivingEditorLine(
            line.Product, line.Quantity, line.Cost, line.QuantityValid, line.CostValid)).ToArray());

    private void RestoreDraft(ReceivingEditorState draft)
    {
        if (draft.RequestId == Guid.Empty || draft.Lines is null || draft.Lines.Length > 1000 || draft.Payments is null
            || draft.Lines.Any(line => line is null || line.Product is null))
        {
            throw new InvalidOperationException("This saved draft is incompatible. It has been kept; start a new draft or use the server copy.");
        }
        RequestId = draft.RequestId;
        SupplierId = draft.SupplierId;
        LocationId = draft.LocationId;
        if (draft.WorkingDate != Options.WorkingDate)
        {
            Error = "Draft recovered from a different working day. Review the current day before submitting.";
        }
        Invoice = draft.Invoice;
        AllowDuplicateInvoice = draft.AllowDuplicateInvoice;
        Notes = draft.Notes;
        Payments = draft.Payments;
        Lines = draft.Lines.Select(line => new CartLine
        {
            Product = line.Product,
            Quantity = line.Quantity,
            Cost = line.Cost,
            QuantityValid = line.QuantityValid,
            CostValid = line.CostValid
        }).ToList();
    }

    [Parameter, EditorRequired]
    public ReceivingOptions InitialOptions { get; set; } = default!;

    [Parameter]
    public Lookup[] InitialCategories { get; set; } = [];

    [Parameter]
    public string Currency { get; set; } = "BDT";

    private ReceivingOptions Options = default!;
    private IReadOnlyList<Lookup> Categories = [];
    private bool Busy;
    private bool DraftReady;
    private bool Searching;
    private bool ReturnMode;
    private bool DialogBusy;
    private string? Error;
    private string? Dialog;
    private string? DialogError;
    private string? Invoice;
    private bool AllowDuplicateInvoice;
    private string? Notes;
    private string? Phone;
    private string Search = "";
    private string PartyName = "";
    private int SupplierId;
    private int LocationId;
    private IReadOnlyList<PaymentInput> Payments = [];
    private decimal Paid => Payments.Sum(payment => payment.Amount);
    private decimal OpeningDue;
    private bool OpeningDueValid = true;
    private Guid RequestId = Guid.NewGuid();
    private ReceiveResult? Completed;
    private ProductInput NewItem = new();
    private List<CartLine> Lines = [];
    private IReadOnlyList<ItemRow> Results = [];
    private CancellationTokenSource? SearchCancellation;
    private ElementReference DialogElement;
    private ElementReference DialogCloseButton;
    private ElementReference DialogSubmitButton;
    private bool FocusDialog;

    private SupplierOption? SelectedSupplier => Options.Suppliers.FirstOrDefault(supplier => supplier.Id == SupplierId);

    private decimal Total => Lines.Sum(line => line.Total);

    private bool ReceiveDisabled => Lines.Count == 0 || SupplierId == 0 || LocationId == 0
        || Paid > Math.Max(Total, 0) || Math.Abs(Total) > 9999999999999999.99m
        || Lines.Any(line => !line.IsValid);

    protected override void OnInitialized()
    {
        // The SSR page supplies serializable parameters to both Auto renderers.
        // No database/API load runs again when the component becomes interactive.
        Options = InitialOptions;
        Categories = InitialCategories;
        if (Options.Locations.Count == 1)
        {
            LocationId = Options.Locations[0].Id;
        }
    }

    private readonly Dictionary<string, IReadOnlyList<ItemRow>> SearchCache = new(StringComparer.OrdinalIgnoreCase);

    private async Task SearchChanged()
    {
        SearchCancellation?.Cancel();
        SearchCancellation?.Dispose();
        SearchCancellation = new();
        var cancellationToken = SearchCancellation.Token;

        var term = Search.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            Results = [];
            Searching = false;
            return;
        }

        if (SearchCache.TryGetValue(term, out var cached))
        {
            Results = cached;
            Searching = false;
            return;
        }

        Searching = true;
        try
        {
            await Task.Delay(200, cancellationToken);
            var found = (await Inventory.SearchAsync(term, cancellationToken)).Where(product => !product.IsNonStocked).ToArray();
            if (!cancellationToken.IsCancellationRequested)
            {
                if (SearchCache.Count > 100)
                {
                    SearchCache.Clear();
                }
                SearchCache[term] = found;
                Results = found;
            }
        }
        catch (OperationCanceledException)
        {
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            Searching = false;
        }
    }

    private void AddLine(ItemRow item)
    {
        if (item.IsNonStocked)
        {
            Error = "Non-stocked items cannot be received.";
            return;
        }
        if (SupplierId == 0)
        {
            SupplierId = item.SupplierId;
        }
        var current = Lines.FirstOrDefault(line => line.Product.Id == item.Id && (line.Quantity < 0) == ReturnMode);
        if (current is null)
        {
            Lines.Insert(0, new CartLine
            {
                Product = item,
                Quantity = ReturnMode ? -1 : 1,
                Cost = item.Cost ?? 0
            });
        }
        else
        {
            current.Quantity += ReturnMode ? -1 : 1;
            Lines.Remove(current);
            Lines.Insert(0, current);
        }

        SearchCancellation?.Cancel();
        Search = "";
        Results = [];
        Searching = false;
    }

    private void OpenItem()
    {
        NewItem = new() { Name = Search, SupplierId = SupplierId };
        OpenDialog("item");
    }

    private void OpenSupplier()
    {
        PartyName = "";
        Phone = null;
        OpeningDue = 0;
        OpeningDueValid = true;
        OpenDialog("supplier");
    }

    private void OpenLocation()
    {
        PartyName = "";
        OpenDialog("location");
    }

    private void OpenDialog(string name)
    {
        Dialog = name;
        DialogError = null;
        FocusDialog = true;
    }

    private void CloseDialog()
    {
        if (!DialogBusy)
        {
            Dialog = null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!FocusDialog || Dialog is null)
        {
            return;
        }

        FocusDialog = false;
        await FocusDialogFirstAsync();
    }

    private Task FocusDialogFirstAsync()
    {
        var target = DialogBusy ? DialogElement : DialogCloseButton;
        return target.FocusAsync(preventScroll: true).AsTask();
    }

    private Task FocusDialogLastAsync()
    {
        var target = DialogBusy || (Dialog == "supplier" && !OpeningDueValid)
            ? DialogElement
            : DialogSubmitButton;
        return target.FocusAsync(preventScroll: true).AsTask();
    }

    private void HandleDialogKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseDialog();
        }
    }

    private async Task SaveItem()
    {
        if (DialogBusy)
        {
            return;
        }

        DialogBusy = true;
        DialogError = null;
        try
        {
            var item = await Inventory.SaveProductAsync(NewItem);
            AddLine(item);
            Dialog = null;
        }
        catch (Exception exception)
        {
            DialogError = Message(exception);
            Logger.LogError(exception, "Quick item failed.");
        }
        finally
        {
            DialogBusy = false;
        }
    }

    private async Task SaveParty()
    {
        if (DialogBusy || (Dialog == "supplier" && !OpeningDueValid))
        {
            return;
        }

        DialogBusy = true;
        DialogError = null;
        try
        {
            if (Dialog == "supplier")
            {
                SupplierId = await Inventory.AddSupplierAsync(new(PartyName, Phone, OpeningDue));
            }
            else
            {
                LocationId = await Inventory.AddLocationAsync(new(PartyName));
            }

            Options = await Inventory.OptionsAsync();
            Dialog = null;
        }
        catch (Exception exception)
        {
            DialogError = Message(exception);
            Logger.LogError(exception, "Quick supplier/location failed.");
        }
        finally
        {
            DialogBusy = false;
        }
    }

    private async Task Receive()
    {
        if (Busy || Completed is not null || ReceiveDisabled)
        {
            return;
        }

        Busy = true;
        Error = null;
        try
        {
            var lines = Lines.Select(line => new ReceiveLine(line.Product.Id, line.Quantity, line.Cost)).ToArray();
            var command = new ReceiveCommand(RequestId, SupplierId, LocationId, lines, 0, PaymentMethod.Cash, Invoice, Notes)
            {
                AllowDuplicateInvoice = AllowDuplicateInvoice,
                WorkingDate = Options.WorkingDate,
                Payments = Payments.ToArray()
            };
            Completed = await Inventory.ReceiveAsync(command);
            Options = await Inventory.OptionsAsync();
        }
        catch (Exception exception)
        {
            Report(exception);
        }
        finally
        {
            Busy = false;
        }
    }

    private void NewReceipt()
    {
        SupplierId = 0;
        ReturnMode = false;
        SearchCancellation?.Cancel();
        Searching = false;
        Lines = [];
        Payments = [];
        Invoice = null;
        AllowDuplicateInvoice = false;
        Notes = null;
        Search = "";
        Results = [];
        Completed = null;
        Error = null;
        RequestId = Guid.NewGuid();
    }

    private void Report(Exception exception)
    {
        Error = Message(exception);
        Logger.LogError(exception, "Receiving operation failed.");
    }

    private static string Message(Exception exception)
    {
        return exception switch
        {
            ValidationException or InventoryRequestException => exception.Message,
            UnauthorizedAccessException => "Your account no longer has inventory access.",
            _ => "Unable to save or load. Please try again; the same receipt won't be recorded twice."
        };
    }

    private string Money(decimal value)
    {
        return $"{value:N2} {Currency}";
    }

    public void Dispose()
    {
        SearchCancellation?.Cancel();
        SearchCancellation?.Dispose();
    }

    private sealed class CartLine
    {
        public Guid Key { get; } = Guid.NewGuid();
        public required ItemRow Product { get; init; }
        public decimal Quantity { get; set; }
        public decimal Cost { get; set; }
        public bool QuantityValid { get; set; } = true;
        public bool CostValid { get; set; } = true;

        public bool IsValid => QuantityValid && CostValid && Quantity != 0 && Quantity >= -999999999999999.999m && Quantity <= 999999999999999.999m
            && (Product.AllowFractionUnit || decimal.Truncate(Quantity) == Quantity)
            && decimal.Round(Quantity, 3) == Quantity && Cost >= 0 && Cost <= 9999999999999999.99m
            && decimal.Round(Cost, 2) == Cost && (Cost == 0 || Math.Abs(Quantity) <= 9999999999999999.99m / Cost);

        public decimal Total => IsValid ? decimal.Round(Quantity * Cost, 2, MidpointRounding.AwayFromZero) : 0;
    }
}
