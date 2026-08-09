# Day 4 — Background Work: Jobs, Schedules, and Making Them Safe to Run Twice

## Where we are

Everything you've built happens *inside a request*: someone calls, you do the work, you
answer. But real systems do a lot of work with **nobody waiting** — send the OTP text,
cancel orders the customer abandoned, run last night's sales report, retry the thing that
failed at 3am. That work happens in the **background**.

Today you add a background job system (**Hangfire**) and meet .NET's built-in
**background services**. And you learn the one rule that governs all of it:

> A background job can fail and be **retried automatically**. So every job runs
> **at least once — and sometimes more than once.** If running a job twice does damage,
> the job is broken.

That rule should sound familiar — it's Day 3's idempotency, now as a daily fact of life.
The work you did to make Place Order safe to repeat is the same work every job needs.

**Everything builds on your Day 1–3 repo. Branch: `day-04-background`.**

---

## Ground rules (carried, plus one)

Thin controllers, DTOs, defend every choice, clean clone, write-ups. Today's new law:

**Assume every job runs at least twice.** Before you write a single job, ask: "if this
runs two or three times instead of once, is the result still correct?" If not, fix the
job, not your hopes.

---

## Part 0 — Hangfire in, and locked down

Add Hangfire, backed by your SQL Server (it creates its own tables on first run — no EF
migration needed for Hangfire itself). Turn on the **Hangfire dashboard** — Hangfire's own
built-in web page for watching jobs run, retry, and fail.

Then immediately **secure the dashboard.** Out of the box it is reachable by anyone who
finds the URL — and it shows job data *and lets people trigger and delete jobs*. An open
Hangfire dashboard is a real, common production hole. Lock it so **only an Admin** (your
Day 2 role) can open it. Write an authorization filter that reuses your existing auth —
do not invent a second login for it.

While you're here, get comfortable with **cron expressions** (you'll write several today)
and remember your Day 1 rule: everything runs in **UTC**. A report scheduled for "2am"
had better mean 2am in a timezone you chose on purpose, not by accident.

---

## Part 1 — Fire-and-forget: get the SMS off the request thread

Right now your OTP/SMS "send" happens *inside* the request-OTP call — the customer waits
for it. Move it to a **fire-and-forget** Hangfire job: the request enqueues the send and
returns immediately; a worker does the actual send.

Then think about what you just changed, and write it down:
- The request is now faster, but the code arrives a moment *after* the response. For an
  OTP the customer is waiting on — is that fine? What's the tradeoff, and is it right here?
- Hangfire will **retry** a failed send. If your send job runs twice, does the customer
  get two texts? Is that acceptable, and how would you make it not happen twice if it
  weren't? (You don't have a real SMS gateway, so "sent" = logged — but reason as if it
  were real money per message.)

---

## Part 2 — A recurring job: cancel abandoned orders (and give the stock back)

Customers create orders and never pay. Those `Pending` orders sit forever, and worse, they
**hold stock** nobody is buying. Build a **recurring job** (cron, e.g. every 5 minutes)
that finds orders stuck in `Pending` longer than, say, 30 minutes and cancels them —
returning their stock, exactly like a real cancel.

This job pulls three earlier days together, on purpose:
- **Day 1:** cancelling must return stock and write an `OrderStatusHistory` row, following
  your real status rules — not a raw `UPDATE status = Cancelled`.
- **Day 2:** this job has **no logged-in user**. So "who cancelled it?" has no human
  answer — it's the *system*. Use the same "no current user" decision you made for the
  seeder. The audit trail must still make sense.
- **Day 3:** what if this job cancels an order at the *same instant* a Partner accepts it?
  Two writers, one order. Your Day 3 concurrency guards apply here too — the job is just
  another concurrent writer.

---

## Part 3 — Scheduled work: cleanup, a nightly report, and a continuation

Three more jobs, to cover the rest of Hangfire's shapes:

1. **Recurring cleanup — every day at 03:00 UTC.** Delete refresh-token rows
   whose expiry is already in the past, and revoked refresh-token rows that were revoked
   more than 7 days ago. Keep the recently-revoked ones — your Day 2 reuse-detection still
   needs them, so that a just-stolen token replayed a few days later is still recognised as
   a *revoked* token and not an unknown one.
2. **A nightly report — every day at 02:00 UTC.** Build yesterday's sales summary per store:
   order count, total revenue, and the single top-selling product. Store each store's
   summary as a new `DailyReport` row (generate that migration yourself) so it can be read
   back later.
3. **A continuation.** When the report job finishes, a **second** job runs automatically
   after it (a Hangfire *continuation*): send each Partner their own store's report, routed
   through a notification abstraction — call it `INotificationEngine` if you're matching
   MyThings' house naming — whose only implementation for now writes to the logs. "Do B
   only after A succeeds" is a common real pattern; wire it as a continuation, not by
   cramming both steps into one job.

Write every cron expression yourself and state in the write-up what each one means in plain
words (e.g. `0 2 * * *` = "every day at 02:00 UTC").

---

