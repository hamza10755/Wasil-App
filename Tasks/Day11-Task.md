# Day 11 — Caching: The Easy Half and the Hard Half

## Where we are

Two days ago I banned caching. Today you get it, and now you will understand why the ban was
there.

On Day 9 you made your queries genuinely fast. On Day 10 you learned why they were slow in the
first place. If you had been allowed to cache on Day 9, you would have hidden the bad queries
behind a cache and never learned any of it — and the slowness would still be there, waiting for
the first moment the cache is empty.

**Fix the query first. Cache second.** That order is the whole reason those days came before this
one.

### The easy half and the hard half

Adding a cache takes about twenty minutes. You store an answer, and next time you hand back the
stored answer instead of working it out again.

The hard half is everything after that:

- knowing **which things** are worth storing at all,
- knowing **when the stored answer has become wrong** — this is called **invalidation**,
- and knowing what happens when the cache is empty, full, or missing entirely.

There is an old joke among developers:

> There are only two hard things in computer science: cache invalidation, and naming things.

It is a joke because it is true. Today you will make your cache serve wrong answers in several
different ways, and fix each one.

**Everything builds on your Day 1–10 repo. Branch: `day-11-caching`.**

---

## Ground rules (carried, plus one)

Thin controllers, DTOs, defend every choice, clean clone, write-ups. Today's new rule is the most
important sentence in this document:

> **A cache may only hold things you could lose without breaking anything.**

Here is the test. Ask, about any piece of data:

**If someone wiped the entire cache this second, would my app still be correct — just slower?**

- **Yes** → it can be cached.
- **No** → it must not be cached. Put it in the database.

You met this rule on Day 2, when OTP codes and refresh tokens went into SQL instead of a cache.
Today you find out it was not an opinion. Part 8 ends with you wiping the whole cache while the
app is running — and everything must still work.

---

## Part 0 — Redis up, and the number that decides

### What Redis is

**Redis is a key-value store that keeps its data in memory.** You give it a key, like
`store:42:details`, and a value. Later you ask for that key and get the value back.

It is fast because it is RAM — no disk, no query planning, no joins. It is a separate server
program, like RabbitMQ, and your app talks to it over the network.

### Start it

```bash
docker run -d --name redis -p 6379:6379 redis
```

To look inside it by hand:

```bash
docker exec -it redis redis-cli
```

Learn these six commands. You need all of them today:

| Command | What it does |
|---|---|
| `KEYS *` | Lists every key. **Never run this from application code** — it blocks Redis while it works. Fine by hand on your laptop. |
| `GET key` | Reads one value. |
| `TTL key` | Seconds until this key expires. `-1` means it never expires. |
| `DEL key` | Deletes one key. |
| `INFO stats` | Server-wide counters, including `keyspace_hits` and `keyspace_misses`. |
| `FLUSHALL` | Deletes everything. You use this at the end of the day. |

### The library

Use **StackExchange.Redis** directly.

Two firm instructions:

1. **Put `abortConnect=false` in your connection string.** Without it, your app refuses to even
   start when Redis is not running. Part 7 is about why that would be exactly backwards.
2. **Do not use a library that solves caching problems for you** — in particular, do not use
   `HybridCache`. It quietly fixes one of today's problems, and you would never find out that
   problem exists. There is a stretch goal at the end where you may try it, to compare. Not
   before.

### The number that decides what gets cached

Caching is worth it when something is **read far more often than it changes**.

Notice this is a **ratio**, not a speed. "It changes a lot" is not, by itself, a reason to say no.
The number to think in is **reads per change** — how many reads each stored copy serves before it
becomes wrong:

> Read 500 times a minute, changes 20 times a minute → each copy serves about **25 reads**. That
> is 25 database queries you did not run. Worth it.
>
> Read 30 times a minute, changes 20 times a minute → each copy serves about **1 read**. You are
> doing all the work of a cache and getting almost nothing back.

### Your prediction table

Before you cache anything, fill in this table in your write-up. For each endpoint, estimate reads
per minute, changes per minute, and the reads-per-change number. Guessing is fine — you check
these guesses against real numbers in Part 8.

