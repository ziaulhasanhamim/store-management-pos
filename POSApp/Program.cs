using POSApp.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSApp.Components;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Security;
using POSApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextPool<AppDbContext>(o => o.UseNpgsql(builder.Configuration["DB_URL"] ?? builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Set DB_URL or ConnectionStrings:DefaultConnection to your PostgreSQL connection string.")));

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = IdentityConstants.ApplicationScheme;
        o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
    {
        o.LoginPath = "/account/login";
        o.AccessDeniedPath = "/account/access-denied";
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = 401;
            }
            else
            {
                ctx.Response.Redirect(ctx.RedirectUri);
            }
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = 403;
            }
            else
            {
                ctx.Response.Redirect(ctx.RedirectUri);
            }
            return Task.CompletedTask;
        };
    });

builder.Services.AddIdentityCore<User>(o =>
    {
        // Accounts sign in with usernames; an email address is optional.
        o.User.RequireUniqueEmail = false;
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<int>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddPosAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddPosIdentityValidation();
builder.Services.AddScoped<UserAdministration>();
builder.Services.AddInventoryUi();
builder.Services.AddHostedService<ImportWorker>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    var previous = WorkingDayRequest.Date;
    try
    {
        if (HttpMethods.IsPost(context.Request.Method) && context.Request.HasFormContentType
            && !context.Request.Path.StartsWithSegments("/account"))
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (DateOnly.TryParseExact(form["__WorkingDay"], "yyyy-MM-dd", out var expected))
            {
                WorkingDayRequest.Date = expected;
            }
        }
        await next(context);
    }
    finally
    {
        WorkingDayRequest.Date = previous;
    }
});

app.MapHealthChecks("/health");
app.MapGroup("/api/inventory").MapInventoryEndpoints();
app.MapGroup("/api/sales").MapSalesEndpoints();
app.MapGroup("/api/finance").MapFinanceEndpoints();
app.MapGroup("/api/entries").MapMoneyEntryEndpoints();
app.MapGroup("/api/imports").MapImportEndpoints();
app.MapGroup("/api/drafts").MapDraftEndpoints();
app.MapGroup("/api/reports").MapReportEndpoints();
// Public CSS, JavaScript and Auto's WebAssembly downloads don't need cookie validation.
app.MapStaticAssets().ShortCircuit();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(POSApp.Client._Imports).Assembly);

if (app.Configuration.GetValue("AutoMigrate", false) ||
    string.Equals(Environment.GetEnvironmentVariable("AUTO_MIGRATE"), "true", StringComparison.OrdinalIgnoreCase))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var dbContext = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

await AdminSeeder.SeedAsync(app.Services, app.Configuration);

if (app.Environment.IsDevelopment())
{
    await DevelopmentDataSeeder.SeedAsync(app.Services, app.Environment, app.Configuration);
}

app.Run();

public partial class Program { }
