using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace POSApp.Tests.Integration.Infrastructure;

public sealed class TestAntiforgery : IAntiforgery
{
    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) =>
        new("test-request-token", "test-cookie-token", "X-CSRF-TOKEN", "test-form-token");

    public AntiforgeryTokenSet GetTokens(HttpContext httpContext) =>
        new("test-request-token", "test-cookie-token", "X-CSRF-TOKEN", "test-form-token");

    public Task<bool> IsRequestValidAsync(HttpContext httpContext) =>
        Task.FromResult(true);

    public void SetCookieTokenAndHeader(HttpContext httpContext)
    {
    }

    public Task ValidateRequestAsync(HttpContext httpContext) =>
        Task.CompletedTask;
}
