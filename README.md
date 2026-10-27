# RetailPOS · Store Management & Point of Sale

A modern, high-performance, multi-store Point of Sale and Retail Management System engineered with **.NET 11**, **Blazor Web App** (Server-Side Stream Rendering + Interactive WebAssembly), **Entity Framework Core**, **PostgreSQL**, and **Tailwind CSS v4**.

Designed for single-store and multi-location retail businesses with full support for low-latency barcode checkout, touch-friendly mobile operation, inventory replenishment, cash drawer reconciliations, and daily/monthly financial closings.

---

## Key Features

### 1. Executive Command Center
- **Operational Dashboard**: Real-time sales KPIs, gross margin metrics, present funds (cash drawer vs. bank split), inventory valuation at weighted average cost, and stock health indicators.
- **Multi-View Activity Feeds**: Zero-latency tabbed streams for today's sales, supplier deliveries, and critical stock depletion alerts ($\le 5$ units).
- **Tactile Quick Launchpad**: Instant shortcuts for checkout, stock receiving, drawer counts, product catalog, day finance, and audits.
- **Multi-Store Location Awareness**: Seamlessly filter financial summaries and reports by branch location or view aggregated consolidated metrics.

### 2. High-Speed Point of Sale (POS) Counter
- **Barcode & SKU Scanning**: Instant item lookup, quantity adjustments, and keyboard shortcuts.
- **Flexible Tenders**: Supports Cash, Card, Mobile Wallets (bKash/Nagad/Rocket), Bank Transfers, Split Payments, and Customer Due accounts.
- **Cart Holds & Drafts**: Save pending sales and switch between multiple customer queues without losing progress.
- **Thermal & Sheet Receipts**: Clean printable receipts optimized for 80mm thermal receipt printers and standard desktop printers.

### 3. Inventory & Multi-Branch Stock Control
- **Catalog Management**: Hierarchical categories, barcodes, cost pricing, selling pricing, and unit tracking.
- **Receiving & Supplier Invoices**: Log incoming shipments, track supplier payments vs. outstanding dues.
- **Stock Transfers & Locations**: Move stock between warehouses and retail floor locations.
- **Stock Taking & Audits**: Digital stock count audits with automatic reconciliation adjustments.

### 4. Cash Drawer & Register Operations
- **Session-Based Drawers**: Explicit opening balance, mid-day cash drops/transfers, and end-of-day physical count reconciliations.
- **Multiple Cash Locations**: Support for multiple registers and physical cash points per branch.

### 5. Daily & Monthly Financial Closing
- **Automated Workday Flow**: Controlled accounting workday cycle ensuring clean daily boundaries.
- **Comprehensive Breakdowns**: Daily sales, cost of goods sold (COGS), gross profit, category expenses, employee payroll, and supplier inflows/outflows.
- **Professional Excel Workbooks (`.xlsx`)**: Native multi-sheet ClosedXML spreadsheet exports with dynamic `=SUM()` formulas and multi-branch performance matrices.
- **Print & PDF Layouts**: Formatted for management audit archives.

### 6. Role-Based Security & Permissions
- Built on **ASP.NET Core Identity** with role enforcement:
  - **Admin**: Full access to users, system settings, financials, margins, and audits.
  - **InventoryManager**: Catalog creation, receiving, stock takes, and cost visibility.
  - **SalesAssociate**: High-speed counter checkout, customer ledger, and order history.

---

## Technology Stack

- **Framework**: .NET 11 (`net11.0`)
- **Frontend**: Blazor Web App (Static SSR + StreamRendering + Interactive WebAssembly Auto Mode)
- **Styling**: Tailwind CSS v4 with mobile-optimized touch layouts
- **Database**: PostgreSQL 16+ via Entity Framework Core & Npgsql
- **Spreadsheets**: ClosedXML for native `.xlsx` generation
- **Barcode Engine**: ZXing.Net barcode generation & EAN-8 roll printing
- **Containerization**: Multi-stage Linux Docker container (`mcr.microsoft.com/dotnet/aspnet:11.0-preview`)

---

## Getting Started

