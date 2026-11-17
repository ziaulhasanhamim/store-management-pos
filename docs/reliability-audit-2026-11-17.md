# Reliability audit — 17 November 2026

## Fixed in this release

- Authorization resolves current database role membership for both service calls and HTTP policy checks. Stale administrator cookie claims no longer retain access after a role is removed.
- Checkout recognizes a completed submission before recalculating pricing. Retrying a payment that settles previous customer dues returns the original totals without posting twice. Customer balance reads now occur inside the serializable transaction.
- Sales and receiving submission fingerprints include transaction timestamps; sales also include the linked original sale. Previously deployed fingerprints remain compatible when their stored transaction identity matches.
- Backdated sales, receiving, and financial entry rows validate the effective transaction date against the trading calendar.
- PostgreSQL URI parsing preserves passwords containing colons and decodes escaped database names.
- Normal startup no longer rewrites historical movement timestamps. The existing repair is available only through the explicit `Maintenance__RepairMovementTimestamps=true` maintenance setting. Verify a recoverable backup and review affected records before enabling it; disable it afterward.
- CI now builds CSS, runs unit and PostgreSQL integration tests, and builds the production container. Tag-triggered releases require a successful full CI run on main for the exact tagged commit. SDK selection is pinned to .NET 11.0.100 with patch roll-forward; Docker uses the same SDK. Frontend installs require the lockfile.

- Database options resolve the final host configuration rather than requiring developer secrets before test-host configuration is installed. Integration hosts explicitly use the Testing environment.

## Reviewed workflows and retained behavior

Checkout, split payments, refunds, previous customer dues, receiving, supplier dues, transfers, adjustments, cash counts, and closings were traced and exercised through the automated suites. Serializable transactions and bounded whole-operation retries protect overlapping stock and financial writes. The new concurrency regressions verify identical submissions post once and distinct sales preserve both stock changes and payments.

Negative sale stock is an intentional workflow documented in `inventory-and-receiving.md`: sales may precede stock receiving. This release preserves that policy and tests the resulting balances. Transfers and supplier returns retain their existing availability checks.

Import processing uses durable database jobs, leases, renewal, and current-user authorization. No unverified bulk historical repair or schema change is included in this release.

## Follow-up opportunities

These are product improvements, not features added by this patch:

1. Add a configurable warning/approval policy when checkout takes stock below zero, retaining support for stores that record receiving later.
2. Provide an administrator-operated backup/restore drill with backup-age monitoring and a documented recovery target.
3. Add browser automation for barcode keyboard input, split-payment editing, receipt printing, and reconnection behavior; service/HTTP tests cannot verify hardware printers or browser interaction.
4. Add a versioned reconciliation preview for historical corrections, showing affected counts, closures, and ledger entries before an administrator applies changes.

## Deployment

No new migrations or historical financial data repair is required. Existing public routes and DTO shapes are preserved. Roll back application code to the previous tag if necessary; do not restore the removed bootstrap password or disable CI gating.

## Regression evidence

| Requirement | Evidence |
| --- | --- |
| Duplicate checkout and concurrent stock/payment integrity | `Checkout_ConcurrentIdenticalRequests_PostStockAndCashOnce`; `Checkout_ConcurrentDistinctSales_PreservesDocumentedNegativeStockAndCash` |
| Previous-dues checkout retry | `Checkout_RetryAfterPreviousDuePayment_ReturnsOriginalResultWithoutDoublePosting` |
| Split payments and refunds | `Checkout_SplitPaymentAndRefund_RestoreStockCashAndCustomerLedger` |
| Closed effective transaction dates | `BackdatedSale_ClosedEffectiveDay_IsRejectedWithoutPosting`; `BackdatedReceiving_ClosedEffectiveDay_IsRejectedWithoutPosting`; `BackdatedExpense_ClosedEffectiveDay_IsRejectedWithoutPosting` |
| Timestamp submission identity | `Checkout_ChangedTimestampOnCompletedRequest_IsRejected` |
| Current role enforcement | `Authorization_RevokedAdminRole_DeniesStaleClaimsInServiceAndHandler`; `Authorization_DemotedAdminCannotReadAnotherCashiersBill` |
| PostgreSQL URI credentials/database parsing | `NormalizePostgresConnectionString_PreservesColonPasswordAndEscapedDatabase` |
| Historical records left untouched at startup | `Startup_DefaultDoesNotRepairHistoricalMovementTimestamps` |

Final local suites: 130 unit and 105 integration tests passed, zero failures/skips. Both solution builds passed with zero warnings/errors. Frontend lockfile installs and CSS builds passed. Production image/smoke and remote CI status are recorded by the release checks.

Production Docker image built successfully. A fresh disposable PostgreSQL database was migrated and bootstrapped; real cookie login and 13 HTTP smoke checks passed. No production transactions were created.
