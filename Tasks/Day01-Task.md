# Day 1 — Foundation: Turn Your Entities Into a Real Platform

## The mission (read this first)

Over the next 6 weeks you are building **one project**: a multi-store delivery platform
backend — think of it as a mini version of a real ordering system. Customers browse
stores, stores sell products, orders get placed, prepared, delivered.

Every advanced topic from now on gets built **into this same repository**. By the end,
this repo is your portfolio: something you can put on GitHub and walk any interviewer
through, feature by feature.

You already built the basic entities (Customer, Store, Order, OrderLine, Product,
ProductOption...). Today you turn that skeleton into something that looks like a
system a real team could work on.

Pick a name for your platform. Name the repo after it. Own it.

**Today has 7 parts. Do them in order. Each part builds on the one before it.**

---

## Ground rules (these apply every day from now on)

1. **Git discipline.** Work on a branch per day (`day-01-foundation`). Small commits,
   clear messages. Merge to `main` only when the Definition of Done is met.
2. **Clean clone rule.** Anyone must be able to clone the repo, follow the README,
   and run it. If setup steps live only in your head, the day is not done.
3. **You choose your libraries — and you defend every choice.** "The tutorial used it"
   is not a defense. Be ready to answer "why this and not the alternative?"
4. **No entities leave the API.** Every endpoint takes and returns DTOs. One DTO per file.
5. **Stuck more than 45 minutes on the same error?** Write down the 3 things you tried
   and what each told you. Then keep digging. This log is part of the job.

---

## Part 0 — Solution shape

Restructure into three projects:

- `YourApp.Api` — controllers, middleware, startup. Controllers stay thin:
  no business logic, no DbContext access. They receive a request, call a service,
  return a response.
- `YourApp.Service` — all business logic. One service per area (OrderService,
  ProductService...), registered through interfaces in DI.
- `YourApp.Data` — entities, DbContext, entity configurations, migrations.

Wire everything with dependency injection. If you can't explain why the layers
point in the direction they do, stop and figure it out.

---

## Part 1 — Harden the domain

Your current entities are a start. A real platform needs more. Extend your model to
cover all of this:

### Base entity
Every entity inherits from a `BaseEntity` with:
- `Id`
- `CreatedAtUtc`, `UpdatedAtUtc`
- Soft delete: `IsDeleted` (+ `DeletedAtUtc`). Nothing is ever really deleted.
  Deleted rows must disappear from ALL normal queries automatically (see Part 2).

### New entities you must add (skip any you already have — but make them match the rules)
- **Address** — a customer has many addresses; exactly one can be the default.
- **Category** — products belong to a category.
- **OrderStatusHistory** — every status change of an order is recorded forever:
  old status, new status, timestamp. Nobody should ever ask "when did this order
  get accepted?" and get no answer.
- **AuditTrail** — see Part 2. The table that remembers who changed what.

### Product rules
A `Product` belongs to exactly one `Store` (required FK — a product cannot exist
without its store) and carries its own `Price` and `StockQuantity`. Different
stores can sell similar products at different prices. All ordering happens against
a store's own products.

### Order rules
- `Order` carries a unique, digits-only `OrderCode` — 12 digits built from
  three parts:
  1. First 4 digits: year + month (`2607` = July 2026).
  2. Last 3 digits: a sequential counter — each order gets the next value
     (`...400`, `...401`, `...402`; after `999` it wraps to `000`). Where "the
     next value" comes from is your design problem.
  3. The 5 digits in between: random.
  Example: `2607` + `84913` + `402` → `260784913402`.
  Business reason for the random middle: a fully sequential code leaks
  information — a competitor could place one order today, one next week, and
  read our order volume from the difference. The random digits hide that.
  Uniqueness must be guaranteed by the database, not by hope.
- `Order` also carries: `Status`, `Subtotal`, `DeliveryFee`, `Total`,
  `PaymentMethod` (Cash / Card — enum).
- `OrderLine` stores a **snapshot**: copy `ProductName` and `UnitPrice` into the line
  at purchase time, in addition to the foreign keys. Think hard about why. You will
  be asked.

### Order status flow
`Pending → Accepted → Preparing → OutForDelivery → Delivered`, and `Cancelled` is
allowed only from `Pending` or `Accepted`. No other jumps exist. This rule will be
enforced in code in Part 4.

### Non-negotiable details
- All money is `decimal` with explicit precision configured. Never float/double.
- All timestamps are UTC.
- One Fluent API configuration class per entity, in `Data/Configurations/`.
  Nothing configured by convention that matters: string max lengths, required fields,
  decimal precision, relationships, delete behaviors — all explicit.
- **No cascade deletes.** Decide the delete behavior of every relationship on purpose
  and be ready to justify each one.
- Unique constraints where reality demands them. At minimum: `Order.OrderCode`,
  `Customer.Email`, and a product name unique inside its own store (StoreId + Name).

Create the migration(s) yourself with clear names. Then open the generated migration
and **read the SQL it produces** (`dotnet ef migrations script`). You should be able
to name every index in your database and say why it exists.