| Endpoint | Reads/min (guess) | Changes/min (guess) | Reads per change |
|---|---|---|---|
| Store list | | | |
| Store details | | | |
| Product browse (store + category + sort + page) | | | |
| Product text search | | | |
| Store dashboard | | | |
| Store sales tally | | | |
| My orders (per customer) | | | |

Then answer three questions:

1. Which endpoint has the best ratio?
2. Which would be a **bad** thing to cache, and why?
3. The store sales tally changes on every single order. But look at what it already is — a table
   that your analytics consumer keeps up to date, read back with one tiny query. Would putting a
   cache in front of it buy you anything? Say why or why not.

---

## Part 1 — Your first cache, and the one helper they all share

### Start with store details

Cache your Day 1 **store details** endpoint. A store's name and address change maybe once a
month, and the page is read every time somebody opens the store. Thousands of reads per change.
This is as easy as the decision ever gets.

The shape is called **cache-aside**, and every cache today uses it:

1. Build the key.
2. Ask Redis for it. If it is there, return it. That is a **hit**.
3. If it is not there, run the real query, store the answer in Redis with a time limit, and
   return it. That is a **miss**.

The details for this one:

- **Key:** `store:{storeId}:details` — so store 42 is `store:42:details`.
- **Value:** the response DTO you already return, as JSON. **Never cache the EF entity.** An
  entity drags its navigation properties and change-tracker baggage with it, and it welds your
  cache to your database shape. You built DTOs for exactly this kind of boundary.
- **TTL:** 5 minutes.

### Build ONE helper before you cache a second thing

You will cache four different things today. Do not write the get-check-build-store steps four
times. Build one small `CacheService` that does cache-aside in one place, and route every cache
today through it.

Two rules live inside that helper, permanently:

1. **Every entry gets a TTL. No exceptions.** The helper simply does not offer a way to store
   something forever. Part 4 shows you why this saves you.
2. **Every read counts a hit or a miss, per cache name.** Two counters per name, visible in your
   logs or an Admin endpoint. Part 8 runs on these numbers — `INFO stats` cannot give them to
   you, because it counts the whole server as one lump.

(Check this against your own helper rule: it has four callers coming, and it guards an invariant.
It earns its place.)

### Prove it

1. Call store details twice. Your SQL log shows one query for the first call and none for the
   second.
2. In `redis-cli`: find the key with `KEYS *`, read it with `GET`, check its `TTL`.
3. Record the response time of a miss and of a hit.
4. Your counters show one miss, one hit.

---

## Part 2 — The key: every input, and only inputs that repeat

### Cache the browse list

Now cache what a customer sees when they open a store: that store's products, optionally narrowed
to one category, sorted, one page at a time.

**A cache key must contain every input that changes the answer.** For browse, that is: store id,
category id (or "all"), sort field, sort direction, page number, page size. Change any one and
different products come back. So every one goes in the key:

```
store:42:products:cat:7:sort:price:asc:page:1:size:20
```

**TTL: 60 seconds.**

### First, clamp the page size

Look at that last input. Page size comes from the client, and right now the client can send any
number it likes: 20, 21, 22, 37, 1000. Every different number is a different cache key. A free
number chosen by the caller means a key space with no limit — and it hands anyone on the internet
a way to fill your Redis with junk keys.

So clamp it: **allow only 10, 20 and 50. Anything else becomes 20.** Do this before caching, in
the endpoint itself. Now that input has three possible values instead of infinity.

### Break it on purpose: the missing input

Leave the **page number** out of your key. Then:

1. Ask for page 1 of a store's products. You get page 1.
2. Ask for page 2. **You get page 1 again.**

The cache did not know the two requests were different, because you did not tell it. Nothing
errors. The customer just quietly sees the wrong page until the TTL expires.

Put the page number back. Write the rule in your own words, and list every value that ended up in
your browse key.

### Now the judgement call: refuse to cache text search

Your search endpoint also accepts a **text term**, a **minimum price** and a **maximum price**.

**Do not cache any request that uses them.** In code: if the request has a text term or a price
filter, skip the cache completely and go straight to the database.