### Prerequisites
- [.NET 11 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL 16+](https://www.postgresql.org/download/) (or a cloud PostgreSQL instance such as Supabase / AWS RDS)
- [Docker](https://www.docker.com/) *(optional, for containerized deployments)*

### 1. Local Setup

Clone the repository:
```bash
git clone https://github.com/ziaulhasanhamim/store-management-pos.git
cd store-management-pos
```

Configure your PostgreSQL database connection string in `POSApp/appsettings.Development.json` or via environment variable:
```bash
export DB_URL="Host=localhost;Database=retailpos_dev;Username=postgres;Password=yourpassword"
export AdminUser__Password="YourStrongPassword123!"
```

Run database migrations:
```bash
dotnet ef database update --project POSApp
```

Run the application:
```bash
dotnet run --project POSApp
```
Visit `https://localhost:5001` or `http://localhost:5000` in your browser.

---

## Docker Deployment

A production-ready, multi-stage `Dockerfile` is included. It exposes port `8080` and includes reverse-proxy header support (`X-Forwarded-For`, `X-Forwarded-Proto`) for deployment behind Nginx, Traefik, Caddy, or Cloudflare.

### 1. Build the Docker Image
```bash
docker build -t retailpos:latest .
```

### 2. Run the Container
Point the container to your cloud or external PostgreSQL database:
```bash
docker run -d \
  --name retailpos \
  -p 8080:8080 \
  -e DB_URL="Host=your-db-host.com;Port=5432;Database=retailpos;Username=postgres;Password=SecretPassword;SSL Mode=Require;Trust Server Certificate=true" \
  -e AdminUser__Username="admin" \
  -e AdminUser__Password="InitialAdminPassword123!" \
  -e StoreName="City Mart" \
  -e StoreSubtitle="Superstore & Retail" \
  -e CurrencyCode="USD" \
  -e BusinessTimeZone="America/New_York" \
  -e AUTO_MIGRATE="true" \
  retailpos:latest
```

---

## Environment Variables & Configuration

All application behaviors can be customized without rebuilding:

| Environment Variable | Default | Description |
| :--- | :--- | :--- |
| `DB_URL` | *(None)* | PostgreSQL connection string (or `ConnectionStrings__DefaultConnection`). |
| `StoreName` | `RetailPOS` | Business name displayed on header, receipts, page titles, and exports. |
| `StoreSubtitle` | `Store Management` | Sub-branding / tag line displayed on login and navigation. |
| `CurrencyCode` | `BDT` | ISO currency code formatted across monetary values (e.g. `USD`, `EUR`, `GBP`, `BDT`). |
| `BusinessTimeZone` | `Asia/Dhaka` | IANA / Windows timezone for business day calculations and receipt timestamps. |
| `AUTO_MIGRATE` | `true` *(Docker)* | Automatically apply pending EF Core schema migrations on startup. |
| `AdminUser__Enabled` | `true` | Enables initial bootstrap provisioning of the administrative user. |
| `AdminUser__Username` | `admin` | Username for the bootstrap administrator account. |
| `AdminUser__Password` | *(Empty)* | Initial password for the bootstrap admin. |
| `ASPNETCORE_URLS` | `http://+:8080` | Internal listening port inside the container. |

---

## Deep Dive Documentation

Detailed architectural notes and implementation guides are organized in the [`docs/`](./docs) directory:

- [Security, Accounts & Authentication](docs/security-and-accounts.md)
- [Database Persistence, Entities & Migrations](docs/persistence-and-migrations.md)
- [Financial Architecture, Workday Flow & Closings](docs/finance-and-workday.md)
- [Inventory Control, Receiving & Stock Transfers](docs/inventory-and-receiving.md)

---

## Running Automated Tests

The solution includes a test suite with 73 unit and integration tests (using Testcontainers for PostgreSQL):

```bash
# Run unit tests
dotnet test tests/POSApp.Tests.Unit

# Run integration tests (requires Docker for Testcontainers)
dotnet test tests/POSApp.Tests.Integration
```

---

## License

This project is licensed under the MIT License.
