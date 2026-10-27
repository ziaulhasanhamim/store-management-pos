using Microsoft.AspNetCore.Components;
using POSApp.Client.DTOs;
using POSApp.Client.Services;

namespace POSApp.Client.Components;

public partial class TransferEditor
{
    [Parameter]
    public DateOnly? WorkingDate { get; set; }

    private LocalDraft<TransferEditorState>? Recovery;
    private TransferEditorState DraftState => new(RequestId, SourceId, DestinationId, WorkingDate,
        Notes, Lines.Select(line => new TransferEditorLine(line.Item, line.Quantity, line.Valid)).ToArray());

    private void RestoreDraft(TransferEditorState draft)
    {
        if (draft.RequestId == Guid.Empty || draft.Lines is null || draft.Lines.Length > 200
            || draft.Lines.Any(line => line is null || line.Product is null))
        {
            throw new InvalidOperationException("This saved draft is incompatible. It has been kept; start a new draft or use the server copy.");
        }
        RequestId = draft.RequestId;
        SourceId = draft.SourceId;
        DestinationId = draft.DestinationId;
        if (WorkingDate != draft.WorkingDate)
        {
            Error = "Draft recovered from a different working day. Review the current day before submitting.";
        }
        Notes = draft.Notes;
        Lines = draft.Lines.Select(line => new Line
        {
            Item = line.Product,
            Quantity = line.Quantity,
            Valid = line.Valid
        }).ToList();
    }

    [Parameter, EditorRequired]
    public IReadOnlyList<Lookup> Locations { get; set; } = [];

    private int SourceId;
    private int DestinationId;
    private string Search = "";
    private string Notes = "";
    private string? Error;
    private bool Searching;
    private bool Busy;
    private bool DraftReady = true;
    private Guid RequestId = Guid.NewGuid();
    private TransferResult? Completed;
    private List<Line> Lines = [];
    private IReadOnlyList<ItemRow> Results = [];
    private CancellationTokenSource? SearchCancellation;
    private bool Disabled => SourceId == 0 || DestinationId == 0 || SourceId == DestinationId
        || Lines.Count == 0 || Lines.Count > 200 || Lines.Any(line => !line.IsValid);

    private string LocationName(int id) => Locations.FirstOrDefault(location => location.Id == id)?.Name ?? "—";

    private void SourceChanged()
    {
        Reset();
        if (DestinationId == SourceId)
        {
            DestinationId = 0;
        }
    }

    private async Task SearchChanged()
    {
        SearchCancellation?.Cancel();
        SearchCancellation?.Dispose();
        SearchCancellation = new();
        var token = SearchCancellation.Token;
        var source = SourceId;
        Results = [];
        Searching = !string.IsNullOrWhiteSpace(Search) && source != 0;
        if (!Searching)
        {
            return;
        }
        try
        {
            await Task.Delay(250, token);
            var found = await Inventory.SearchAsync(Search, token, source);
            if (!token.IsCancellationRequested)
            {
                Results = found;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Error = exception is InventoryRequestException ? exception.Message : "Unable to search products. Please try again.";
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                Searching = false;
            }
        }
    }

    private void Add(ItemRow item)
    {
        var existing = Lines.FirstOrDefault(line => line.Item.Id == item.Id);
        if (existing is null)
        {
            Lines.Insert(0, new Line { Item = item, Quantity = Math.Min(1, item.Stock) });
        }
        else
        {
            if (existing.Quantity + 1 <= item.Stock)
            {
                existing.Quantity += 1;
            }
            Lines.Remove(existing);
            Lines.Insert(0, existing);
        }
        SearchCancellation?.Cancel();
        Searching = false;
        Search = "";
        Results = [];
    }

    private async Task Save()
    {
        if (Busy || Disabled || Completed is not null)
        {
            return;
        }
        Busy = true;
        Error = null;
        try
        {
            Completed = await Inventory.TransferAsync(new TransferCommand(RequestId, SourceId, DestinationId,
                Lines.Select(line => new TransferLine(line.Item.Id, line.Quantity)).ToArray(), Notes)
            {
                WorkingDate = WorkingDate
            });
        }
        catch (Exception exception)
        {
            Error = exception switch
            {
                System.ComponentModel.DataAnnotations.ValidationException or InventoryRequestException => exception.Message,
                UnauthorizedAccessException => "Your account no longer has inventory access.",
                _ => "Unable to save the transfer. Try again; the same transfer won't be recorded twice."
            };
        }
        finally
        {
            Busy = false;
        }
    }

    private void Reset()
    {
        SearchCancellation?.Cancel();
        Lines = [];
        Results = [];
        Search = "";
        Notes = "";
        Searching = false;
        Completed = null;
        Error = null;
        RequestId = Guid.NewGuid();
    }

    public void Dispose()
    {
        SearchCancellation?.Cancel();
        SearchCancellation?.Dispose();
    }

    private sealed class Line
    {
        public required ItemRow Item { get; init; }
        public decimal Quantity { get; set; }
        public bool Valid { get; set; } = true;
        public bool IsValid => Valid && Quantity > 0 && Quantity <= Item.Stock
            && decimal.Round(Quantity, 3) == Quantity && (Item.AllowFractionUnit || decimal.Truncate(Quantity) == Quantity);
    }
}