Here is the reasoning, and it matters more than the code. Look at where each input's values come
from:

- Store, category, sort, page, size — each one is picked from a **small fixed menu**. There is a
  limited set of possible keys, and real customers hit the same ones all day, because most people
  open a store and look at page 1 of everything.
- A text term is **typed by a human**. `phone`, `phones`, `Phone`, `iphone case`, `iphone  case`
  with two spaces — every one is a different key. Typed keys almost never repeat. Each cached
  answer would be written once, read once, and never looked at again.

A cache full of answers nobody asks for twice is not a cache. It is a memory leak with a TTL.

**The rule: a cache only pays when the same key comes back again and again.** Menu inputs repeat.
Typed inputs do not.

In your write-up: which inputs you refused to cache, and the rule in your own words.

---

## Part 3 — The value: the stock problem

Stop before you cache the whole product row, and look at what is inside it.

A product has a **name**, a **price**, a **category** — and a **stock quantity**.

Name, price and category change once in a while. **Stock changes on every single order.** If
stock is part of your cached value, then every order placed at a busy store makes that store's
cached pages wrong. Your reads-per-change number from Part 0 collapses, and you built a cache
that dies every few seconds.

Decide what to do about it. Two honest answers:

1. **Leave stock out of the cached value.** Cache the slow-moving part — id, name, price,
   category — and fetch stock separately, only where the screen truly needs it.
2. **Cache a coarser fact instead of the number.** A browsing customer does not need "7 left".
   They need "available" or "sold out". That flips only when stock crosses zero (or comes back),
   which is far rarer than the number changing.

Pick one and defend it. Then write down a prediction: roughly how often does your cached browse
value become wrong now, compared with how often it would have with the exact stock number inside
it? You check this in Part 8.

**The general rule, worth more than today's example:** when one thing mixes fast-moving and
slow-moving data, do not cache it as one lump. Split it, and cache the slow part.

---

## Part 4 — Invalidation: three lies, and the thing you never invalidate

Your cache serves answers that were true a moment ago. Today you find out how that goes wrong,
and what each fix costs.

### Lie 1: the data changed and the cache did not

**Do this, exactly:**

1. Call store details for store 42. It is now cached for 5 minutes.
2. As the Partner, change that store's name.
3. Call the endpoint again straight away.

You get the **old name**, and you will keep getting it for up to 5 minutes. Nothing anywhere says
anything is wrong. Write down the longest time a customer could see the wrong name.

**The fix: delete the key when the data changes.** When a store is updated, delete
`store:{id}:details`. The next read misses, rebuilds, and is correct.

Do it, and prove it: change the name, read again, get the new name immediately.

### Lie 2: the keys you cannot list

Now a Partner changes a **product's price**.

Deleting that product's own cached entry is easy. But the product also sits inside your cached
**browse pages** — possibly dozens of them: different categories, different sorts, different
pages. You do not know which ones, and you cannot go and look at each key.

Three honest answers exist. Pick one, build it, defend it:

1. **Short TTL, accept the staleness.** Browse is 60 seconds stale at worst. Simple, and
   sometimes correct enough. The cost: a customer can see an old price for up to a minute.
2. **Versioned keys.** Keep a version number per store in Redis, at its own key:
   `store:42:products:ver` holding `7`. Every browse read first GETs the version, then builds the
   data key with it: `store:42:products:v7:cat:7:...`. When any product in store 42 changes,
   `INCR` the version to 8. Every old key becomes unreachable instantly and dies quietly when its
   TTL runs out. Nothing is searched, nothing is deleted.
   The costs, honestly: **one extra Redis call on every read**, and it is **blunt** — one price
   change throws away every cached page for that store, every category, every sort, every page.
   (If the version key is ever missing, treat that as version 1 and set it. Missing must not be
   an error.)
3. **Find and delete by pattern.** Use `SCAN` to walk the keys matching `store:42:products:*` and
   delete them. Honest, but it is work on every write, and it gets slower as the cache grows.

### Lie 3: the write worked and the invalidation did not

Look closely at what your store-update now does:

1. Save the change to SQL.
2. Delete the key from Redis.

