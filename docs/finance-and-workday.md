# Accounting, employees, warehouses and closing

## Pages and access

- `/stock/locations`: inventory managers/admins create selling locations or warehouses/godowns. Warehouses can receive and transfer stock but are rejected by sales, including returns. Existing locations retain selling permission until changed.
- `/stock/transfers`: move a product between locations, with name/barcode/ID search and transfer history. Stock, paired movements, and a transfer record commit atomically. Whole/fractional rules, available source stock, destination limits and retries are validated. Transfers do not create a purchase, change weighted average cost, or change supplier dues.
- `/cash-drawer`: each selling location has one physical cash account and one session per working day. Warehouses do not have sales drawers. Staff can share a location drawer; the author of each transaction/opening/count remains audited. Select the paying cash location here for expenses, payroll, supplier/customer payments, bank deposits/withdrawals and warehouse purchases. Cash sales/refunds always use the sale's own selling location, regardless of that selected context. A drawer's retained cash carries into its next session automatically. Count and close each active location separately.
- `/finance/bank`: administrators inspect one combined bank/card/mobile-wallet balance, enter an opening, signed corrections, and cash-to-bank or bank-to-cash transfers. Transfers update both ledgers atomically and do not count as profit. Bank balances can be negative to represent an overdraft or missing migration opening; physical drawer cash cannot be negative.
- `/employees`: maintain employees independently of login users. The list shows the working month's created salary, settled amount, remaining due, money paid, bonuses and carried advance. Salary creation always targets the shared working month's salary and immediately applies any outstanding employee advance. The default salary is a template; changing it does not rewrite existing records. Older months remain available for viewing and overdue installment payments.
- `/finance`: store staff record expenses and view their own entries. Administrators see all expenses and payroll. The default list shows the shared working day. Expenses and payroll always post to that day; a submitted calendar date cannot move them elsewhere. Salary installments settle the created monthly obligation. Salary overpayments become a stored advance for the next salary. Bonus is separate, nonrecoverable extra pay. Salary, bonus and advance payouts are expenses when paid. Applying a previous advance moves no money and adds no new expense.
- `/accounting-date`: an administrator opens the shared working day for everyone. No global opening-cash input is needed. Set initial cash already physically held once on each location drawer during initial setup. Close the current day before opening a later day; each location retains its own counted closing balance. Sales, purchases, supplier/customer payments, bank entries, transfers, expenses and payroll use this global date. Actual `CreatedAt` audit timestamps still record entry time. A fresh/unconfigured database defaults to today until the opening is saved.
- `/finance/day` (also `/finance/closing`): review expected physical cash, sales receipts/refunds, due collections, purchases/returns, supplier payments, payroll, advances, expenses, bank movements, dues and stock value. The small Auto `CashCount` component on each drawer shows the difference while typing; the surrounding pages and forms stay SSR. Count cash on each location's drawer; the business-day screen shows every location's status/count and cannot close while any drawer remains open. It totals those counts without requesting another business-wide cash count. The next-day link starts the next working day. Monthly closing derives its opening from the first working day and includes saved daily cash differences. Inactive days require no zero entry. The dashboard uses a small indexed cash aggregate rather than loading the full stock/profit report.
- `/accounts/suppliers` and `/accounts/customers`: administrator creation with signed opening dues/credit, account histories, opening corrections, and signed due adjustments. A supplier waiver from 35,000 to 30,000 is an adjustment of -5,000 with a reason. This is a noncash account change and does not automatically become a profit/loss item. Changing an opening changes current balance only by the difference, preserving subsequent invoices and payments. Customer quick-add also exposes opening balance to inventory/admin roles. Customer accounts can receive payments against opening or accumulated due; these update cash/bank and the stored customer balance without creating new sales revenue.
- `/reports/profit-loss`: administrator-only streamed SSR P&L, with day/week/month grouping, date filters and printing.

## Money and dates