---

## Part 2 — EF Core plumbing real systems have

1. **Global query filter for soft delete.** Every query in the whole app must skip
   `IsDeleted` rows automatically — no `.Where(x => !x.IsDeleted)` scattered around.
   Bonus points: apply the filter to all entities in one place instead of repeating
   it per entity.
2. **SaveChanges interceptor for auditing.** `CreatedAtUtc` and `UpdatedAtUtc` are set
   automatically for every insert/update. No service should ever set them by hand.
   Same for `IsDeleted`/`DeletedAtUtc`: deleting through EF (`Remove`) should be
   converted into a soft delete automatically. That's right — make `Remove` not
   actually delete. Research how.
3. **Audit trail.** Create an `AuditTrail` entity: `EntityName`, `EntityId`,
   `Action` (Insert / Update / SoftDelete), a JSON string holding the changed
   properties with their old and new values, `TimestampUtc`, and a nullable
   `UserId` (stays empty for now — authentication arrives later and will fill it).
   Capture it inside the same SaveChanges pipeline: EF's **ChangeTracker** already
   knows every entity you touched and every property that changed. Your job is to
   read that knowledge and store it. Requirements:
   - The audit rows must be saved in the **same transaction** as the change itself.
     A change that saved while its audit row didn't is a lie in your log.
   - Auditing must be switchable per operation. The seeder must run with it OFF —
     think about why, and what would happen if you left it on.

---

## Part 3 — The seed engine (this one matters more than it looks)

A system with 100 rows hides every mistake you make. A system with a million rows
hides nothing. From today, you develop against **big, realistic data**.

Build a seeding mechanism (CLI flag like `--reseed`, or a separate console project)
that wipes and refills the database with generated data. Use a data generation
library (look up **Bogus**) — hand-written loops with "Product 1", "Product 2" don't
count as realistic.

### Minimum row targets
| Table | Rows |
|---|---|
| Stores | 50 |
| Categories | 30 |
| Products | ~40,000 (each store owns hundreds of its own products) |
| Customers | 20,000 |
| Addresses | ~35,000 |
| Orders | 300,000 |
| OrderLines | ~900,000 (1–8 lines per order) |
| OrderStatusHistory | one row per status an order passed through |

### The data must look real
- Order dates spread over the last 12 months, not all today.
- Some customers order weekly, most order rarely (not a flat distribution).
- Orders are in realistic statuses: most Delivered, some Cancelled, a few in progress.
- Delivered orders have a full status history chain, not just one row.
- Some products have `StockQuantity = 0` (out of stock happens).
- Generated prices must look believable for the kind of product: seed
  electronics in the hundreds, snacks in the singles. No flat random 1–1000
  range for everything — nobody pays 850 for a cola.

### The performance requirement
A full reseed must finish in **under ~3 minutes** on your machine, and it must log
the duration and row count per table.

Fair warning: the first obvious way you write this will be too slow. Making inserts
fast **is** today's lesson. Research why it's slow and what your options are.
Whatever technique you land on — defend it.

---

## Part 4 — The API

### 4.0 First: choose and defend your API standard (research task)

Before writing a single endpoint, research how professional APIs are designed.
Compare the two main styles used in the real world:

- **Action style (RPC):** `POST /v1/Product/Add`, `GET /v1/Product/GetProducts`
- **Resource style (REST):** `POST /v1/products`, `GET /v1/products?storeId=5`

Read about REST resource naming, what each HTTP method means (GET / POST / PUT /
PATCH / DELETE), which status code means what (200, 201, 204, 400, 404, 409),
where filters and paging parameters belong, and API versioning (why so many
URLs start with `/v1/` — and what breaks for old mobile apps without it). Then:

1. Write `docs/api-guidelines.md` — YOUR convention for this project. How URLs
   are named, which method is used when, which status code means what, what the
   error body looks like, how paging parameters are named. One page, clear enough
   that another developer could add a new endpoint without asking you anything.
2. Apply it to **every** operation below, consistently. Consistency is the real
   test — a beautiful convention applied 80% of the time is worse than a plain
   one applied 100% of the time.
3. Be ready to defend your choice against the other style. You will be asked.

The operations below are named by what they DO. The URLs, methods, and status
codes are YOUR design, following YOUR written guideline.

### The operations

DTOs everywhere. Paged lists return a shared envelope:
`{ items, totalCount, page, pageSize }`.

**Catalog**
1. **List stores** — paged, filter by name text.
2. **Store details** — store info + how many products it sells.
3. **Search products** — the serious one. Filters: store, category, text,
   min/max price, in-stock only. All filters optional and combinable. Sorting at
   least by price and by name, both directions. Paged.
   **Hard requirement:** the filtering, sorting, and paging must all happen inside
   SQL Server — the database returns only the rows for the requested page.
   You must **prove** it: capture the SQL your query produces (see Part 5, SQL
   logging) and paste it into your write-up with two sentences explaining where it
   executes and how you know.

**Customers**
4. **Create customer** and **update customer** — validated: proper email, proper
   phone format, no duplicate email.
