# Day 9 — The Performance Hunt: Measure, Fix, Measure Again

## Where we are

You have built a lot. Orders, auth, jobs, messages, an outbox, live tracking, notifications.
Along the way, nobody ever asked how fast any of it is.

Today you find out. This is a **hunt day**. You are not adding features. You are going to attack
your own system with a stopwatch, find the slow parts, understand exactly *why* they are slow,
fix them, and prove the fix with numbers.

This is your second break day. Day 3 you broke your system with many requests at once. Today you
break it with a lot of data.

### The one law of today

> **Measure first. Never guess.**

Every developer has a feeling about which part of their code is slow. That feeling is wrong most
of the time. A number is right every time.

So today, nothing gets changed until you have written down what it costs now. And nothing counts
as fixed until you have written down what it costs after. No number, no fix.

### One thing you may not do today

**Do not add any caching.** Not anywhere.

Caching means keeping an answer so you do not have to work it out again. It is useful, but it
does not make slow code fast — it just makes slow code run less often. The slowness is still
there, waiting.

Today you fix the slow thing itself. If you hide it behind a cache, you will never learn what was
wrong with it.

**Everything builds on your Day 1–8 repo. Branch: `day-09-performance`.**

---

## Ground rules (carried, plus one)

Thin controllers, DTOs, defend every choice, clean clone, write-ups. Today's new rule:

**Fix the biggest number first.**

If one endpoint takes 900 ms and another takes 5 ms, making the 5 ms one twice as fast is wasted
work. Nobody will ever notice. Always spend your time where the numbers are big.

---

## Part 0 — Full data, and the tools to see with

### Get your data back to full size

Check how many rows are in your database right now.

Your Day 1 targets were **300,000 orders** and about **900,000 order lines**. If your database
has shrunk since then — and it may have, if you reseeded smaller to make life easier — put it
back to the full size before you do anything else.

This is not optional. A performance hunt on 500 rows finds nothing. Every mistake you are looking
for today hides completely on small data and only appears at scale. That is the whole reason
Day 1 made you build a seeder.

### Four tools you need

You already have the first two. Get comfortable with all four.

1. **Your request-duration log** (Day 1). Every request logs its path and how many milliseconds
   it took. This is how you find *which* endpoint is slow.
2. **EF Core SQL logging** (Day 1). This shows you the actual SQL your LINQ produced, and **how
   many queries** one request made. This is how you find *why*.
3. **The actual execution plan.** Turn it on in your SQL client and run a query. It draws you a
   picture of what the database really did. You are looking for the words **scan** and **key
   lookup**. On Windows this is SSMS, with "Include Actual Execution Plan" turned on. **On a Mac
   or Linux, read the next section first** — SSMS does not run there.
4. **`SET STATISTICS IO ON`.** Run this in your SQL client before a query, and SQL Server tells
   you how many **logical reads** it did — how many 8KB pages it had to touch to answer you.

### If you are not on Windows

**SSMS only runs on Windows.** If you are on a Mac or on Linux, you can still do every part of
today. You only need a different tool for tool 3, the execution plan. Tool 4 already works
everywhere — see the last paragraph of this section.

**Best option: VS Code with the "SQL Server (mssql)" extension.** Free, made by Microsoft, and it
runs on Mac and Linux. It gives you the same graphical plan that SSMS does: an **Estimated Plan**
button next to Run Query, and an **Enable Actual Plan** button beside it.

**Do not use Azure Data Studio.** Microsoft retired it in February 2026.

**If you would rather stay in DBeaver**, right-click your query and choose *Explain Execution
Plan*. You get a tree of rows instead of the picture — harder to read, same information.

**The fallback that works in every client** is to ask SQL Server for the plan as text:

```sql
SET STATISTICS PROFILE ON;
-- your query here
```

You get one row per step. The **PhysicalOp** column is where you find the words *Index Seek*,
*Index Scan* and *Key Lookup*.

**One piece of good news.** `SET STATISTICS IO ON` — the logical reads, the number that matters
most today — comes from the **server**, not from your tool. It works the same everywhere. In
DBeaver look in the **Server Output** panel; in VS Code look in the **Messages** tab.

### Why logical reads matter more than milliseconds

This is the most useful thing in today's task, so read it twice.

Time is unreliable. The same query takes different times depending on what else your machine is
doing, and on whether the data was already in memory from the last run. Run a query twice and the
second one looks fast — not because you fixed anything, but because it was still in memory.

**Logical reads do not lie.** If a query touched 40,000 pages before your change and 12 pages
after, you genuinely made it better. If the reads are the same and only the time dropped, you got
lucky with memory and fixed nothing.

So for every fix today, record **both**: the time and the logical reads.

### How to measure time properly

Never trust one run. For every endpoint you measure:

1. Call it once and throw that result away. (The first call is always slower — the code and the
   data are being loaded for the first time.)
2. Then call it **10** times.
3. Write down the **middle** value, not the average. One unlucky slow run ruins an average.

---

## Part 1 — Build your list of suspects

### First, write down your guess