Applied cash sale payments add to a drawer, cash refunds subtract, and cash supplier payments/expenses/payroll subtract. Card, wallet and bank-transfer receipts/payments post to the combined bank ledger instead. Cash change is excluded by posting only the applied amount. Credit transactions post invoices and stored party due but do not move funds until paid. All money entries retain source links, author, amount, business date and actual creation time. Stable request IDs and serializable transactions protect retries and concurrent postings.

A drawer's opening is physical cash already present, not income. During initial setup, enter it once per location. Later openings carry the previous actual count automatically; an arbitrary replacement opening is ignored. New locations created after trading begins start at zero and are funded through a recorded transfer or cash-in. If cash is banked before closing, record the bank deposit first; only the remaining cash carries overnight.

The location formula is expected cash = carried opening + cash sales/collections + cash-in/transfers received - refunds/supplier payments/expenses/payroll/payouts/transfers sent. Closing saves expected, counted and signed difference; a discrepancy requires notes. Closed counts cannot be rewritten by retrying or deleting their source transactions.

Location-to-location transfers have a unique `CashTransfer` header and two linked cash movements. Both locations must have an open session, the source must have enough cash, and the transaction commits atomically. Retries do not move money again. These internal transfers are excluded from consolidated cash-in/out totals and never create income or expense. Bank transfers retain their cash/bank legs and likewise have no profit effect.

`WorkingDay` is the shared date. `DayFinance` stores the combined opening, expected closing, sum of location counts, variance and close audit, linked to its `FinanceClosing` snapshot. Retained balances at locations that did not operate remain part of total cash. A business-day closing is a consolidation, not a second cash account or cash count. Monthly cash includes saved daily differences. Cash shortfalls are recorded as reconciliation differences rather than silently turned into operating expenses.


Closing a day/month saves its snapshot and blocks further postings, voids or date corrections in that period. Every open location session must be counted and closed first. Each local difference requires notes; the business close totals the final balances. The global date stays on the closed day until an administrator opens a later day. Business opening cash is derived automatically from location balances; use explicit cash movements for additions/payouts.

Revenue includes completed nondeleted sales net of returns/discounts, including amounts due. COGS uses the saved sale-line cost rounded per line. Payroll expense follows the requested payout basis: salary, bonus and advances count in the working month when money leaves cash/bank. Creating the salary obligation or applying an earlier advance is not another expense. Net profit = revenue - COGS - operating expenses - paid payroll. This is the application's operational reporting convention; salary obligations and remaining staff dues are shown separately. Supplier settlements, bank transfers and opening funds are not operating expenses.

Historical dues use account entries as of period end; stock value replays surviving inventory movements. Later payments never rewrite an already closed summary. Salary installments against an older month's unpaid obligation can be paid in a later open working day, and their expense belongs to that payment period.


## Automatic payroll and monthly breakdowns

Payroll entry offers only salary payment or bonus. The amount is the money being paid now. For a salary of 2,000, paying 3,000 settles 2,000 and stores a 1,000 employee advance automatically. The request has no manual `AdvanceApplied` field. Any available advance is consumed automatically before calculating remaining salary; a bonus neither settles salary nor creates recoverable credit. The legacy Advance enum remains readable for old ledger rows, but new Advance-type requests are rejected.

`/employees` and the small Interactive Auto `PayrollFields` island show the created monthly salary, prior-month carried advance, cash paid, bonus and signed remaining salary. A negative remainder is displayed as “Advance” with a positive amount. The form previews the balance after the entered payment. Paid totals use the calendar month when money was paid, even when settling an older salary. All writes still go through the antiforgery-protected SSR form and recalculate against the database. A shared database aggregate supplies both screens, and historical month balances stop at that month's end so later advance application does not rewrite them.

`/finance/closing?period=month` and the monthly report link show every calendar day, including zero-activity days, with net sales, saved COGS, sales profit, operational expenses, payroll, total spend and net result. Categories include separate rent and snacks/refreshment totals. Employee payouts split salary cash, bonus and newly created advances, counted by payment date rather than the salary month being settled. Prior advance application has no cash payment and is not counted again.

