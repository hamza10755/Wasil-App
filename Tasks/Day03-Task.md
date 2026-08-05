# Day 3 — Concurrency: Break Your Own System Under Load, Then Make It Unbreakable

## Where we are

Days 1 and 2 you built features and locked doors — and you tested them one request at a
time. That is not how a real system gets used. In production, hundreds of requests hit
the same rows at the same instant. Code that is obviously correct for one user does
impossible things under a crowd: it sells stock you don't have, mints the same order code
twice, and hands out more OTP guesses than you allowed.

Today is different from the last two. Today you are not adding features — you are
**attacking your own system under load, watching it break, and then making it correct.**
This is your first *incident* day. Each thing that breaks gets a short postmortem, then a
fix, then proof the fix holds. By tonight you'll own the tools that make concurrent code
correct — transactions, isolation, optimistic vs pessimistic locking, atomic updates,
idempotency — not as words you read, but as things you broke and repaired with your own
hands.

**Everything builds on your Day 1 + Day 2 repo. Branch: `day-03-concurrency`.**

---

## Ground rules (carried from before, plus one)

Thin controllers, DTOs, defend every choice, clean clone, write-ups. And today's new law:

**A bug you cannot reproduce on demand is not fixed — it's hiding.** For every race you
tackle today, you must first be able to *trigger it reliably*, then fix it, then *trigger
the same experiment again and show it gone*. "I added a transaction and it feels safe now"
is worth nothing. Reproduce → understand → fix → prove. Every time.

---

## Part 0 — Build a way to hammer your own API

You cannot find a race by clicking Swagger. You need to fire many requests at the **same
instant**. Build or choose a small concurrency harness:

- a tiny C# console that fires N calls with `Task.WhenAll`, or
- a load tool (k6, NBomber, JMeter, bombardier — your pick, defend it).

It must let you point at an endpoint, fire **N truly simultaneous** requests, and report
how many succeeded, how many failed, and let you check the resulting database state. You
will reuse this all day.

One warning that is itself a lesson: a `for` loop that sends one request, waits, sends the
next — is **sequential**, and sequential calls hide every race. The requests must overlap
in time. Make sure your harness really fires them together (fire-then-await-all, not
await-each), and prove to yourself that it does.

---

## The incident format (use this for Parts 1–4)

For each incident, write an entry in `docs/day-03-incidents.md`:

1. **Experiment** — exactly what you fired (endpoint, how many parallel, starting state).
2. **Expected** — what a correct system should do.
3. **Observed** — what actually happened (numbers, the bad DB rows, screenshots welcome).
4. **Why** — the interleaving that caused it. Walk two requests through step by step.
5. **Fix** — what you changed and *why that closes the race*, plus what it costs.
6. **Proof** — the same experiment re-run, now behaving.

Do not skip step 4. If you can't explain the exact interleaving that broke it, you don't
understand it yet, and your fix is a guess.

---

## Part 1 — Incident #1: Oversold stock (the classic)

Pick a product and set its `StockQuantity` to **1**. Now fire **50 place-order requests
for that product at the same instant** with your harness.

A correct system sells exactly one and rejects the other 49. Watch what your Day 1 code
actually does. Most likely: many orders succeed and stock goes to zero or **negative** —
you sold units you never had. That's a **lost update**: every request read "1 in stock,"
every request passed the check, every request wrote.

Reproduce it, document it, and *understand the interleaving* before you touch it. Then fix
it. There is more than one tool that works here — research and choose, and be ready to
defend why yours closes the race and what it costs under load:

- database **transactions** and **isolation levels**;
- **optimistic concurrency** (a version/rowversion column that makes a write fail if the
  row changed since you read it — you'll add the column and generate the migration
  yourself);
- **pessimistic locking** (lock the row while you work);
- a single **atomic update** (`decrement only if enough in stock`, done in one statement).

Fix it, re-run the 50, and prove stock never goes below zero and exactly one order won.

---

## Part 2 — Incident #2: Duplicate order codes

Fire **many concurrent place-order requests** (across products with stock, so orders
actually succeed) and collect every `OrderCode` generated. Look for collisions —
especially in that sequential 3-digit tail. If your code generates the number by "read the
last one, add one, save," two parallel requests read the same last value and produce the
same code.

