# Day 4 — Background Work: Jobs & Schedules

This document covers the implementation of background jobs, scheduling, and reliability concerns using Hangfire and .NET's `BackgroundService`.

---

## 1. Job Configurations & Cron Schedules

The following background jobs are registered in the system:

| Job Name | Type | Cron / Trigger | Execution Timezone | Method Called |
| :--- | :--- | :--- | :--- | :--- |
| **Send OTP SMS** | Fire-and-Forget | Upon OTP request | UTC | `ISmsService.SendOtpSmsAsync` |
| **Cancel Stale Orders** | Recurring | `*/5 * * * *` (Every 5 minutes) | UTC | `IOrderService.CancelStalePendingOrdersAsync` |
| **Nightly Sales Report** | Recurring | `0 2 * * *` (Daily at 02:00 UTC) | UTC | `IOrderService.GenerateNightlySalesReportAsync` |
| **Send Store Reports** | Continuation | Automatically after Nightly Sales Report | UTC | `IOrderService.SendStoreReportsForDateAsync` |
| **Hygiene Cleanup** | Recurring | `0 3 * * *` (Daily at 03:00 UTC) | UTC | `ITokenService.CleanupRefreshTokensAsync` |

---

## 2. Hangfire Dashboard Security
* **Implementation**: Enforced using `HangfireAuthorizationFilter` which implements `IDashboardAuthorizationFilter`.
* **Security Details**: It extracts the JWT token from either the query string (`?token=...`) or from a secure cookie named `hangfire_token`. It then runs full signature and expiration validation against the JWT.
* **Why it matters**: A public Hangfire dashboard exposes job execution queues, history, exception details, and allows unauthorized users to trigger, delete, or cancel critical business processes, resulting in denial of service or data leaks.

---

## 3. Retries & Idempotency: The Double-Refund Issue
* **The Problem**: If a background job is interrupted midway (e.g., app restarts, DB timeout) after restoring the product stock but *before* committing the order status to `Cancelled`, Hangfire will automatically retry the job. On retry, a naive job will refund the stock *again*, leading to inventory inflation.
* **The Fix**: The cancellation query holds update locks on the target rows, and the service re-verifies that the order status is still `Pending` before executing the stock refund loop. If it has already been cancelled in a previous attempt, it rolls back gracefully and terminates.

---

## 4. BackgroundService vs Hangfire Classification

| Scenario | Chosen Tool | Deciding Factor |
| :--- | :--- | :--- |
| **1. Send OTP text right after request-OTP** | **Hangfire** (Fire-and-forget) | Needs to be fast, reliable, retried on transient failures, and must not block the API thread. |
| **2. Nightly per-store sales reports** | **Hangfire** (Recurring) | Must execute on a strict schedule, exactly once across all server instances, and persist through crashes. |
| **3. Log 60s status heartbeat** | **BackgroundService** | Lightweight, transient, in-memory polling where execution history or cross-instance coordination is unnecessary. |
| **4. Cancel stale orders every 5 minutes** | **Hangfire** (Recurring) | Critical business operation requiring persistent scheduling, retry capabilities, and single-instance execution. |
| **5. Retry failed payment capture with backoff** | **Hangfire** (Delayed/Scheduled) | Requires long-term state persistence across app restarts and built-in exponential backoff retries. |
| **6. Drain in-memory event queue** | **BackgroundService** | High-throughput, low-latency, real-time loop operating in memory with no need for persistent DB task overhead. |