5. **Add address** and **list addresses** — enforce the "exactly one default
   address" rule no matter what order calls happen in.

**Orders**
6. **Place order** — the centerpiece. Input: customer, store, address, payment
   method, and lines `[{ productId, quantity }]`. Rules:
   - every product must belong to that store, be in stock, not deleted;
   - **prices come from the database, never from the client** — compute Subtotal,
     DeliveryFee (flat is fine), Total on the server;
   - snapshot name and unit price into each line;
   - decrement `StockQuantity` for each line;
   - generate the OrderCode; status `Pending`; write the first history row.
   - If ANY rule fails, nothing is saved — no half-orders in the database. Prove
     you know how you guaranteed that.
7. **Order details** — the full picture in one response: order info, customer
   name, store name, delivery address, every line with product name/qty/prices,
   and the complete status history. Design the response DTO like a frontend would
   actually want it.
8. **Customer order history** — paged, newest first, each row with order code,
   date, status, store name, line count, total.
9. **Update order status** — enforce the exact transition rules from Part 1.
   Illegal transition → clear error saying what was attempted and what was
   allowed. Every change writes a history row. Cancelling an order **returns its
   stock**. Implement the transition rules as **one data structure** (e.g. a
   dictionary of allowed transitions), not a pile of if/else.

**Reporting (flex your LINQ)**
10. **Store dashboard** — one response containing:
    - today's order count and today's revenue (exclude Cancelled);
    - top 5 products of the last 30 days by quantity sold;
    - average order value over the last 30 days;
    - count of orders currently in each active status.
    Use as few queries as you can justify. On your 300k-order database this
    must respond in **under 1 second**. If it doesn't, the database is telling you
    something is missing — figure out what, fix it, and document before/after timings.

---

## Part 5 — Cross-cutting (make it feel professional)

1. **Validation** — every write endpoint validates input before any logic runs
   (FluentValidation or manual — defend your pick). Bad input → 400 with a body that
   tells the client exactly which fields are wrong. One consistent error shape everywhere.
2. **Global exception middleware** — no endpoint ever leaks a stack trace. Unhandled
   exception → logged with full detail, client gets a clean 500 with a correlation id.
   Business rule violations (insufficient stock, illegal status change) are NOT 500s —
   pick the right status codes and keep them consistent.
3. **Structured logging with Serilog** — console + rolling file. A request-logging
   middleware records method, path, status code, and duration in ms for every request.
   Turn on EF Core SQL logging in Development so you can always see what SQL your
   LINQ produces. You'll need it today (Part 4.3 and 4.10).
4. **Swagger** — every endpoint documented and callable from it.
5. **`requests.http` file** in the repo — a scripted full story someone can execute
   top to bottom: create customer → add address → search products → place order →
   walk the statuses to Delivered → view details → view the dashboard. This file is
   proof the system works end to end.

---

## Part 6 — Write-up (not optional)

Create `docs/day-01.md` in the repo:

- A schema diagram of your final model (a Mermaid diagram in markdown is fine).
- Every non-obvious decision + why, one or two lines each (delete behaviors,
  validation library, error model, seeding technique...).
- The seed timing table (per-table row counts and durations).
- The captured SQL of your product search + your two-sentence proof.
- The dashboard endpoint's before/after timings if you had to fix it.
- The hardest problem of the day, and how you found the answer.
- One thing you would redo differently.

Keep it under a 5-minute read. Writing clearly about your own system is a skill
companies pay for.

---

## Definition of Done

- [ ] Three-project solution, DI wired, thin controllers.
- [ ] All entities from Part 1 exist, fully configured with Fluent API, migrations clean.
- [ ] Soft delete works globally: soft-deleted rows vanish from every endpoint without per-query filters.
- [ ] Audit fields set automatically by interceptor — zero manual assignments.
- [ ] AuditTrail rows written for insert/update/soft-delete with old+new values,
      in the same transaction — and OFF during seeding.
- [ ] Full reseed under ~3 minutes, timings logged, data passes the "looks real" rules.
- [ ] `docs/api-guidelines.md` written; all Part 4 operations built, consistent
      with it, and demonstrated in `requests.http`.
- [ ] Product search proven to filter/sort/page inside SQL.
- [ ] Placing an order is all-or-nothing; stock moves correctly on place and on cancel.
- [ ] Status transitions enforced from a single data structure; history always written.
- [ ] Dashboard under 1 second on the full seeded dataset.
- [ ] Consistent validation errors, exception middleware, Serilog with request timings.
- [ ] `docs/day-01.md` complete. Branch merged.

---

## Stretch goals (only if you still have fuel)

1. Run SQL Server in **Docker** instead of a local install; document the exact
   `docker run` command in the README.
2. `--scale` parameter for the seeder (`--scale 3` = triple all row targets) so the
   dataset size is one flag away.
3. A generic `ToPagedResultAsync<T>()` extension used by every paged endpoint.
4. A `/health` endpoint that actually checks the database connection.

Good luck. Build it like the next developer to open this repo is watching.
