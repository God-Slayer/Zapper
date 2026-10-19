# Postman Request-by-Request Walkthrough

Run **00 – Readiness and reset** first. Then run folders 01–08 in order, or run any scenario folder by itself after the services are running. In these notes, a **payment event** is a row in `PaymentLifecycleEvents`; a **notification row** is a row in `InboundNotifications`.

## Database tables to watch

- **`Payments`** stores one row per Zapper payment and its latest status and Acme response code.
- **`PaymentLifecycleEvents`** stores the payment history: submission, provider response, reconciliation, refund, and reversal events.
- **`InboundNotifications`** stores callbacks received from the stub, including duplicate and stale deliveries.
- **`Customers`**, **`StoredCards`**, and **`SubMerchants`** are seeded lookup data used by the demo. The demo requests do not change them.

“Capture … transaction ID” requests read the Acme stub’s in-memory transaction list and do not write to PostgreSQL. The stub’s scenario and throttle controls are also in-memory only.

`Payments.Status` is stored as an integer: `0` Pending, `1` Processing, `2` OutcomeUnknown, `3` Approved, `4` Declined, `5` SubmissionFailed, `6` Reversed, `7` PartiallyRefunded, `8` Refunded.

## 01 – Approved payment and idempotency

| # | Postman request | What happens |
|---|---|---|
| 1 | **Ensure default outcome is Approved** | Sets the stub’s default response. No database change. |
| 2 | **Submit approved payment** | Each send generates a fresh app payment ID and amount. Zapper creates one `Payments` row and a **Submitted** event, then calls the stub. The stub approves it; Zapper updates that same payment row to **Approved** with Acme’s details and adds an **Approved** event. Postman expects HTTP 201. Sending this request manually again generates another payment, which is why the observed run has three new rows after it was sent three times. |
| 3 | **Capture approved transaction ID** | Reads the stub’s transaction list and saves the transaction ID for later. No database change. |
| 4 | **Get approved payment** | Reads the saved payment from Zapper. It is already Approved, so no reconciliation event is needed. |
| 5 | **Replay exact same payment request** | Sends the same ID and details again. Zapper returns the existing result with HTTP 200; no new payment or lifecycle event is added. |
| 6 | **Reuse appPaymentId with changed amount** | Reuses the ID with a changed amount. Zapper returns HTTP 409 `IDEMPOTENCY_CONFLICT`; no database change. |

## 02 – Decline and pending PEND

| # | Postman request | What happens |
|---|---|---|
| 1 | **Set scenario InsufficientFunds** | Configures the stub for the next payment. No database change. |
| 2 | **Submit declined payment** | Zapper creates one payment and a **Submitted** event, then calls the stub. The stub returns insufficient funds (`51`); Zapper updates that same payment to **Declined** and adds a **Declined** event. |
| 3 | **Set scenario EftPending** | Configures the stub for the next payment. No database change. |
| 4 | **Submit pending PEND payment** | Creates a *second, separate* payment and a **Submitted** event. The stub returns `Processing` / `PEND`; Zapper updates that payment to **Processing** and adds a **Processing** event. |
| 5 | **Capture pending transaction ID** | Reads the stub’s transaction list and saves this payment’s provider transaction ID. No database change. |
| 6 | **Get pending payment (manual reconciliation check)** | Zapper queries the stub again. It is still pending, so the payment remains **Processing**. Because the same PEND result was already recorded when the payment was submitted, the repeated reconciliation does not add a duplicate lifecycle event. |
| 7 | **Change pending provider transaction to Approved** | Changes the outcome of the existing stub transaction. It does not submit another payment and does not update PostgreSQL. |
| 8 | **Stub sends approved Payment Advice** | The stub sends the callback to Zapper. Zapper adds an `InboundNotifications` row, updates the existing payment to **Approved**, and adds an **Approved** event. |
| 9 | **Get payment after approval advice** | Reads the updated payment and confirms Approved / `00`. No new event is expected. |
| 10 | **Reset scenario to Approved** | Resets the stub’s default outcome. No database change. |

## 03 – Payment Advice, refund, and reversal callbacks

This folder creates two approved payments, so it can run without first running folder 01. The payment and reversal amounts are generated per run. The partial refund amount is also generated per run (300–5,000 minor units, or R3.00–R50.00). In the run already reviewed, the payment was R412.69, the refund was R3.00, and the separate payment later reversed was R126.41.

| # | Postman request | What happens |
|---|---|---|
| 1 | **Ensure default outcome is Approved** | Sets the stub’s default. No database change. |
| 2 | **Create approved payment for advice and refund** | Adds a payment and **Submitted** event; the stub approves it, so Zapper updates that row and adds an **Approved** event. |
| 3 | **Capture advice/refund transaction ID** | Reads the stub’s in-memory transaction list. No database change. |
| 4 | **Send duplicate Payment Advice** | The stub delivers the same Payment Advice twice. In the reviewed run, the first delivery was recorded as `Processed` but stale because the payment already had a newer approved outcome; the second was recorded as `IgnoredAsDuplicate`. Neither changed the payment or added another lifecycle event. |
| 5 | **Send stale Payment Advice** | Zapper records the callback in `InboundNotifications`, recognizes its older timestamp, and retains the existing payment state. |
| 6 | **Send signed partial refund notification** | The stub signs and delivers a Transaction Status callback. Zapper records it in `InboundNotifications`, updates the payment to **PartiallyRefunded**, and adds a **RefundApplied** event. |
| 7 | **Get payment after refund callback** | Reads the current payment state. No database change. |
| 8 | **Submit separate approved payment for reversal** | Adds another payment, with **Submitted** and **Approved** events. It is separate because the first payment has already been refunded. |
| 9 | **Capture reversal transaction ID** | Reads the stub’s in-memory transaction list. No database change. |
| 10 | **Send signed reversal notification** | Zapper records the callback in `InboundNotifications`, updates the second payment to **Reversed**, and adds a **Reversed** event. |
| 11 | **Get payment after reversal callback** | Reads the updated payment. No database change. |