Before you measure anything, open your write-up and write down which **three** endpoints you
think are slowest, and why you think so.

Do this now, before you look at any numbers. You will compare it later. Most people get at least
one badly wrong, and finding that out is the point.

### Now measure everything

Go through **every** read endpoint you have built since Day 1. Order details, customer order
history, product search, the store dashboard, the store sales tally — all of them.

For each one, record the median of 10 runs, on your full-size database.

Put them in a table in your write-up, slowest first.

### Then measure them again, under load

An endpoint that is fine on its own can fall apart when 20 people call it at once. A single
stopwatch will never show you that.

You already built the tool for this on Day 3: your concurrency harness. Get it out.

For your **five slowest** endpoints, fire **20 requests at the same time** and record how long
they take now. Compare against the single-request number.

Some will be about the same. At least one will be much worse. That one is your most interesting
problem, because it is the one that will fall over on a busy evening while looking perfectly fine
in your testing.

### Compare with your guess

Now look back at the three endpoints you guessed. How many did you get right?

Write the answer down honestly. This is the lesson of Part 1: **your feeling about what is slow
is not evidence.**

---

## The incident format (use this for Parts 2 to 6)

For each problem you find, write an entry in `docs/day-09-incidents.md`:

1. **What you measured** — the endpoint, and the starting numbers (time and logical reads).
2. **What you expected** — what a sensible system should cost here.
3. **What you found** — the real numbers, the SQL, and how many queries ran.
4. **Why it was slow** — the actual reason. Point at the SQL or the execution plan. Not a guess.
5. **What you changed** — and what that change costs you elsewhere.
6. **The numbers after** — time and logical reads, measured the same way as before.

Step 4 is the one that matters. If you cannot say exactly why it was slow, you did not fix it —
you changed something and it got faster by accident.

---

## Part 2 — Hunt #1: one request, many queries

Turn on your SQL logging. Call your **customer order history** endpoint, asking for one page of
20 orders. Now count the SQL queries in your log.

If you see **1 query, then 20 more** — one for each order, to fetch its store name or count its
lines — you have found an **N+1**.

The name says what it is: 1 query to get the list, then N more, one per row. With 20 rows that is
21 trips to the database instead of 1. It looks harmless on your screen. On a page of 100 rows,
over a real network, it is a disaster.

**Do this:**

1. Count the queries for one call. Write the number down.
2. Find the line of C# that causes the extra queries. It is usually a property being read inside
   a loop, or inside the code that builds your DTO.
3. Fix it so one call makes **one** query.
4. Count again. Measure again.

Then check your other list endpoints for the same problem. Order details and the store dashboard
are worth a careful look.

If one of your endpoints genuinely does not have this problem, prove it — show the query count —
and say what you did differently there.

---

## Part 3 — Hunt #2: the query that returns far too many rows

Look at your **order details** endpoint. It loads one order with its **lines** and its **status
history**.

Log the SQL. Then run that SQL yourself in your SQL client and count the rows that come back.

An order with 8 lines and 5 status rows should need 13 rows of child data. You will probably see
**40**.

Here is why. When one SQL query joins a parent to **two** different child lists at once, the
database has no way to keep them separate. It gives you every line paired with every history row
— 8 × 5 = 40 rows. Your code then throws most of them away.

With 8 and 5 that is wasteful. With 50 lines and 20 status rows it is 1,000 rows to build one
page.

**Do this:**

1. Count the rows the query really returns, and compare with what you actually need.
2. Fix it. Research how EF Core can fetch the two child lists as **separate** queries instead of
   one joined query. There is also a second answer: only ask for the columns you display.
3. Measure again — rows returned, time, and logical reads.

In your write-up, explain in your own words why joining two child lists multiplies rows.

---

## Part 4 — Hunt #3: the missing index, and what an index costs

Take your **customer order history** query — one customer's orders, newest first, one page.

Run it in your SQL client with the actual execution plan turned on.

Look at the picture. If you see a **scan** over your orders table, that means SQL Server read
every one of your 300,000 orders to find the 20 you asked for. It had no faster path.

**Do this:**

1. Record the logical reads before you touch anything.
2. Work out which index this query needs. Think about which column it filters on, and which column
   it sorts by. Both matter.
3. Add it. Generate the migration yourself.
4. Run the query again. The scan should become a **seek**, and the logical reads should fall a
   long way.

### Now measure what that index cost you

An index is not free. It makes reads faster and writes slower, because every insert must now also
update the index.

**Prove it:**

1. Time a bulk insert of **10,000** orders before your new indexes. (Use your Day 1 seeder.)
2. Time the same insert after.
3. Write down both numbers.

Now answer this in your write-up: how many indexes is too many? What is your rule for deciding
whether an index earns its place?

Then go through every index you added today and, for each one, name the exact query it serves. If
you cannot name one, drop the index.

---

## Part 5 — Hunt #4: the count nobody needed

Every paged endpoint you built returns `totalCount` along with the items. That count is a second
query, and on a big table it is often **slower than fetching the page itself**.