**Two systems. No shared transaction.** You have seen this exact shape before — it is Day 6's
dual write, wearing a different hat.

**Do this, exactly:**

1. Put `Environment.Exit(1)` between the SQL save and the Redis delete.
2. Change a store's name. The app dies.
3. Restart it and read the endpoint. You get the **old name**, served from a cache that nobody
   cleared.

Two fixes, and the strong answer is both:

- **The TTL you already cannot skip.** Your helper puts a TTL on everything, so this wrong answer
  repairs itself within 5 minutes no matter what. This is why the helper refuses to store things
  forever: every invalidation bug you ever write has a built-in end.
- **Invalidate from the event, not from the request.** You already publish events through the
  outbox, and the outbox guarantees the event goes out even across a crash. A consumer that
  deletes the cache key (or bumps the version) is therefore reliable in a way an inline delete
  never is. And notice: if that event is redelivered and the consumer deletes the key twice,
  nothing bad happens. Deleting a key is **naturally idempotent** — your Day 5 vocabulary, paying
  off again.

### And the thing you never invalidate: the dashboard

Now cache the **store dashboard** — today's orders, revenue, top products. You measured on Day 9
how expensive that query is. It is read all day. And it changes **on every order**.

Delete-on-write is useless here: you would delete the key every few seconds and cache nothing.
Versioned keys, same story. So do neither:

- **Key:** `store:{id}:dashboard`. **TTL: 30 seconds.** No invalidation at all.

A Partner looking at today's revenue does not need it correct to the second. Thirty seconds of
staleness on a dashboard is invisible — and it turns your most expensive query into one run per
store per half-minute, no matter how many people are watching.

So you now have **two escape routes** for "it changes constantly", and you have used both today:

1. **Split the value** and cache the slow part (stock, Part 3).
2. **Accept bounded staleness** with a short TTL and no invalidation (dashboard).

Write both down. And say in your write-up what the longest lie your dashboard can now tell is.

---

## Part 5 — The cache that hands one customer another customer's data

This one is a security bug, and it is the most dangerous thing in today's task.

**Do this, exactly:**

1. Cache your "my orders" endpoint — the customer's own order history — under a key that has the
   page number in it but **not the customer id**: `orders:page:1`.
2. Log in as customer A. Call it. A's orders are now in the cache.
3. Log in as customer B. Call it.

**B is looking at A's orders.** Your authorization ran perfectly: it checked B's token, it agreed
B may read their own orders — and then it served A's data out of the cache. The breach you closed
on Day 2 and again on Day 7 just arrived through a third door.

**The fix:** the caller's identity goes in the key: `customer:{customerId}:orders:page:1`.

Then two more things, because the fix alone is not the lesson:

1. **Write the rule so it can never happen again:** if the answer depends on who is asking, then
   *who is asking* is an input, and every input goes in the key. (And notice the opposite case:
   the dashboard needed **no** caller in its key, because store 42's dashboard is the same answer
   for every allowed viewer. The ownership check still runs — see the warning below.)
2. **Now judge it with Part 0's number.** A per-customer cache is only reused by that one
   customer. How many times does one customer read their own order list in 60 seconds? If the
   honest answer is "about once", this cache serves nothing. **Removing it is a correct result.**
   Decide, and write your decision down.

**One warning that outlives today: a cache hit must never skip authorization.** The ownership
check runs first, every time, hit or miss. The cache stores answers. It does not store
permission.

---

## Part 6 — The stampede

A cache protects your database — right up until the moment it matters most.

**Do this, exactly:**

1. Use the **dashboard** cache — your most expensive query. Warm it with one call.
2. Delete its key by hand in `redis-cli`, so the next read misses.
3. With your Day 3 harness, fire **50 requests at the same instant**.
4. Count the dashboard queries in your SQL log.

You will count **about 50**. Every request missed, and every request went and ran your heaviest
query at the same moment. This is called a **stampede**. Notice *when* it happens: the instant a
popular key expires — which on a busy system is also the instant of heaviest traffic. The cache
stops protecting the database exactly when the database most needs it.

**The fix: only one request rebuilds; the rest wait for it.** The first request to miss takes a
lock, runs the query, fills the cache, releases. The other 49 wait a moment and then read the
freshly filled cache.

