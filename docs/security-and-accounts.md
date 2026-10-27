# Accounts and access

The app targets .NET 11. Login, logout, and user management are statically server-rendered Blazor pages with enhanced forms/navigation. Identity cookie writes happen in HTTP requests. No interactive render mode is needed for these pages.

Public static assets use `MapStaticAssets().ShortCircuit()` so CSS, JavaScript, and Interactive Auto's WebAssembly downloads bypass cookie authentication middleware. Page, API, and SignalR endpoints retain their authentication pipeline. Auto's initial server renderer maintains a SignalR connection; WebSocket keep-alive frames do not run a new HTTP cookie validation.

The users list streams its loading state and then its data. On the edit page, only the GET data-loader component streams; form POSTs remain buffered so self-edit cookie refreshes and redirects can complete before response headers are sent. New-user pages don't load existing user details. Abandoned reads use the request cancellation token. Read failures show a retry link.

`wwwroot/POSApp.lib.module.js` is a Blazor JavaScript initializer. It provides a navigation progress bar for all enhanced navigation and pending button text for enhanced form submissions. New asynchronous read pages can reuse `LoadingPanel` inside a `[StreamRendering]` component; authentication and mutation handlers should remain buffered.

Form feedback is opt-in: set `data-submit-indication="true"` on an enhanced form to enable submit-button disabling, pending text, spinner visibility, and the busy cursor. If this attribute is missing or `"false"`, the form's controls and spinner are left untouched. The app-wide navigation bar operates independently. The wait cursor defaults to enabled for opted-in busy forms; set `data-pending-cursor="false"` to opt out.

`data-spinner-selector` selects developer-provided indicator elements; JavaScript never generates spinner markup. Selectors are scoped to the form first, with a document-level fallback for external indicators. Give the indicators the `hidden` attribute initially (not a Tailwind `hidden` class); their original hidden states are restored on completion. An omitted selector means no spinner changes. Invalid selectors are ignored with a console warning. Place indicators outside the button's `[data-submit-label]` span so pending text doesn't remove them. For plain-text buttons the helper can update a direct text node instead.

Login, logout confirmation, and user editor forms opt in and contain their own spinner spans. The compact header logout form opts out. `UserEditorForm` exposes `SubmitIndication`, `UseWaitCursor`, and `SpinnerSelector`. Pending text is supplied by the button's `data-pending-text`. The helper preserves spinner/icon elements, restores the button's original label/disabled state, and suppresses repeat submissions while an opted-in form is busy.

The top bar starts at a small width, gradually advances while enhanced navigation or form submission is in progress, reaches 100% on `enhancednavigationend`, then fades out. It estimates progress because Blazor doesn't expose a total byte count or database completion percentage. Its timers reset on successive navigation, and reduced-motion preferences are respected.

## Initial setup

Configure the PostgreSQL connection using `DB_URL` or `ConnectionStrings:DefaultConnection`. Provision the schema before starting the app. If no migrations exist, generate and review an initial migration first:

```sh
dotnet ef migrations add InitialPosSchema --project POSApp
dotnet ef database update --project POSApp
```

Configure `AdminUser:Username` and `AdminUser:Password`. The checked-in username is `admin`, and the password is intentionally empty. Supply the password through .NET user secrets or an environment variable (`AdminUser__Password`); do not commit a real password to appsettings. Set `AdminUser__Username` to override the username.

On startup, the seeder creates the three roles and the initial admin from configuration. Passwords use ASP.NET Identity hashing. Existing admin passwords are never reset by startup. Once any admin exists, a renamed bootstrap account is not recreated. A configured username that belongs to a non-admin is rejected rather than silently promoted. Set `AdminUser:Enabled` to `false` to disable automatic provisioning after setup, or when accounts are provisioned externally.

In the Development environment, startup also loads [`Data/development-seed.json`](../Data/development-seed.json) after admin provisioning. It adds sample categories, stock locations, suppliers, customers, products, opening stock movements, and opening balance entries. Records already matching a category name, location code, supplier/customer name, or product barcode are left untouched, so repeated starts do not add duplicate stock or dues or overwrite edits. No demo users or passwords are created. The database schema must already exist. Set `DevelopmentSeed:Enabled` to `false` in development configuration to skip sample data. The development seed never runs in other environments.

Visit `/account/login`. Usernames and passwords are required. Five failed attempts trigger temporary lockout. Sign-out uses an antiforgery-protected POST. Post-login redirects are restricted to local paths.

## Roles and policies

- `Admin`: manage users, inventory, cost prices, and sales.
- `InventoryManager` (Inventory Manager / Store Manager): receive stock, view cost prices, and make sales.
- `SalesAssociate`: sales only.

The policies are `ManageUsers`, `ManageInventory`, `ViewCostPrice`, and `MakeSales`. The `/users`, `/users/new`, and `/users/{id}/edit` routes require `ManageUsers`. The administration service also checks the acting user's current database role before reading or changing accounts.

Admins can create accounts and update usernames, roles, and passwords. Accounts managed through this UI have one assigned role. Leaving an edit password blank preserves it. Password reset also clears lockout. Admins cannot remove their own admin role, and at least one admin must remain. Each save commits account/password/role changes in one transaction.

Credential or permission edits update the security stamp. Cookie security stamps are revalidated on the next HTTP request after the five-minute validation interval, avoiding repeated Identity claims queries on each request or SignalR poll. Inventory and user-administration services check current database roles on every operation, so role revocation takes effect there immediately. Password changes invalidate other cookies at their next interval check; an established server circuit's principal isn't refreshed merely by changing the cookie interval. Editing your own account refreshes your cookie. No public registration or account deletion is provided.

Items and dashboard are available to signed-in store roles. Only admins and inventory managers can edit items, receive stock, and see purchase costs or supplier balances. InventoryService rechecks current database roles for every operation; sales-only projections omit cost data. Receiving streams its SSR shell, then uses an Interactive Auto client component for search, cart editing, and Blazor quick-add dialogs. Authorized inventory API endpoints serve the WebAssembly render mode. Catalog lists/details and item editors retain SSR, enhanced navigation/forms and streamed loading states. Checkout supports split payments, held sales, customer dues and returns. Transaction retries, working-day validation and revisioned recovery drafts are described in `Services/Finance.README.md`.

## Development

Tailwind source is `wwwroot/css/main.css`; the generated stylesheet is `wwwroot/tailwind.css`. From the repository root:

```sh
npm run css:build
dotnet build POSApp.sln
dotnet run --project POSApp.Persistence.Tests
```

Tests use an isolated SQLite database for Identity provisioning, user edits, password resets, policies, rollback, cookies, and lockout. A local test server exercises the real SSR login, user forms, logout, enhanced-navigation headers, and antiforgery checks. Gated database reads verify that loading HTML arrives before the data query completes, followed by the loaded list or form. They also verify PostgreSQL model/schema generation. No production database is touched.