Net assets follow the requested business formula: stock + physical cash + bank − supplier due + customer due. The opening comparison uses the month's opening funds and stock/party ledgers before its boundary; closing cash uses the last counted or expected balance of each drawer as of period end. Stock replays movements with signed weighted cost, including supplier returns. The report shows opening and closing components and their change. Asset growth may differ from profit because opening capital, cash discrepancies and stock/due adjustments also change assets.

On month close, `FinanceClosing.MonthlyDetailsJson` stores the full breakdown and asset positions as a portable text snapshot. Later payments, employee renames and balances do not rewrite it. Older closings without a detail snapshot derive their expanded details from retained history. The existing numeric summary remains preserved.

## Export

The closing screen exports UTF-8 CSV with a BOM for Excel, containing the summary, daily cash/bank rows and the full monthly daily/category/employee/asset breakdown. It is a real CSV, not an XLSX file with a misleading extension. Text is quoted and protected from spreadsheet formula interpretation. A provisional export is labeled provisional; a closed export uses the saved snapshot. Print/PDF uses the browser's print dialog. Servicing is intentionally excluded. Export GET routes are in `Endpoints/FinanceEndpoints.cs` and require administrator authorization.

## Upgrade and verification

Apply migrations through `StoreMonthlyFinanceBreakdowns` using `dotnet ef database update --project POSApp`. No real database is updated automatically. The migration preserves cash history, converts existing staff/payroll records into independent employees and settled monthly salaries, and imports recorded noncash payments, supplier payments, expenses and payroll into the bank ledger. Historic business dates use the application's established Asia/Dhaka timezone. Only legacy staff with account history become employees; creating an employee never grants login access.

The global-day migration replaces per-user selections with their latest selected date, preserves old daily closing snapshots in `DayFinance`, initializes historical advances, and aligns payroll expense snapshots with recorded payroll payouts. Legacy closings have no invented physical count: `CountedCash` stays null and carry-forward uses their expected closing. Its downgrade is intentionally blocked to preserve reconciliation history.

The location-drawer migration preserves old cashier attribution in audit fields, derives retained cash from existing sessions, and only assigns a location automatically when old metadata identifies it unambiguously. Other legacy drawers remain visible for administrator assignment; it never guesses which shop held the money. Count a legacy drawer before consolidating its cash into an existing open location; the consolidation uses paired transfer entries and preserves old sessions. Unassigned nonzero balances block business-day closing. Draft day openings are recalculated from retained drawer cash and existing movements to avoid counting the old independent global opening twice. Closed business snapshots are preserved. Automatic downgrade is blocked to protect location/transfer history.

Imported bank movements start with zero initial bank funds. Enter the opening balance from before those recorded movements, or use a signed adjustment to reconcile to a known current balance. An opening adds to existing recorded flows; it does not overwrite them. New employees have no relationship to users, so this migration's automatic down path is intentionally unsupported rather than creating login accounts or discarding financial history. Rollback requires a pre-upgrade backup.

Run `dotnet run --project POSApp.Persistence.Tests`. Isolated SQLite checks cover stock transfer conservation/fractions/retries, selling restrictions, independent drawers, bank posting/transfers, employees without users, monthly salary installments/bonuses, opening corrections/waivers, selected business dates, exact day arithmetic, shared dates across users, required cash counts, shortage carry-forward, month-end variances, salary overpayments and advance application without double expense, frozen month snapshots and full daily/category/employee/asset breakdowns, automatic 2,000-salary/3,000-payment advance calculations, historical employee balances, historical dues, permissions and rollback. HTTP checks submit real enhanced forms for each feature and download the CSV. Existing scanner checks run with `node --test POSApp.Client.Tests/barcode-scanner.test.mjs`. Model/migration validation and PostgreSQL script generation do not apply migrations to a real business database.

## Fast entry and separated finance pages

