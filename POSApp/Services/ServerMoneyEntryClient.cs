using POSApp.Client.Services;

namespace POSApp.Services;

public sealed class ServerMoneyEntryClient : IMoneyEntryClient, IDisposable
{
    private readonly MoneyEntryService entries;
    private readonly AuthenticationStateProvider authentication;
    private Task<AuthenticationState>? state;

    public ServerMoneyEntryClient(MoneyEntryService entries, AuthenticationStateProvider authentication)
    {
        this.entries = entries;
        this.authentication = authentication;
        authentication.AuthenticationStateChanged += StateChanged;
    }

    private void StateChanged(Task<AuthenticationState> current)
    {
        state = current;
    }

    private Task<AuthenticationState> StateAsync() => state ?? authentication.GetAuthenticationStateAsync();

    public async Task<MoneyEntryPage> ReadAsync(string kind, DateOnly? date = null, int page = 1, int pageSize = 30)
    {
        return await entries.ReadAsync((await StateAsync()).User, kind, date, page: page, pageSize: pageSize);
    }

    public async Task<MoneyEntryResult> SaveAsync(string kind, MoneyEntryCommand input)
    {
        return await entries.SaveAsync((await StateAsync()).User, kind, input);
    }

    public async Task<int> SaveCategoryAsync(ExpenseCategoryInput input)
    {
        return await entries.SaveCategoryAsync((await StateAsync()).User, input);
    }

    public void Dispose()
    {
        authentication.AuthenticationStateChanged -= StateChanged;
    }
}
