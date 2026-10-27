using Microsoft.AspNetCore.Components;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Services;

namespace POSApp.Client.Components;

public partial class MoneyEntryEditor
{
    private LocalDraft<MoneyEditorState>? Recovery;
    private MoneyEditorState DraftState => new(BatchId, Data.WorkingDate,
        Lines.Select(line => new MoneyEditorLine(line.Input, line.ValidAmount, line.FixedParty)).ToArray());

    private void RestoreDraft(MoneyEditorState draft)
    {
        if (draft.BatchId == Guid.Empty || draft.Lines is null || draft.Lines.Length > 100
            || draft.Lines.Any(line => line is null || line.Input is null))
        {
            throw new InvalidOperationException("This saved draft is incompatible. It has been kept; start a new draft or use the server copy.");
        }
        BatchId = draft.BatchId;
        if (Data.WorkingDate != draft.WorkingDate)
        {
            Error = "Draft recovered from a different working day. Review the current day before submitting.";
        }
        Lines = draft.Lines.Select(line => new DraftLine
        {
            Input = line.Input,
            ValidAmount = line.ValidAmount,
            FixedParty = line.FixedParty
        }).ToList();
    }

    [Parameter, EditorRequired]
    public MoneyEntryPage InitialData { get; set; } = default!;

    [Parameter]
    public int? InitialPartyId { get; set; }

    [Parameter]
    public string Currency { get; set; } = "BDT";

    private MoneyEntryPage Data = default!;
    private List<DraftLine> Lines = [];
    private Guid BatchId = Guid.NewGuid();
    private string? Error;
    private string? Notice;
    private string NewCategory = "";
    private bool Busy;
    private bool LoadingRows;
    private bool DraftReady = true;
    private IReadOnlySet<int> PendingIds => Lines
        .Where(line => line.Input.Id.HasValue)
        .Select(line => line.Input.Id!.Value)
        .ToHashSet();

    private bool IsExpense => Data.Kind == "expenses";
    private bool IsSupplier => Data.Kind == "supplier-payments";
    private string Label => IsExpense ? "Expenses" : IsSupplier ? "Supplier payments" : "Customer collections";
    private bool SaveDisabled => Lines.Count is 0 or > 100 || Lines.Any(line => !line.ValidAmount
        || line.Input.Amount <= 0 || (IsExpense ? line.Input.CategoryId == 0 : line.Input.PartyId == 0));

    protected override void OnInitialized()
    {
        Data = InitialData;
        AddLine();
        Lines[0].Input.PartyId = InitialPartyId ?? 0;
    }

    private async Task ChangePageAsync(int page)
    {
        if (Busy || LoadingRows)
        {
            return;
        }
        LoadingRows = true;
        Error = null;
        try
        {
            Data = await Entries.ReadAsync(Data.Kind, Data.Date, page, Data.Pagination?.Size ?? 30);
        }
        catch (Exception exception)
        {
            Error = Message(exception);
        }
        finally
        {
            LoadingRows = false;
        }
    }

    private void ResetDraft()
    {
        Error = null;
        Notice = null;
        BatchId = Guid.NewGuid();
        Lines = [];
        AddLine();
    }

    private void AddLine()
    {
        var last = Lines.LastOrDefault()?.Input;
        Lines.Add(new DraftLine
        {
            Input = new MoneyEntryInput
            {
                PartyId = last?.PartyId ?? 0,
                CategoryId = Data.Categories.Any(category => category.Id == last?.CategoryId && !category.IsArchived) ? last!.CategoryId : 0,
                Method = last?.Method ?? PaymentMethod.Cash
            }
        });
    }

    private void Edit(MoneyEntryRow row)
    {
        if (Busy || !row.CanEdit || Lines.Any(line => line.Input.Id == row.Id))
        {
            return;
        }
        Lines.RemoveAll(line => line.Input.Id is null && line.Input.Amount == 0 && line.Input.Notes.Length == 0);
        Lines.Add(new DraftLine
        {
            FixedParty = row.PurchaseId is not null,
            Input = new MoneyEntryInput
            {
                Id = row.Id,
                Revision = row.Revision,
                PartyId = row.PartyId,
                CategoryId = row.CategoryId,
                Amount = row.Amount,
                Method = row.Method,
                Notes = row.Notes,
                Receipt = row.Receipt
            }
        });
        Notice = $"Editing entry #{row.Id}. Save to apply the correction.";
    }

    private async Task Save()
    {
        if (Busy || SaveDisabled)
        {
            return;
        }
        Busy = true;
        Error = null;
        Notice = null;
        var committed = false;
        try
        {
            var result = await Entries.SaveAsync(Data.Kind, new MoneyEntryCommand(BatchId, Data.WorkingDate,
                Lines.Select(line => line.Input).ToArray()));
            committed = true;
            BatchId = Guid.NewGuid();
            Lines = [];
            AddLine();
            Notice = $"Saved {result.Ids.Count} {(result.Ids.Count == 1 ? "entry" : "entries")}. Ready for a new entry.";
            Data = await Entries.ReadAsync(Data.Kind, Data.Date, Data.Pagination?.Number ?? 1, Data.Pagination?.Size ?? 30);
        }
        catch (Exception exception)
        {
            Error = committed ? "Entries were saved, but the history could not refresh. Use the refresh button below." : Message(exception);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task AddCategory()
    {
        if (Busy || string.IsNullOrWhiteSpace(NewCategory))
        {
            return;
        }
        Busy = true;
        Error = null;
        try
        {
            var id = await Entries.SaveCategoryAsync(new(null, NewCategory));
            Data = await Entries.ReadAsync(Data.Kind, Data.Date, Data.Pagination?.Number ?? 1, Data.Pagination?.Size ?? 30);
            var empty = Lines.FirstOrDefault(line => line.Input.CategoryId == 0);
            if (empty is not null)
            {
                empty.Input.CategoryId = id;
            }
            NewCategory = "";
        }
        catch (Exception exception)
        {
            Error = Message(exception);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task Refresh()
    {
        if (Busy)
        {
            return;
        }
        Busy = true;
        Error = null;
        try
        {
            Data = await Entries.ReadAsync(Data.Kind, Data.Date, Data.Pagination?.Number ?? 1, Data.Pagination?.Size ?? 30);
        }
        catch (Exception exception)
        {
            Error = Message(exception);
        }
        finally
        {
            Busy = false;
        }
    }

    private static string Message(Exception exception) => exception switch
    {
        System.ComponentModel.DataAnnotations.ValidationException or InventoryRequestException => exception.Message,
        UnauthorizedAccessException => "Your account no longer has access to this operation.",
        _ => "Unable to complete the request. Your pending entries are kept; retrying won't duplicate them."
    };

    private string Money(decimal amount) => $"{amount:N2} {Currency}";

    private sealed class DraftLine
    {
        public required MoneyEntryInput Input { get; init; }
        public bool ValidAmount { get; set; } = true;
        public bool FixedParty { get; init; }
    }
}