`/finance` is the finance hub. `/expenses`, `/suppliers/payments` and `/customers/payments` each stream a small Interactive Auto batch editor from the client project. Add multiple rows, reuse the last category/party/method, and save once without a document reload. Working-day amount and entry-count cards include all entries in the displayed day; payment lists show the latest 200. Expense lists combine every entry for the selected day into one total per category, with expandable individual payments for editing. Cash/bank methods, notes, receipts and correction history remain separate underneath each category; categories on different days never merge. Notes and expense receipt references are optional. `/payroll` retains the enhanced SSR salary-payment form on its own page. `/finance/day` and `/finance/month` have separate date/month selectors and group trading results, receipts/payments, cash/bank movement and asset balances. Legacy closing URLs still work.

The administrator can add, rename and archive expense categories from `/expenses/categories` or add a category inside expense entry. Archived categories remain attached to previous expenses. Built-in classifications are retained for compatibility, while monthly category breakdowns and CSV exports use the actual managed category name. `/customers` provides customer creation, opening due/credit, balances, statements and collection links; administrator opening-balance adjustments remain available under `/accounts/customers`.

`MoneyEntryService` saves each batch in a serializable transaction. A cached request/result and payload hash make retries safe, including edits and lost responses. A changed payload with an already-committed batch ID is rejected rather than silently discarding changes. A stale entry revision is also rejected. Staff may amend their own entries; administrators may amend all entries in the current open working day. Supplier receipt payments may be corrected while the day/drawer are open, but their supplier stays fixed. Receipt payments whose supplier was reassigned through an administrator correction remain locked here; sale checkout payments remain part of the sale workflow. Closed accounting days/months and closed cash drawer postings cannot be amended.

Corrections append reversal and replacement cash/bank postings. Supplier/customer corrections append signed account adjustments, preserving original ledger amounts and before/after audit records. Changing party restores the old account before applying the corrected payment to the new account. Money collected beyond customer due becomes customer credit; supplier overpayments become advances. Posting foreign keys allow multiple historical postings per entry; unique request IDs and unique reversal links continue to guard retries and reversals. Original supplier/customer ledger payment links retain their uniqueness. No payroll, sales, purchase, or closed-period totals are rewritten by entry correction.

`BatchMoneyEntriesAndExpenseCategories` backfills existing expenses to the corresponding managed built-in category without changing amounts or dates. Apply it with `dotnet ef database update --project POSApp`; startup does not run migrations automatically. The upgrade keeps payroll/request/reversal uniqueness while replacing payment/expense posting uniqueness with normal foreign-key indexes so historical amendments can coexist.

## Loans and lender debt

Administrators manage lenders at `/finance/debt` and borrowing/repayments at `/finance/debt/payments`. Each lender has one persisted `LenderBalance` with opening/current debt and dated `DebtEntry` history. Existing debt is entered when adding a lender on the current working day; this opening entry changes the liability without creating cash or bank funds. New borrowing increases debt and funds; principal repayments reduce both. Card, mobile wallet and transfers use the combined bank account, while cash requires an explicitly selected selling location's open drawer. No interest is calculated. Repayment cannot exceed outstanding principal.

Entries use the shared working date, reject stale form dates and closed periods, and atomically save debt with its linked cash/bank movement. Request IDs prevent duplicate submissions, including retries after day closing; reusing an ID with changed values is rejected. The enhanced SSR forms use antiforgery protection and submit feedback. Histories can be filtered by lender and show the latest 200 entries; stored balances include every entry.

Day/month finance and CSV exports show borrowing, repayments and outstanding debt separately from revenue, operating expenses and profit. Monthly assets subtract lender debt using historical dates, and closing snapshots retain the saved debt totals. Apply `AddLenderDebt` with `dotnet ef database update --project POSApp` before using the new pages.

## Operating flow and reviewed closings

The sidebar has six primary work areas. `/inventory` groups stock operations; `/finance` shows the shared operating day; `/finance/payments`, `/finance/people` and `/finance/accounts` keep entry, payroll and setup tasks separate. `/administration` owns user/role setup and audit access. The working-day board guides open day → open location drawers → trade/payments → count and close drawers → review and close day. Month closing is a separate administrator task after that month's working days have been reconciled. Unused warehouse locations never require a cash drawer.