Show the collision (or, if your design already prevents it, prove *why* and how you tested
that). Then fix it — and here's the twist that separates a real fix from a crash:

Your Day 1 unique constraint on `OrderCode` is a safety net; the database will refuse a
duplicate. But a safety net that throws a raw 500 in the customer's face is not a fix, it's
a different bug. So make the code both **safe** (never two identical codes committed) *and*
**graceful** (a collision is handled, not a stack trace). Think about: catching the
constraint violation and retrying, a database-generated sequence, or a design where the tail
can't collide by construction. Defend your choice.

---

## Part 3 — Incident #3: The OTP guess-limit bypass (your Day 2 door)

Your OTP allows a few wrong tries (say 5) before the code dies. Request a code for a phone,
then fire **many wrong-guess verify requests at the same instant**. Because the attempt
counter is almost certainly "read the count, check it's under the limit, increment, save,"
several requests read the same "0 used" together — and you get **far more than 5 tries**.
Enough parallel rounds and a million-code space is brute-forceable after all. Show it.

Then a second experiment: fire **two verifies with the correct code at the same moment.**
Does your single-use code get consumed exactly once, or do both requests sail through?

Fix both, prove both. Note where this state lives: if your OTP counter is in **Redis**, a
SQL transaction can't help you — the right fix is an **atomic Redis operation**. That's the
real lesson of this part: *the correct tool depends on where the contended state lives.*

---

## Part 4 — Incident #4: Refresh-token rotation race

Log in, grab a refresh token, and fire **two refresh calls with that same token at the same
instant.** Your Day 2 rotation says "each refresh issues a new token and kills the old one,"
and your reuse-detection says "a token used twice means it's stolen — revoke everything."

So which happens under the race? Two possibilities, both wrong:
- you **double-issue** — both calls succeed and now two valid refresh chains exist; or
- your reuse-detection **misfires** — two honest, simultaneous calls look like a replay, so
  you nuke a legitimate user's session.

Find out which your code does. Reason about what *should* happen when the same token is
presented twice at once (hint: exactly one should win, and it should not look like an
attack). Fix it, prove it.

---

## Part 5 — Idempotency: the double-tap

Different from a race, and just as real. A customer taps **Place Order**, the network is
slow, nothing seems to happen, so they tap again — or the mobile app quietly retries a
request it thinks failed. Either way, the *same* order arrives more than once.

**The experiment (be exact):**
1. Pick one valid order: a real customer, a store, and one product that has stock. Save
   that exact request body.
2. With your harness, send that **identical** request **10 times at the same instant.**
   Then, separately, send it once, wait for the response, and send the very same body
   again a few seconds later.
3. Query the orders for that customer. You will find **10 orders** from step 2 (plus more
   from step 3) where there should be **one**. On a paid order, that's charging one person
   ten times.

**The fix.** Make place-order **idempotent**: the caller includes an *idempotency key* — a
unique id it generates once per real attempt and reuses on a retry (research how this is
normally passed in and stored). A repeat of the same key must return the **same single
order and the same response**, never a new one. One warning: "SELECT to check if it
exists, then INSERT" is *itself* the check-then-act race from Incident #1 — solve it
properly, not with another race.

**Prove it:** run the exact experiment again — 10 at once plus the delayed retry, all with
the same key — and confirm **exactly one** order exists.

This idea comes back hard in two weeks, when a queue can deliver the same message twice —
so learn it cold now.

---

## Part 6 — Induce a deadlock, then design it out

Locking prevents races — and creates a brand-new danger. A **deadlock** is two transactions
each holding something the other one is waiting for, so both wait forever. SQL Server breaks
the tie by killing one of them (the "deadlock victim") — that customer's request dies with
an error.

You can build one straight out of your own place-order code. Placing an order is a single
all-or-nothing transaction (Day 1), and it touches the **stock row of every product in the
order** — so an order with two products takes two row locks inside one transaction. Two
such locks, grabbed in different orders by two orders at once, is all a deadlock needs.

**The experiment (be exact):**
1. Create two products in the same store — call them **P1** and **P2** — both with stock.
2. Prepare two place-order requests:
   - **Order A** has two lines in this order: **P1 first, then P2.**
   - **Order B** has the same two products in the **opposite** order: **P2 first, then P1.**