**Do this:**

1. Time the two halves separately: the query that fetches 20 rows, and the query that counts all
   matching rows. Write down both.
2. On 300,000 orders, the count may well be the bigger number. Confirm whether it is for you.

Now think about it as a product question, not a code question.

- Who reads that number? A customer scrolling their orders does not care that they have 1,247 of
  them.
- Does your app show it anywhere at all?
- Would "there are more results" be enough, instead of an exact total?

**Your job:** pick one paged endpoint and make the count cheaper. You may make it optional, ask
for one row more than the page size to learn whether more exist, or remove it. Do not cache it.

Then write down the rule you learned. Sometimes the fastest thing you can do with a query is stop
running it.

---

## Part 6 — Hunt #5: the slow thing no endpoint test will ever find

Everything so far was started by a user. Now look at the work your system does when nobody is
there.

Your **outbox relay** runs constantly. It asks the database "anything to send?" over and over,
forever. Almost every time, the answer is no.

**Do this:**

1. Work out how many times your relay queries the database in one hour. Do the arithmetic from
   your poll interval.
2. Leave your app running for 10 minutes with **no traffic at all**. Count the relay's queries in
   your SQL log, and how many of them found nothing.
3. Write down both numbers.

No endpoint test would ever have shown you this. It is invisible load, and it runs all night.

**Now fix it, without breaking Day 6.** Make the relay slow down when there is nothing to do, and
speed straight back up when work arrives. A common approach is to widen the wait each time a poll
comes back empty, up to some limit, and reset it the moment a row is found.

**The hard part:** Day 6 gave you a firm rule — an order must reach the consumer in under **2
seconds**. Your fix must not break that rule. Measure the delay again after your change and prove
it still holds.

This is a real trade, and it is the same shape as the index trade in Part 4: you made the quiet
case much cheaper, and you must show the busy case did not suffer.

---

## Part 7 — Write-up

`docs/day-09-performance.md`:

- **Your guess from Part 1, and what the numbers actually said.** Be honest about what you got
  wrong. This is the most valuable paragraph in the document.
- Your full table of endpoint timings, before and after, slowest first.
- Which endpoint got much worse under 20 concurrent requests, and why.
- For each hunt: the before and after numbers, in **time and logical reads**.
- Why logical reads are a better measure than milliseconds, in your own words.
- Every index you added, and the exact query each one serves.
- What your new indexes cost you on insert, with the two numbers.
- Your decision on the count query, and the reasoning behind it.
- The relay's queries per hour, before and after, and proof that the 2-second rule still holds.
- **One slow thing you chose NOT to fix, and why.** Something that is genuinely slow but rarely
  called, or where the fix would cost more than the problem. Knowing what to leave alone is a real
  skill, and there should be at least one on your list.
- The hardest thing today, and how you worked it out.

---

## Definition of Done

- [ ] The database is back at full size: ~300,000 orders and ~900,000 lines.
- [ ] Your Part 1 guess is written down **before** any measurement, and compared afterwards.
- [ ] Every read endpoint is timed (median of 10 runs) and listed slowest first.
- [ ] The five slowest are also measured at 20 concurrent requests, using your Day 3 harness.
- [ ] `docs/day-09-incidents.md` has a full six-step entry for each hunt, and step 4 gives the real
      reason every time.
- [ ] The N+1 is found and fixed: one request now makes one query, with counts shown before and
      after.
- [ ] The two-child-list query is fixed, with rows returned shown before and after.
- [ ] A scan is turned into a seek with the right index, with logical reads before and after.
- [ ] The insert cost of your new indexes is measured with 10,000 rows, before and after.
- [ ] One paged endpoint's count query is made cheaper, without caching, and the decision is
      explained.
- [ ] The relay's queries per hour are measured, reduced, and the Day 6 two-second rule is proven
      to still hold.
- [ ] **No caching was added anywhere today.**
- [ ] Every index added is justified by naming the query it serves.
- [ ] `docs/day-09-performance.md` complete, including the thing you chose not to fix. Branch
      merged; clean clone still runs.

---

## Stretch goals (only if you still have fuel)

1. **Find the slowest query in the whole database, without guessing.** SQL Server keeps statistics
   about every query it has run. Research `sys.dm_exec_query_stats` and write a query that lists
   the top 10 by total time. Did it find anything your endpoint testing missed?
2. **Make one endpoint fast with raw SQL or Dapper.** Take your worst remaining endpoint and write
   the query by hand instead of with EF. Measure the difference. Then answer the real question:
   was it worth giving up EF for, and what did you lose?
3. **Find a query that gets slower as one customer gets busier.** Seed one customer with 5,000
   orders while everyone else has 10. Now measure their order history against a normal customer's.
   Real systems always have one customer like this, and they are the ones who complain.
4. **Watch memory, not just time.** Measure how much memory one call to your heaviest endpoint
   allocates. A response that builds huge object graphs can be fast and still hurt your server
   under load.

Your feeling is not evidence. Your stopwatch is. Go and find out how wrong you were.