Drawer and day/month closing forms first render a review. Only the confirmed second submission closes the record. A reviewed expected-cash value or finance fingerprint prevents finalizing a stale review after money has moved. Cash counts survive validation errors. Each close records an audit event.

Administrators can use the Reopen links on saved drawer/day/month views. A reason and explicit confirmation are required. Reopen a closed month before its day, then reopen the drawer if cash corrections are necessary. Reopening the latest drawer restores its pre-count expected cash, resets the final count, and preserves the previous count and shortage/overage in `/administration/audit`. Count and close again after corrections. Requests are idempotent, scoped to the reviewed record and protected by a unique audit request ID. A later working day's opening cannot be silently rewritten: older day/drawer reopenings are rejected. Later closed months must be reopened before earlier ones. Use current-period corrections for historical operational changes.

`AuditedClosingReopening` adds only a nullable, uniquely indexed request ID to the existing audit table. Existing business amounts and closing snapshots are not changed by this migration.

## Report controls and saved-cost profit

Sales reports expose saved checkout costs and gross profit only to admins/inventory managers; sales associates retain their own-sale scope and receive no cost/profit values in the report DTO. Expand a sale for quantity, selling price, weighted discount, net sales, saved cost and profit. Header totals, table footers and the item-performance report reconcile to the saved sale total, including monetary rounding. Costs are historical snapshots, never today's catalog cost.

`/reports/items` aggregates sales and returns by product/unit. Signed quantities, revenue and cost produce net sold quantity and gross profit. Net-average unit values divide signed totals by net quantity; a zero quantity keeps any remaining monetary result and displays an undefined average. Transaction, receiving, inventory and item reports support explicit search and whitelisted sorting; paged reports distinguish current-page totals from all matching results. Date presets and previous/next-day links retain search, sort and location. Purchase/transfer search matches document ID, supplier or location; all-location transfer pairs remain net zero.

HTML GET and POST forms explicitly opt into `data-enhance`; SSR EditForms use `Enhance` and explicit POST methods. Native Blazor enhancement handles filters and routine form updates without replacing the browser document. The existing submit feedback remains opt-in through `data-submit-indication`. Authentication and download flows retain their specific redirect/download behavior.

Cash drawer pages separate operation/counting, location selection, admin cash movements and reconciliation history with a `view` query. Selecting a location returns to its live drawer rather than retaining the previously selected session. Historical sessions remain linked by ID. Section navigation respects role policies and highlights the correct work area even for legacy routes such as `/expenses`, `/payroll` and `/suppliers/payments`. Inactive months carry the previous counted cash into their opening calculation.

## Concurrent posting and recovery

Financial mutations replay complete serializable operations through `StoreOperation`,
including a new DI scope and DbContext for each attempt. Only PostgreSQL serialization
failures, deadlocks, and specifically identified creation/request uniqueness races are
eligible. Four attempts with short randomized backoff bound retry latency. Validation,
permission failures, arbitrary database failures and unknown commit outcomes are not
blindly retried. Mutation request IDs remain unchanged. Stored request fingerprints
reject attempts to reuse a completed submission with different values. Sale drafts have
an explicit revision; a stale tab cannot replace a newer draft.

`FinancialPosting` remains the shared cash/bank posting path. Close-drawer validation
adds one indexed movement sum for that session. `/finance/balance-checks` performs the
broader on-demand history comparison; normal submissions and closings do not scan all
inventory or party history. It reports differences and never repairs balances silently.
Imported baselines without ledger history need deliberate review.

Plain SSR submissions carry a working-day stamp, validated when the operation reads
its working day. Interactive sale, receiving and transfer commands carry the displayed
working date explicitly; payment batches and debt entries already do so. A changed day
requires refreshing/reviewing while drafts retain the entered values. No polling or new
SignalR connection is introduced.

## Drafts