## Part 4 — Retries and idempotency: prove a job is safe to run twice

This is the heart of the day. Hangfire retries failed jobs automatically — so you must
prove your jobs survive being run more than once. You'll force it and watch.

**The experiment (be exact):**
1. Take your Part 2 cancel job. Temporarily add a forced failure *in the middle* — after it
   returns the stock but **before** it marks the order `Cancelled` (throw an exception right
   there, on the first attempt only).
2. Trigger the job on an abandoned order and let Hangfire **retry** it.
3. Watch the stock. On the first attempt it gave the stock back but crashed before marking
   the order cancelled. On the **retry**, the order is still `Pending`, so a naive job gives
   the stock back **again** — the product's stock is now too high. You just invented
   inventory out of a retry.

**The fix:** make the job **idempotent** — running it once or five times lands the same
correct result. Think about doing the whole thing as one atomic step, or checking the
order's real state before acting, so a re-run can't double-refund. Remove the forced
failure, then prove it: force the crash, let it retry, and the final stock is exactly
right.

Now look back at your other jobs (SMS send, cleanup, report) and ask the same question of
each: **what if this runs twice?** Fix any that would misbehave.

---

## Part 5 — `BackgroundService` vs Hangfire: know which tool, and why

Hangfire is not the only way to run background work. .NET ships `BackgroundService`
(an `IHostedService`) — a task that starts with your app and runs continuously.

Build this exact `BackgroundService`: every 60 seconds it logs one line showing how many
orders are currently in each active status — `Pending`, `Accepted`, `Preparing`,
`OutForDelivery`. A simple live heartbeat of the system's state.

Then the lesson. A `BackgroundService` lives **in your app's memory**: it forgets
everything on restart, does not retry, and there is one copy of it per app instance.
Hangfire **persists** jobs in the database: they survive restarts, get retried, and run
once across all instances. Given that, decide **which tool fits each of these six**, and
justify each in your write-up:

1. Send the OTP text right after request-OTP.
2. Generate the nightly per-store sales reports.
3. Log the every-60-seconds status heartbeat above.
4. Cancel abandoned `Pending` orders every 5 minutes.
5. Retry a failed payment capture, with backoff, until it succeeds or gives up after a day.
6. Continuously drain an in-memory queue of just-arrived events as fast as they land.

There is no trick — each has a clearly better choice. The marks are in the *why*: name the
deciding factor for each (must it survive a restart? does it need retries? is it a schedule,
a one-off, or a continuous loop?).

---

## Part 6 — Write-up

`docs/day-04-background.md`:
- The four job shapes you used — fire-and-forget, recurring, scheduled/continuation — and
  which real task each one runs.
- Every cron expression, in plain words, and which timezone it runs in.
- How you secured the Hangfire dashboard, and why an open one is dangerous.
- The retry/idempotency story: the double-refund you caused and exactly how you killed it.
- `BackgroundService` vs Hangfire: your call on each of the six scenarios, and the deciding
  factor you used.
- The hardest thing today, and how you worked it out.

---

## Definition of Done

- [ ] Hangfire running on SQL storage; dashboard works and is **Admin-only** (reusing Day 2
      auth), verified by trying to open it as a non-admin.
- [ ] SMS/OTP send moved to a fire-and-forget job; the request no longer waits on it.
- [ ] Recurring job cancels stale `Pending` orders, returns stock, writes history as the
      *system* user, and respects your Day 3 concurrency guards.
- [ ] Cleanup job, nightly per-store report (stored as `DailyReport`), and a continuation
      after the report all work; crons documented in plain words and UTC.
- [ ] The double-refund retry is reproduced and then fixed; every job is idempotent and you
      can prove the cancel job is safe under a forced mid-way failure + retry.
- [ ] One `BackgroundService` built; write-up classifies all six scenarios (BackgroundService
      vs Hangfire) with the deciding factor for each.
- [ ] `docs/day-04-background.md` complete. Branch merged; clean clone still runs (README:
      how to open the Hangfire dashboard and as whom).

---

## Stretch goals (only if you still have fuel)

1. **Run two app instances against one database and watch the recurring job.** With two
   Hangfire servers, does your every-5-minutes cancel job fire **once** or **twice per
   tick**? Find out, then make it run exactly once no matter how many instances there are.
   (This is the same "one copy vs many copies" trap from Day 3's stretch — and it gets very
   real when we split into multiple apps.)
2. **The enqueue gap (a preview of Day 6).** When you place an order and then enqueue a job,
   what happens if the app dies *between* the database commit and the enqueue? The order
   exists but the job never runs — or the reverse. Note exactly where the gap is; on
   **Day 6** you'll close it properly with the **outbox pattern**.
3. **A poison job.** Make a job that always fails. Watch Hangfire retry it and then give up.
   Where does it end up? How would an on-call engineer find it and deal with it?
4. **Batch fan-out** — the nightly report, but one child job per store running in parallel,
   with a final job that runs only once *all* store reports are done.

Do the work when no one's watching — and make sure that if it runs twice, nobody can tell.
