# POS persistence

Entities contain properties and navigation collections only. Balance updates, payment processing, validation, and history creation belong in future application services.

EF Core discovers relationships from navigation properties and matching foreign-key names. Explicit configuration covers indexes, precision, and restricted deletion. Collections use C# collection expressions.

## Balance models

The app uses one currency, configured by `CurrencyCode` in `appsettings.json` (currently `BDT`). Balances, sales, and purchases all use this app currency; there are no currency columns on individual records.

`AccountBalance` is an abstract base containing shared fields: `OpeningBalance`, `OpenedAt`, and `CurrentBalance`. It shares C# properties and is not registered as a separate database entity or table.

`CustomerBalance` adds the required customer relationship, customer ledger entries, and collected payments. `SupplierBalance` adds the required supplier relationship, supplier ledger entries, and supplier payments. Each customer/supplier has a single `Balance` navigation. EF conventions infer one-to-one relationships using `CustomerId`/`SupplierId` and create unique foreign-key indexes, preventing multiple balances for a party. Creating the balance alongside a new party remains an application-service responsibility.

Positive balances represent outstanding dues. Negative balances represent advances/credits. Opening balances support importing existing dues when a business switches to this application. Current balances are stored values; the models do not calculate or update them automatically.

`CustomerBalanceEntry` and `SupplierBalanceEntry` store history with entry type, signed amount, resulting balance, occurrence time, notes, audit fields, and optional invoice/payment references. Opening entries need no historical invoice. Unique invoice/payment indexes prevent duplicate source references.

Payments can link to an account without an invoice, allowing settlement of opening or accumulated dues. Optional sale/purchase references can identify a specific invoice. Walk-in payments may link only to a sale.

Use the database-generated entry `Id` for posting order, or `OccurredAt` for transaction dates. Money has EF precision `(18,2)` and quantities `(18,3)`.

## Future application services

Application services will initialize opening balances, increase dues for credit transactions, decrease dues for payments, and save the resulting balance and history together. For example, an opening due of 1,000 plus a purchase of 500 minus a payment of 200 gives a current balance of 1,300.

The models contain no workflow enforcement or automatic posting. Services must handle validation, UTC timestamps, concurrent updates, duplicate requests, audit fields, and reversals. The current models have no concurrency token.

## Verification and database setup

Check EF field mappings, indexes, relationships, and schema generation without connecting to a database:

```sh
dotnet run --project POSApp.Persistence.Tests
```

There are currently no migration files. Configure `DB_URL` or `ConnectionStrings:DefaultConnection`, then generate and review a migration appropriate to the existing database before applying it:

```sh
dotnet ef migrations add InitialPosSchema --project POSApp
dotnet ef database update --project POSApp
```

An initial migration includes Identity tables. No database changes are applied automatically at startup.