`EditorDrafts` stores one current draft per user and feature (stock takes include their
record ID), with revision-based conflict detection, a unique save ID and a bounded JSON
payload. Clearing retains a revisioned tombstone. The draft store never posts stock,
dues, cash or expenses. Feature permissions are rechecked on read and save.

Interactive editors save local browser snapshots immediately after input renders and
sync to the server after 800 ms of inactivity. Browser copies are isolated by user,
feature and browser tab. An unacknowledged local snapshot is retained even when its
server write has a later timestamp. Conflicting tabs require an explicit choice, with
backup copies before replacing recovered data. SSR payroll, bank and debt forms use an
opt-in DOM adapter; passwords, antiforgery tokens and file contents are excluded.

Valid stock-take counts continue to autosave through the stock-take service. Pending
batch rows and incomplete edits additionally use the shared draft recovery store.
Stock adjustments retain their existing browser recovery and gain server recovery.

## Salary balances

A month's `MonthlySalary.Amount` includes its base salary and `BroughtForward` unpaid
balance. Earlier salary records retain `CarriedForward` and `CarriedToMonth`, and their
actual settlements are not rewritten as fictitious payments. Transferred balances
cannot be paid again against an earlier salary. Previous advances apply to the combined
amount; salary overpayment creates a new advance. Creating/carrying salary does not
create cash movement or expense. Salary, bonus and advances count toward management
profit expense when cash/bank payment actually occurs, as configured for this business.

Supplier offers and history corrections remain signed balance adjustments with a
required reason: negative reduces due, positive increases due. These adjustments do not
invent a cash payment or automatically revalue historical stock/profit. Borrowing,
principal repayment and owner funding retain their separate non-operating classifications.

## Reports and audit

Trading and inventory reports read rows, totals and details from one consistent
transaction snapshot. Filtered CSV exports for sales, receiving/transfers and item
performance preserve search, sort and location scope, include full-filter summaries,
protect text cells from spreadsheet formulas and refuse exports over 10,000 rows with
an instruction to narrow the range. Normal on-screen pages remain paginated.

Financial corrections retain before/after snapshots, including merged expense updates.
Explicit batch correction audit records are reused instead of duplicating events.
Voids retain original records and reversal history; the admin UI previews transaction
value, payments and signed stock effects and requires an explicit confirmation.

Focused verification includes the existing isolated service/SSR suite and
`node POSApp.Persistence.Tests/form-drafts.check.mjs` for browser-draft semantics:
missing drafts, immediate local backup, exclusion of antiforgery tokens, enhanced
form node reuse, acknowledged submission reset and stale-tab preservation.
`RefinementChecks` injects PostgreSQL conflict errors at commit against an isolated
SQLite fixture to verify complete rollback/replay and frozen input values. It is
not a PostgreSQL throughput/load test.

## Background JSON imports

Supplier migration is available at `/suppliers/import` (also linked from Suppliers).
Use a JSON array with required string `name`, optional numeric `id`, optional
string/null `phone`, and optional numeric `balance` (defaults to zero). Missing or
null IDs receive independently generated application IDs without a migration reference.
Positive balance means outstanding due; negative means credit. Supplied source IDs
are stored in `Supplier.LegacyId` and must be positive and unique. Re-importing a
source ID is rejected, including soft-deleted suppliers. Existing accounts are never silently overwritten or merged by name.
Opening balances and ledger entries use the working day captured when uploading.
No supplier payments or cash/bank movements are created.

Stock adjustment JSON uploads use the same background pipeline. Each row contains
`productId`, `quantity`, and `unitCost`; the location, date, and notes come from the
editor. Imported rows are never staged in the manual editor. Manual entries are
preserved. Existing product/fractional/date/closed-period/cost validation still applies.
Both types accept up to 10 MB and 10,000 distinct rows per file. Validation and
posting happen in the worker, with one atomic transaction per file. Invalid files
are marked failed without partially posting their rows.