3. Fire Order A and Order B **at the same instant**, and repeat that pair ~20 times in a
   loop to reliably hit the timing.
4. What happens: Order A's transaction locks P1's stock row, then reaches for P2's — while
   at that same moment Order B has locked P2's row and is reaching for P1's. Each holds the
   row the other needs. SQL Server detects the standoff and kills one order with a
   **deadlock exception** — find it in your logs and read what it says.

**The fix.** It deadlocked because the two orders locked the same products in *different*
orders. Make **every** order touch its products in one **consistent** order — for example,
always decrement stock sorted by `ProductId` — so two orders can never each be holding the
other's next row. Apply that, then re-run the same 20-pair experiment and show the deadlock
can no longer happen.

The lesson is senior-level and simple to say: deadlocks come from *inconsistent* lock
ordering, and die from *consistent* lock ordering.

---

## Part 7 — The toolkit write-up and peer break

### `docs/day-03-concurrency.md`
Explain each of these **in your own words**, and say where you used it today (or why you
didn't):
- Race condition and lost update — the one-sentence version of what went wrong today.
- Transactions and atomicity (all-or-nothing).
- Isolation levels — at least Read Committed vs Serializable/Snapshot, and one anomaly each
  allows or prevents. What level is your database on by default?
- Optimistic vs pessimistic concurrency — how each works, and the rule you'd use to pick
  one over the other.
- Atomic single-statement updates — when one `UPDATE` beats a read-modify-write transaction.
- Unique constraints as the last line of defense — and how to fail gracefully when one fires.
- Idempotency — and what makes an operation safe to retry.

Keep it a 5-minute read, but make it *yours* — this write-up is exactly the kind of thing a
senior interviewer probes, and you'll have real war stories to back every line.

### Peer break (you two, again)
Swap systems with the other trainee. Point your Part 0 harness at **their** endpoints and
try to oversell their stock, duplicate their order codes, and bypass their OTP limit.
Anything you break, hand them a short repro. Fix anything they break in yours. Two people
who just learned to trigger races will find ones you each missed.

---

## Definition of Done

- [ ] A concurrency harness that fires N truly-simultaneous requests, reused across the day.
- [ ] Incident #1 (oversell): reproduced, understood, fixed, and proven — stock can never go
      negative; exactly one order wins the last unit under 50 parallel tries.
- [ ] Incident #2 (duplicate codes): shown or ruled out, fixed to be both safe and graceful
      (no raw 500 on collision).
- [ ] Incident #3 (OTP): both the attempt-limit bypass and the double-consume are fixed and
      proven, with the fix living where the state lives.
- [ ] Incident #4 (refresh race): behavior found, reasoned about, fixed, proven.
- [ ] Place-order is idempotent: the same keyed request 10× yields exactly one order.
- [ ] A deadlock induced on purpose, observed, and then designed out.
- [ ] `docs/day-03-incidents.md` has a real postmortem for each incident (all six steps).
- [ ] `docs/day-03-concurrency.md` toolkit write-up complete and in your own words.
- [ ] Peer break done both ways; findings fixed. Branch merged; clean clone still runs.

---

## Stretch goals (only if you still have fuel)

1. **Distributed lock with Redis.** Your in-process fixes work because there's one copy of
   your app. Run **two** copies behind the same database and re-run the oversell test — does
   your fix still hold? Some fixes (a C# `lock`) break the moment there are two instances; a
   database-level fix survives. For a resource that isn't a single DB row, research a
   **Redis distributed lock** and use it. (This is a preview of a problem that gets very real
   when we split into multiple apps.)
2. **Load-test report.** Push a fixed endpoint hard with your harness and record throughput
   and latency. Find the point where your locking becomes the bottleneck. Keep the numbers —
   we hunt performance in a couple of weeks.
3. **Pick a non-default isolation level** for one specific operation, justify it, and
   demonstrate the exact anomaly it prevents that Read Committed would allow.
4. **Idempotency with response replay** — store the idempotency key and the original
   response, so a retry returns the *original* result byte-for-byte, not just "already done."

Break it on purpose. Then make "many at once" behave exactly like "one at a time."