Build it into your helper, re-run the 50-request test, and show **one** query in the log.

### Then the catch

If your lock is a C# `lock` or `SemaphoreSlim`, start a **second copy** of your app and run the
test against both. You get **two** queries — one per copy. An in-process lock means nothing to
the other process. This is Day 3's lesson arriving in new clothes.

To make it one query total, the lock must live somewhere every copy can see: **Redis itself**.
Research how a Redis lock works (start from the `SET` command's `NX` and `EX` options), build it,
and prove the two-copy test now shows exactly one query.

If you built the Redis lock in Day 3's stretch goal, reuse it. The interesting part today is not
the lock — it is what the lock is protecting, and that you can now **measure** the difference.

---

## Part 7 — The cache must never be required

Right now, answer honestly: is your app still working if Redis is not?

**Do this, exactly:**

1. `docker stop redis`
2. Call every cached endpoint.

If anything returns an error, you have made your system **worse**. You added a component whose
only job is speed, and gave it the power to take the site down. That is a terrible trade, and it
is a very common mistake.

A cache is an optimisation, never a dependency. When it is missing, the app quietly goes to the
database — slower, and completely correct.

Because every cache goes through your one helper, this is fixed in **one place**: the helper
catches the failure, logs it, and falls through to the real query. (And this is what
`abortConnect=false` in Part 0 was for — without it the app will not even start without Redis.)

Prove all three:

1. Redis stopped → every endpoint returns correct data.
2. `docker start redis` → caching resumes by itself, no app restart.
3. Your logs said something while Redis was gone. **Silence is a failure** — nobody would learn
   the cache was dead until the database fell over under the full load.

---

## Part 8 — Worth it? The numbers, the memory, and the wipe

### Check your predictions

Run a realistic mix of traffic — your `requests.http` story, repeated, **including placing
orders**, because orders are what invalidate things. Then read the per-name hit and miss counters
from your helper.

(`INFO stats` shows hits and misses for the whole server as one lump — use it as a cross-check,
but it cannot tell you which cache is earning its keep. That is why your helper counts per name.)

Now go back to Part 0's prediction table and Part 3's stock prediction, and compare against the
real numbers. Answer in the write-up:

- The hit rate of each cached thing.
- Which prediction was furthest off, and what you had not thought about.
- **Anything not worth keeping?** A cache with a low hit rate is pure cost — memory, an extra
  moving part, and a place for wrong answers to live. If you find one, delete it and say so.
  Removing a cache you added this morning is a good result, not a failure.

### Memory, and what happens when it fills

Redis lives in RAM, and RAM runs out. Set a limit and a policy:

```
CONFIG SET maxmemory 100mb
CONFIG SET maxmemory-policy allkeys-lru
```

`allkeys-lru` means: when full, throw away the key that has gone unused longest.

Answer three questions:

1. The default policy is `noeviction`. What does Redis do to **writes** when it is full under
   that policy — and what would that do to your app if the helper did not catch failures?
2. Why is `allkeys-lru` the right policy for a pure cache like yours?
3. Redis just evicted a key you were counting on. Does your app still work? *(If Part 7 is done
   properly, you already know.)*

### The wipe — proving the rule from the top of this document

With the app running and traffic flowing:

```
FLUSHALL
```

Then use your app. Place an order. Log in with an OTP. Refresh a token. Open a store. Browse its
products. Check the dashboard.

**Everything must work.** Slower for a moment while the cache refills — and completely correct.

If anything broke, you cached something that belonged in the database. Find it, move it to SQL,
and write down what it was and why you were wrong about it.

This is why OTP codes and refresh tokens went into SQL on Day 2. Losing a cached store name costs
one slow query. Losing a live OTP locks a real person out.

---

## Part 9 — Write-up

`docs/day-11-caching.md`:

- Your prediction table from Part 0, and your answer about the sales tally.
- The helper: its two built-in rules, and why they live in one place instead of at every call
  site.
- Every cache key you built, and every input inside each one.
- The page-size clamp, and what an unclamped client-chosen number does to a key space.
- The missing-page-number bug: what the customer saw, and your rule for keys.
- Why text search is not cached: menu inputs versus typed inputs, in your own words.
- What you left out of the cached product value (or coarsened), and your prediction for how often
  the value goes wrong now.
- Lie 1: how long a customer could see the old store name, and the fix.
- Lie 2: which of the three answers you picked, and its honest cost.
- Lie 3: where you have seen save-then-delete before, and both halves of the fix.
- The dashboard: why it is TTL-only, and the longest lie it can now tell.
- The two escape routes for "it changes constantly", each in one line.
- The leak: what customer B saw, the key rule, your keep-or-remove decision on the my-orders
  cache — and the warning about authorization and cache hits.
- Stampede numbers: queries with no protection, with the in-process fix, with two copies, and
  with the Redis lock.
- Redis stopped: what still worked, what got logged, and how recovery happened.
- Hit rates per cache, your furthest-off prediction, and anything you deleted.
- Your eviction policy and the `noeviction` answer.
- The FLUSHALL run: what you did afterwards, and what survived.
- **What caching cost you.** At minimum: answers can now be wrong for bounded time, there is one
  more system to run, and every write path now has to think about invalidation.
- The hardest thing today, and how you worked it out.

---

## Definition of Done

- [ ] Redis in Docker; `abortConnect=false`; the six commands used by hand.
- [ ] One `CacheService` helper that all caches go through, which forces a TTL on every entry and
      counts hits and misses per cache name.
- [ ] Store details cached (`store:{id}:details`, DTO as JSON, 5-minute TTL); hit and miss proven
      in the SQL log and timed.
- [ ] Browse cached with every input in the key; page size clamped to 10/20/50.
- [ ] The missing-page-number bug reproduced, then fixed.
- [ ] Text search deliberately not cached, with the menu-versus-typed reasoning written down.
- [ ] A defended decision on stock in the cached value — left out, or coarsened to
      available/sold-out.
- [ ] Lie 1 reproduced and fixed with delete-on-write.
- [ ] Lie 2 solved by one of the three approaches, with its cost stated.
- [ ] Lie 3 reproduced with `Environment.Exit(1)`; the TTL backstop explained; invalidation moved
      to (or planned through) the outbox event, with the naturally-idempotent point made.
- [ ] Dashboard cached TTL-only at 30 seconds, with the staleness bound written down.
- [ ] The cross-customer leak reproduced and fixed; a keep-or-remove decision made on that cache;
      the "a hit never skips authorization" warning written down.
- [ ] Stampede: ~50 queries shown, then 1 with the in-process fix, then 2 with two app copies,
      then 1 with the Redis lock.
- [ ] Redis stopped: all endpoints correct, outage logged, recovery automatic on restart.
- [ ] Per-name hit rates measured with orders flowing; predictions compared; anything not earning
      its keep deleted.
- [ ] `maxmemory` and `allkeys-lru` set; the `noeviction` question answered.
- [ ] `FLUSHALL` run against the live app; nothing broke.
- [ ] `docs/day-11-caching.md` complete. Branch merged; clean clone still runs (README: how to
      start Redis).

---

## Stretch goals (only if you still have fuel)

1. **Now try `HybridCache`.** Rebuild one cache with it and compare. Which of today's problems
   does it handle for you? Which are still entirely yours? You have now hit each one by hand, so
   you can finally judge what the library is worth.
2. **Two levels.** Add a small in-memory cache in front of Redis, so the hottest keys skip the
   network completely. Measure the difference. Then face the new problem you just created: every
   copy of your app now has its own private copy, and deleting the Redis key does not touch them.
   How do you tell every copy to drop an entry at once? *(You already own a tool that can
   broadcast a message to every copy of your app.)*
3. **Warm the cache.** Instead of the first customer after expiry paying for the rebuild, have a
   background job refresh the dashboard cache just before its TTL runs out. What is the risk if
   the timing is wrong, and what did you just trade for that speed?
4. **Cache something that is not a query.** Find an expensive computation in your system and
   cache its result. Does cache-aside change shape at all when the source is not a database?

Making something fast is easy. Keeping it correct while it is fast is the job.