`ImportService` persists a durable `ImportJob` before returning HTTP 202. A unique
nullable active key allows only one queued/processing job per import type, across
users and tabs. Request IDs guard uncertain upload retries. `IImportQueue` is a
bounded channel notification abstraction; payloads and status live in the database,
so replacing its notification mechanism later does not change import handlers.
`ImportWorker` uses scoped services and a fresh principal, rechecks database roles,
claims jobs with an atomic conditional update, and renews its two-minute lease.
The periodic scan recovers missed notifications and abandoned leases after restarts.
Infrastructure failures retain the payload/lease for automatic recovery; invalid
files or revoked permissions are marked failed. A lost commit acknowledgement is
retried with its original identity rather than inviting a duplicate migration.
Supplier import audit receipts commit with the inserted accounts; stock imports use
their existing deterministic posting request ID. A crash after posting but before
acknowledgement therefore does not duplicate accounts or stock.

Upload controls poll only while an import is active (every three seconds). The
header indicator polls every five seconds while active and stops when idle. It links
to `/imports`. There is no new notification hub. Status survives navigation and
refreshes. Completed/failed jobs release the upload lock, retain the result/audit
history, and discard the raw upload payload. Corrected/failed files can be uploaded
again; reposting the same stock file/location/date/notes acknowledges its earlier
adjustment rather than adding stock twice.

`ImportChecks` exercises 1,200 suppliers, signed opening balances/history, rejection
of duplicate source IDs, active upload exclusion, invalid-row rollback, lease
recovery after committed posting, repeated stock uploads, and revoked permissions.
The HTTP checks cover antiforgery, accepted responses, status reads and the active
indicator. These checks use the isolated SQLite fixture, not a PostgreSQL load test.


## Starting a fresh form

Sales, receiving, stock adjustments, transfers and money-entry batches share the
revisioned draft reset action. Use New sale / New receipt / New adjustment /
New transfer / New batch. A populated draft prompts for confirmation. Reset waits
for any in-flight recovery save, cancels debounce saves, writes a server tombstone,
updates this tab's browser copy, then clears the visible form and creates new
submission IDs. The current entries are retained if the reset cannot finish, so
users can retry. A confirmed reset can also resolve a recovery conflict using the
current server revision; a concurrent change still causes a protected write failure.
Posted records and explicitly held sales are retained. Existing SSR payroll, bank
and debt forms keep their New entry action. Stock takes use Discard unsaved changes
to return to saved counts without erasing counts already saved on that stock take.

## Directory and history pagination

`QueryPagination.ToPageAsync` counts the filtered query, clamps the requested page,
and applies database Skip/Take before materialization. New lists default to 30 rows;
30, 50 and 100 are accepted page-size query values. Directory sorts use the primary
key as a final tie-breaker. `PageNavigation` shows record range, total count and
first/previous/next/last links, preserving the current query parameters. Empty lists
have page 1 with zero records. Search forms reset the page while retaining page size.

Suppliers, customers, users, employees, lenders, expense categories and stock
locations now page their directory queries. Supplier/customer account ledgers,
product stock-location history, stock takes, stock adjustments, transfers, audit,
payroll, debt payments, and drawer sessions/movements no longer stop at an
inaccessible fixed history cap. Administrative account tables have independent
account/history page parameters. Items, reports, stock-take counts and cash/bank
statements retain their existing pagination.

Payment/expense and stock-adjustment history pagination runs inside the interactive
editor and leaves the current entry draft intact. Loading indicators replace table
body rows while retaining the headers. Expenses page whole category groups rather
than splitting a category across pages. Customer due/credit, lender outstanding debt,
payment/expense day totals, salary period totals and drawer reconciliation summaries
remain based on the entire matching dataset. Employee payroll calculations only load
the employees and related payroll history needed for the displayed page.

`PaginationChecks` seeds more than two pages of directories and more than four pages
of ledger movements, verifies deterministic tied-name ordering, filtered totals,
empty/out-of-range pages, accessible last pages, intact expense groups and unchanged
full-dataset financial summaries. The existing inventory-history check now verifies
access beyond the former 100-row cap.