## 04 – Timeout and reconciliation

Reviewed run: the payment was R728.52. Its lifecycle was Submitted → Processing / `PEND` → Approved / `00`. The timeout path found the existing transaction pending; after it was changed to Approved, GET reconciliation updated the same payment row.

| # | Postman request | What happens |
|---|---|---|
| 1 | **Set scenario Timeout** | Configures the stub. No database change. |
| 2 | **Submit timeout payment** | Zapper creates the payment and **Submitted** event. The stub records the transaction but drops the submission response. Zapper’s timeout handling queries Acme, finds the transaction as `PEND`, updates the payment to **Processing**, and records the reconciliation event. In this demo the resulting status is Processing, not OutcomeUnknown. |
| 3 | **Capture timeout transaction ID** | Reads the stub’s in-memory transaction list. No database change. |
| 4 | **Set existing transaction to Approved** | Changes the existing stub transaction’s outcome. No PostgreSQL change. |
| 5 | **GET reconciliation returns Approved** | Zapper queries Acme again, updates the same payment row to **Approved**, and adds an **Approved** reconciliation event. |
| 6 | **Reset scenario to Approved** | Resets the stub’s default. No database change. |

## 05 – E29 throttling and manual retry

Reviewed run: the payment was R536.21. Acme throttled the automatic attempts with `E29`; the manual retry then approved that same payment with `00`. The database has one payment row and three lifecycle events.

| # | Postman request | What happens |
|---|---|---|
| 1 | **Ensure default outcome is Approved** | Sets the stub’s default. No database change. |
| 2 | **Queue two throttled requests** | Sets the stub’s in-memory throttle counter to two. |
| 3 | **Submit payment (expect E29 after automatic retry)** | Zapper creates one payment and a **Submitted** event. Its Acme client automatically retries once; both attempts are throttled. Zapper updates the payment to **SubmissionFailed** with `E29` and adds a **SubmissionFailed** event. Postman expects HTTP 503. |
| 4 | **Manual retry with same ID and body** | Reuses that same payment and provider identifiers. The stub now approves it; Zapper updates the existing payment to **Approved** and adds an **Approved** event. No second `Payments` row is created; the earlier E29 remains in lifecycle history. |

## 06 – E50 gateway failure

Reviewed run: the payment was R490.76 and ended as `SubmissionFailed` with `E50`. The stub did not retain a transaction for the failed request, and no callback was recorded.

| # | Postman request | What happens |
|---|---|---|
| 1 | **Set GatewayFailure scenario** | Configures the stub. No database change. |
| 2 | **Submit payment (expect E50)** | Zapper creates one payment and a **Submitted** event. The stub returns a gateway failure with `E50`; Zapper updates that payment to **SubmissionFailed** and adds a **SubmissionFailed** event. Postman expects HTTP 502. |
| 3 | **Reset scenario to Approved** | Resets the stub’s default. No database change. |

## 07 – RRN collision

Reviewed run: both direct requests used R90.09 and shared RRN `792421613593`. The first transaction was stored as Approved / Success; the second was rejected as the `E09` collision. Neither request created Zapper database rows.

This folder calls the Acme stub directly, so its payment attempts bypass Zapper and do not create rows in Zapper’s PostgreSQL tables.

| # | Postman request | What happens |
|---|---|---|
| 1 | **Ensure default outcome is Approved** | Sets the stub’s default. No database change. |
| 2 | **Prepare unique collision values** | Generates two transaction IDs, two order numbers sharing the same first 12 characters, and one fresh payment amount (300–10,000 minor units). The same amount is sent in both requests so only the RRN collision differs. The health request checks the stub; no database change. |
| 3 | **Submit first Acme payment (reserve RRN)** | Sends a direct authenticated request to the stub. The stub stores the transaction in memory and returns success (`00`) for the generated amount. |
| 4 | **Submit second Acme payment (expect E09 collision)** | Sends a direct request with a different transaction ID but the same RRN prefix. The stub returns HTTP 409 / `E09` and identifies the first transaction. No Zapper database change. |

## 08 – Provider query and daily report

Reviewed run: the new approved payment was R847.73. Acme's authenticated query returned that transaction as Success / `00`. The daily report for 19 October 2026 (SAST) included it in Cape Demo Coffee's approved totals: 12 approved payments totaling R5,435.40, two declines totaling R807.49, one reversal totaling R126.41, and refunds totaling R3.00. Joburg Demo Books showed one decline totaling R25.00.

| # | Postman request | What happens |
|---|---|---|
| 1 | **Ensure default outcome is Approved** | Sets the stub’s default. No database change. |
| 2 | **Create approved payment for provider query** | Creates a payment and **Submitted** event; the stub approves it, and Zapper updates the payment and adds an **Approved** event. |
| 3 | **Capture query transaction ID** | Reads the stub’s in-memory transaction list. No database change. |
| 4 | **Query Acme by TransactionID** | Sends an authenticated RTQ request directly to the stub and reads the provider transaction. No PostgreSQL change. |
| 5 | **Daily settlement summary (today, SAST)** | Zapper reads lifecycle events joined to payments and sub-merchants to build the report. It does not update those tables. |

## What to look for in DBeaver

`Payments` shows each payment’s current result. `PaymentLifecycleEvents` shows how it reached that result. `InboundNotifications` shows which callbacks arrived and how they were handled. After the E29 retry, for example, `Payments` shows Approved while `PaymentLifecycleEvents` retains the earlier E29 failure.
