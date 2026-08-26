# Day 10 — Inside the Database: How SQL Server Actually Finds Your Rows

## Where we are

Yesterday you hunted. You found slow endpoints, you added indexes, you watched a scan turn into a seek, and you fixed real problems with real numbers.

Now be honest with yourself about one thing. You used words yesterday that you do not fully
understand yet. *Seek. Scan. Logical reads. Key lookup.* You used them the way you use a tool
somebody handed you. It worked, and you could not have explained exactly why it worked.

Today you go one level down and actually understand them.

By tonight you will be able to look at any query and say why it is fast or slow, and prove it
with a number. Every fix you made yesterday will make sense for a reason, instead of because a
guide told you to do it.

### Today's theme: look at what your tools really produce

You write LINQ. Something else writes the SQL. You write `WHERE`. Something else decides how to
find the rows. You write a DTO. Something else copies the fields across.

All day today you are going to **open the lid** and read what was generated for you:

- the **execution plan** — the steps SQL Server chose,
- the **SQL** your LINQ produced,
- and at the end, the **mapping code** a tool writes for you.

A developer who can read those three things is worth a great deal more than one who cannot.

### There is no app code to write for most of today

Most of today happens in your SQL client, against your own 300,000-row database.
That is on purpose. Days 5 to 8 were four days of wiring your app to other people's systems — a
broker, sockets, Google's servers. Today there is nothing to install and nothing to connect. Just
you, one window, and your own data.

**Everything builds on your Day 1–9 repo. Branch: `day-10-database`.**

---

## Ground rules (carried, plus one)

Thin controllers, DTOs, defend every choice, clean clone, write-ups. Today's new rule:

**Never say a query is fast. Say how many pages it read.**

You will meet the number that matters in Part 0. From then on, every claim you make today comes
with that number attached.

And one instruction that runs through the whole day:

**Keep track of your indexes.** Today you will create indexes and drop them again, several times.
Each part tells you which indexes must exist before you start it, and which to drop when you
finish. Follow that exactly.

The reason is simple. If an index from an earlier part is still sitting there, SQL Server may
decide to use it, and the part you are working on will not show you what it is meant to show.
Your numbers will be real, but they will be answering a different question.

Keep a running list in your write-up: which index, which part it belongs to, and whether you
dropped it or kept it.

---

## Part 0 — Full data, and learning to see

### Your data must still be full size

You restored it yesterday, so this is a ten-second check. Confirm you still have around
**300,000 orders** and **900,000 order lines**. If anything has shrunk, put it back first.

Everything today is invisible on small data. On 200 rows every query is instant and every mistake
is hidden. On 300,000 rows the database has to make real choices, and you can watch it make them.

### The two tools, and what they are really telling you

You used both of these yesterday to get through the hunt. Today you find out what they mean.

Open your SQL client and connect to your database. On Windows that is SSMS. **If you are on a Mac
or Linux, read the box further down before you start** — SSMS does not run there, and you need a
different tool.

**1. `SET STATISTICS IO ON`.** Run this once in your query window:

```sql
SET STATISTICS IO ON;
```

Now every query you run also prints how many **logical reads** it did. A logical read is one
8KB **page** that SQL Server had to look at to answer you.

**This is the number that matters today.** Not milliseconds. Time changes depending on what
else your machine is doing and on what happens to still be in memory — run the same query twice
and the second one looks faster even though nothing changed. Logical reads do not move around.
If reads drop, the query genuinely got better.

**2. The actual execution plan.** In SSMS, press `Ctrl+M`, then run your query. A diagram appears
in a new tab. That diagram is SQL Server showing you the steps it chose.

### If you are not on Windows

**SSMS only runs on Windows.** If you are on a Mac or on Linux, you can still do every part of
today. You just need a different tool.

**Best option: VS Code with the "SQL Server (mssql)" extension.** Free, made by Microsoft, and it
runs on Mac and Linux. It gives you the same graphical plan that SSMS does: an **Estimated Plan**
button next to Run Query, and an **Enable Actual Plan** button beside it. It can also save a plan
to a `.sqlplan` file.

**Do not use Azure Data Studio.** Microsoft retired it in February 2026. Use VS Code instead.

**If you would rather stay in DBeaver**, it can show plans too — right-click your query and choose
*Explain Execution Plan*. You get a tree of rows instead of the picture. Harder to read, same
information.

**The fallback that works in every client**, DBeaver included, is to ask SQL Server for the plan
as text:

```sql
SET STATISTICS PROFILE ON;
-- your query here
```

You get one row per step of the plan. Three columns matter:

- **PhysicalOp** — this is where you find the words *Index Seek*, *Index Scan* and *Key Lookup*.
- **Rows** — how many rows really came out of that step.
- **EstimateRows** — how many SQL Server guessed before it ran. Part 6 is about these two
  disagreeing.

There is also `SET STATISTICS XML ON`, which returns the whole plan as XML. Save that into a file
ending in `.sqlplan` and you can open it in the VS Code viewer to get the diagram.

**One piece of good news.** `SET STATISTICS IO ON` — the logical reads, the number that matters
most today — comes from the **server**, not from your tool. It works exactly the same everywhere.
In DBeaver look in the **Server Output** panel. In VS Code look in the **Messages** tab.

### How to read a plan

Three rules and you can read any plan well enough for today:

1. **Read it right to left.** The right-hand boxes happen first.
2. **Thick arrows mean lots of rows.** A very thick arrow early on means SQL Server pulled a huge
   amount of data and then threw most of it away.
3. **Hover over a box.** You get a panel showing, among other things, **Estimated Number of Rows**
   and **Actual Number of Rows**. Remember those two — Part 6 is about what happens when they
   disagree.

Reading a text plan instead of the picture? The same information is all there. Read it top to
bottom, and use the **PhysicalOp**, **Rows** and **EstimateRows** columns.

The words you are hunting for all day are **Scan**, **Seek**, and **Key Lookup**. Parts 1 and 2
teach you what each one means.

---

## Part 1 — What a table really is, and what an index really is

### First, get back to a known starting point

You cannot measure anything today until you know which indexes already exist.

List them all:

```sql
SELECT  t.name AS TableName,
        i.name AS IndexName,
        i.type_desc,
        i.is_unique
FROM    sys.indexes i
JOIN    sys.tables  t ON t.object_id = i.object_id
WHERE   i.name IS NOT NULL
ORDER BY t.name, i.name;
```

Copy that list into your write-up. That is where you are starting from.

Now clean up. **Drop every index you added yesterday on Day 9.** Write down the `CREATE INDEX`
statement for each one before you drop it — you put them back at the end of today.

**Keep everything that came from Day 1**: your primary keys, the unique index on `OrderCode`, and
the unique index on product name inside a store.

Why bother? Because yesterday everybody added different indexes. Every experiment today needs a
known starting point, or SQL Server will quietly use some index you forgot about and the numbers
will not show what the part is trying to show. You are putting the database back to a clean,
shared state so the experiments actually work.

### How your rows are stored

SQL Server does not store rows one at a time. It stores them in **pages** of 8KB. A page holds
however many of your rows fit into it. To read one row, SQL Server must read the whole page it
sits on.

That is why logical reads matter: they count pages touched, and pages are the real unit of work.

Your `Orders` table with 300,000 rows is thousands of pages. If SQL Server has to look at all of
them to find one order, that is thousands of reads.

### The two kinds of index

- A **clustered index** is the table itself, kept sorted by one key. Your `Id` column almost
  certainly is this. A table has only one, because the rows can only be stored in one order.
- A **nonclustered index** is a **separate small copy** of a few columns, kept sorted, with a
  pointer back to the full row. You can have many. Your unique index on `OrderCode` from Day 1 is one of these.

An index is a sorted copy. That is genuinely all it is. Sorted means SQL Server can jump instead
of searching.

### What an index costs you

That copy is not free, because SQL Server has to keep it correct.

When you **insert** one order, SQL Server does not do one write. It writes the row into the table,
and then it writes an entry into **every nonclustered index on that table**. Five indexes means
six writes instead of one. The same happens when you **delete** a row, and when you **update** a
column that an index contains.

So the deal is always the same, and it never changes:

> **An index makes reading faster and writing slower. Always.**

You measured this yesterday without naming it, when you timed a 10,000-row insert before and
after adding your indexes.

This is why "just add an index" is not a free move, and why a table carrying fifteen indexes is
usually a mistake. Every one of them is paid for on every single write, forever — including the
ones that no query ever uses.

### See the difference for yourself

Run these three queries, each with statistics on. Use real values from your own data.

```sql
-- A: find one order by its Id      (clustered index)
SELECT * FROM Orders WHERE Id = 150000;

-- B: find one order by its code    (nonclustered unique index from Day 1)
SELECT * FROM Orders WHERE OrderCode = '260784913402';

-- C: find orders by a column with no index at all
SELECT * FROM Orders WHERE Total = 47.50;
```

Write down the logical reads for all three, and what the plan says for each.

You should see something like a handful of reads for A and B, and thousands for C. Same table,
same size, one row wanted. The only difference is whether a sorted copy existed.

In your write-up, explain in your own words why C costs so much more.

---

## Part 2 — Seek, scan, and the sneaky one in the middle

Three words explain most database performance.

- A **scan** means SQL Server read every page of the table or index, checking each row. It had no
  faster route.
- A **seek** means it jumped straight to the rows it needed, using the sorted order of an index.
- A **key lookup** is the sneaky one. The index found which rows you want, but the index does not
  contain all the columns you asked for. So for **every single row found**, SQL Server goes back
  to the full table to fetch the rest.

A key lookup looks like a success in the plan — there is a seek right next to it — but it can
cost more than the scan you were trying to avoid.

### The three-step experiment (be exact)

Use your `Orders` table and one customer who has plenty of orders.

**Step 1.** Make sure there is an index on `CustomerId`:

```sql
CREATE INDEX IX_Orders_CustomerId ON Orders (CustomerId);
```

**Step 2.** Run these two queries with the plan and statistics on:

```sql
-- Query 1: ask for only the columns that are in the index
SELECT CustomerId, Id FROM Orders WHERE CustomerId = 4242;

-- Query 2: ask for more columns
SELECT CustomerId, Id, OrderCode, Status, Total, CreatedAtUtc FROM Orders WHERE CustomerId = 4242;
```

Both find the same rows. Look at the plans. Query 1 is a clean seek. Query 2 has a seek **and a
key lookup**, and its logical reads are much higher.

Write both numbers down.

**Step 3.** Now fix it. An index can carry extra columns that it does not sort by, purely so that
lookups are not needed:

```sql
CREATE INDEX IX_Orders_CustomerId_Covering ON Orders (CustomerId)
    INCLUDE (OrderCode, Status, Total, CreatedAtUtc);
```

Run query 2 again. The key lookup is gone and the reads drop a long way.

An index that contains everything a query needs is called a **covering index**. Write down what
that means in your own words, and what it costs — because it is not free. That index now holds
five more columns, so it takes more space, and every insert has to write all five.

### Clean up before you move on

Drop **both** indexes you made in this part:

```sql
DROP INDEX IX_Orders_CustomerId ON Orders;
DROP INDEX IX_Orders_CustomerId_Covering ON Orders;
```

The first one is already pointless — the covering index does everything it did and more. The
second has to go as well, because Part 3 builds a **different** index on the same column. If this
one is still sitting there, SQL Server may keep using it, and Part 3 will prove nothing.

---

## Part 3 — Column order in an index, and why it is the thing everyone gets wrong

An index on two columns is **not** the same as two indexes. And the order of those two columns
changes everything.

### The phone book

A phone book is sorted by last name, then first name.

- Find "Haddad, Sara" → easy. Go to H, then find Sara.
- Find everyone whose last name is "Haddad" → easy. They are all together.
- Find everyone whose **first** name is Sara → you have to read the entire book. The Saras are
  scattered across every page.

An index on `(LastName, FirstName)` behaves exactly like that book. It helps when you know the
**first** column. It does nothing when you only know the second.

### Prove it on your own data

Your `Orders` table should have **no index on `CustomerId` at all** right now — you dropped both
of them at the end of Part 2. Check that before you carry on, or this part will not work.

**Step 1 — run this before you create anything, and keep the plan.**

```sql
SELECT TOP 20 * FROM Orders
WHERE CustomerId = 4242
ORDER BY CreatedAtUtc DESC;
```

With no useful index, SQL Server has to read the whole table to find customer 4242's orders, and
then put them in date order itself. Find the box named **Sort** in the plan. Write down the reads.

**Step 2 — now create the index.**

```sql
CREATE INDEX IX_Orders_Customer_Created ON Orders (CustomerId, CreatedAtUtc);
```

**Step 3 — run these three, with statistics on.**

```sql
-- 1: knows the first column
SELECT * FROM Orders WHERE CustomerId = 4242;

-- 2: knows both columns
SELECT * FROM Orders WHERE CustomerId = 4242 AND CreatedAtUtc > '2026-01-01';

-- 3: knows only the second column
SELECT * FROM Orders WHERE CreatedAtUtc > '2026-06-01';
```

Queries 1 and 2 seek. Query 3 scans, and your new index is useless to it.

Record all three sets of reads.

**Step 4 — run the Step 1 query again.**

Two things changed. The scan became a seek, and **the Sort box has disappeared completely.**
Compare the reads against what you wrote down in Step 1.

### Why the Sort disappeared

Go back to the phone book.

It is sorted by last name, then first name. So all the Haddads sit together in one block — and
**inside that block, they are already in first-name order**.

If someone asks you for the first 20 Haddads in first-name order, you do not sort anything. You
find the block and start reading. The sorting was already done, when the book was printed.

Your index works exactly the same way. `(CustomerId, CreatedAtUtc)` keeps customer 4242's orders
together in one block, and inside that block they are already in date order. SQL Server jumps to
the block and reads it backwards to get `DESC`. There is nothing left to sort.

Now imagine the index had only `CustomerId` in it. SQL Server could still jump straight to 4242's
orders — but inside that group they would be in no particular order, so it would have to sort them
before it could hand you the newest 20. **That is the Sort box, and the second column is what
removed it.**

Sorting 500 rows is not free. Sorting them on every page load, for every customer, is a lot of
work you never needed to do.

### The rule

> For a query that filters on one column and sorts by another:
> **put the filter column first, and the sort column second.**

Write that down in your own words.

Then answer one question in your write-up, without building anything. If the index had been
`(CreatedAtUtc, CustomerId)` instead — the same two columns, the other way round — what would this
query have to do? Work it out from the phone book.

### Keep this one

Leave `IX_Orders_Customer_Created` in place for the rest of the day. It is a genuinely useful
index — it serves "this customer's orders, newest first" — and it does not get in the way of
Part 4, which works on completely different columns.

---

## Part 4 — Four ways to accidentally switch your index off

You can have exactly the right index and still get a scan, because of how the query is written.
There is a word for a condition that an index can be used for: it is called **sargable**. You
will see the word in real documentation, so learn it now.

### First, create the indexes this part needs

None of the columns below has an index right now. Without one, **both** halves of every pair
would scan and you would learn nothing. So build them first:

```sql
CREATE INDEX IX_Orders_CreatedAtUtc ON Orders (CreatedAtUtc);
CREATE INDEX IX_Orders_Total        ON Orders (Total);
CREATE INDEX IX_Products_Name       ON Products (Name);
```

These three exist only for this part. You drop all three at the end of it.

Two things keep your numbers clean, and both matter:

- **Every query below selects only `Id`.** That keeps the index covering, so the single thing
  changing between the two halves of a pair is whether the index can be used at all.
- **Every filter below is narrow on purpose** — it matches only a few rows. If a query matches
  most of the table, SQL Server scans it no matter what you do, because reading everything once
  really is cheaper. So use values that exist in your data and match only a handful of rows.

### The four pairs

Run each pair with statistics on. Both queries in a pair return the same rows. One can use the
index; one cannot.

**Pair 1 — a function wrapped around your column.** Pick one date that has orders on it.

```sql
SELECT Id FROM Orders WHERE CONVERT(date, CreatedAtUtc) = '2026-03-01';
SELECT Id FROM Orders WHERE CreatedAtUtc >= '2026-03-01' AND CreatedAtUtc < '2026-03-02';
```

**Pair 2 — arithmetic on your column.** Pick a `Total` that only a few orders have.

```sql
SELECT Id FROM Orders WHERE Total + 0 = 47.50;
SELECT Id FROM Orders WHERE Total = 47.50;
```

**Pair 3 — a wildcard at the start.** Pick a few letters that begin only a small number of your
product names.

```sql
SELECT Id FROM Products WHERE Name LIKE '%phone';
SELECT Id FROM Products WHERE Name LIKE 'phone%';
```

**Pair 4 — the wrong data type.** This is the most dangerous of the four, because nothing about
the query looks wrong.

First find out what type your `OrderCode` column is:

```sql
SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Orders' AND COLUMN_NAME = 'OrderCode';
```

**If it says `varchar`**, you can see the problem straight away, because `OrderCode` already has a
unique index from Day 1:

```sql
SELECT Id FROM Orders WHERE OrderCode =  '260784913402';
SELECT Id FROM Orders WHERE OrderCode = N'260784913402';
```

That `N` makes the value an `nvarchar`. SQL Server now has to convert every row in the column
before it can compare. Look for **CONVERT_IMPLICIT** in the plan.

**If it says `nvarchar`**, both of those match and there is nothing to see on your data. Build a
tiny table, watch it happen once, and throw the table away:

```sql
CREATE TABLE dbo.SargTest (Code varchar(20) NOT NULL, Filler char(50) NOT NULL DEFAULT '');

INSERT INTO dbo.SargTest (Code)
SELECT TOP (50000) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS varchar(20))
FROM sys.all_objects a CROSS JOIN sys.all_objects b;

CREATE INDEX IX_SargTest_Code ON dbo.SargTest (Code);

SELECT Code FROM dbo.SargTest WHERE Code =  '12345';
SELECT Code FROM dbo.SargTest WHERE Code = N'12345';

DROP TABLE dbo.SargTest;
```

**Now the part that actually matters.** EF Core sends string parameters as `nvarchar` by default.
So if a column in a real database is `varchar`, every EF query filtering on that column does this
conversion and can never seek. On every call. Forever. And nothing in the C# looks wrong.

Write down whether your own schema is at risk, and how you would check this on a database you did
not build yourself.

### Why the slow one is slow

An index is a **sorted list of your column's values**. The moment you wrap that column in
something — a function, some arithmetic, a type conversion — the sorted order no longer matches what you are asking for. SQL Server cannot jump. It has to work out the answer for every single row, which means reading every single row.

The rule: **keep the column alone on one side of the comparison.** Do the work on the other side.

Record all four pairs of numbers, and write the rule in your own words.

### Clean up

Drop the three indexes you created for this part:

```sql
DROP INDEX IX_Orders_CreatedAtUtc ON Orders;
DROP INDEX IX_Orders_Total        ON Orders;
DROP INDEX IX_Products_Name       ON Products;
```

Not one of them serves a real query in your application. They existed to make a point. Leaving
them behind would slow down every insert forever in exchange for nothing — which is exactly the
lesson from Part 1.

Your database should now be back to the Day 1 indexes plus `IX_Orders_Customer_Created`.

---

## Part 5 — The database is built for sets, not loops

A very common mistake is to make the database do one row at a time, when it wanted to do all of
them at once.

### The experiment (be exact)

Pick one store that has a few hundred products. Give every one of its products a 10% price rise,
twice, in two different ways.

**Way 1 — a loop in C#:**

```csharp
var products = await db.Products.Where(p => p.StoreId == storeId).ToListAsync();
foreach (var p in products)
{
    p.Price = p.Price * 1.1m;
    await db.SaveChangesAsync();   // inside the loop, on purpose
}
```

**Way 2 — one statement:**

```csharp
await db.Products
    .Where(p => p.StoreId == storeId)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, p => p.Price * 1.1m));
```

Time both. Count the SQL statements each one produced.

Way 1 makes one round trip **per product**. Way 2 makes one, and the database updates every row
itself without sending any of them to your app.

Write down both times and both statement counts. Then answer this: at what number of products
would Way 1 become completely unusable?

There is a rule hiding in here, and it is one of the most useful in this whole course: **the
database is much better at working on a whole set of rows than your app is at working on them
one at a time.** Every loop that talks to the database is a red flag.

### Now go and check what the fast way skipped

Way 2 was much faster. Before you celebrate, look at two things:

1. Open your `AuditTrail` table. Is there a row for any of those price changes?
2. Look at `UpdatedAtUtc` on those products. Did it change?

Both are probably untouched.

Here is why. `ExecuteUpdateAsync` sends one `UPDATE` statement straight to the database. It never
loads your entities and it never calls `SaveChanges`. Your Day 1 interceptor — the one that sets
`UpdatedAtUtc` and writes the audit trail — only runs inside `SaveChanges`. So it never ran.

You just made an operation hundreds of times faster **and silently lost your audit trail for
it**. Nobody warned you, and nothing failed.

Decide what you would do about it, and write your decision down. There is more than one
reasonable answer. You might accept it for genuinely bulk operations and say so clearly in the
code. You might write the audit rows yourself, in the same transaction. You might decide this
particular operation must not be bulk at all, because being able to prove who changed a price
matters more than speed.

The one thing you may not do is use the fast version without knowing what it skipped.

---

## Part 6 — Why the same query is fast one day and slow the next

SQL Server does not know your data. Before it runs a query, it **guesses** how many rows will
come back, using stored summaries of your columns called **statistics**. It picks its plan based
on that guess.

If the guess is good, the plan is good. If the guess is far out, the plan can be badly wrong.

### Cause a bad guess on purpose (be exact)

Do not go hunting for one. Here is how to produce a badly wrong guess every single time.

First pick a customer who has plenty of orders — 200 or more. Then run these two, with the actual
plan turned on:

```sql
-- A: the value is written straight into the query
SELECT Id FROM Orders WHERE CustomerId = 4242;

-- B: the same value, this time held in a variable
DECLARE @cid bigint = 4242;
SELECT Id FROM Orders WHERE CustomerId = @cid;
```

Now compare the guess against reality for each one:

- In the picture, hover the right-hand box and read **Estimated Number of Rows** and **Actual
  Number of Rows**.
- In a text plan, they are the **EstimateRows** and **Rows** columns.

**Query A guesses almost exactly right. Query B is wildly wrong** — usually a small number like
15, no matter which customer you ask about.

Write down all four numbers: the estimate and the actual for A, and the estimate and the actual
for B.

### Why B guesses so badly

When the value is written into the query, SQL Server can look `4242` up in its stored summary of
that column before choosing a plan. It gets a good answer.

When the value sits in a variable, SQL Server builds the plan **before that variable has a value
in it**. There is nothing to look up. So it falls back to an average: total rows divided by the
number of different customers. With 300,000 orders and 20,000 customers, that average is 15 — and
it uses 15 whether the true answer is 2 or 2,000.

That is not a bug. It is SQL Server being asked to choose without being allowed to look.

Find out what `OPTION (RECOMPILE)` does to query B, and write down in one line what it fixes and
what it costs you.

### Why this matters

For 20 rows, jumping to each one is the right plan. For 200,000 rows, reading the whole table in
one pass is the right plan. Same query, opposite answers. SQL Server decides based on the guess.

So a query can be fast for a customer with 5 orders and slow for a customer with 5,000, using a
plan built for whichever one ran first. This is a well-known and genuinely hard problem, and you
are not fixing it today. Find one example, write down the two numbers, and describe what you
think happened.

---

## Part 7 — Mappers: stop writing the same code by hand

Since Day 1 you have had a rule: no entity ever leaves the API, everything is a DTO. That rule is
right, and it has cost you hundreds of lines of this:

```csharp
return new OrderDto {
    Id = order.Id,
    OrderCode = order.OrderCode,
    Status = order.Status,
    Total = order.Total,
    // ...and on, and on
};
```

It is boring, and boring code is where mistakes hide. Add one property to an entity and forget
one of these lists, and a field is silently empty in production.

### The tool

Add **Mapperly** to your project. It is a **source generator**: at compile time it writes the
mapping code for you, as normal C# you can open and read. There is no reflection, and nothing is
worked out while your app is running.

Convert exactly these **three**. They are chosen because each one teaches something different.

**1. `Address` → your address DTO.** The easy case. Every property has the same name and the same
type, so Mapperly needs almost nothing from you. Start here, because it is the one where the
generated code is short enough to read in full.

**2. `Order` → your order details DTO.** The awkward case, on purpose. The store's name lives on
`order.Store.Name`, not on the order itself. `Status` is an enum you may be showing as text. And
there is a child collection of order lines. Mapperly will force you to be explicit about the parts
it cannot work out by itself — and that is the whole point. **It refuses to guess.** A mapper that
guesses is how a field ends up quietly empty in production.

**3. Your create-customer request DTO → `Customer`.** The other direction: incoming, not outgoing.
Nothing has been read from the database here. You are shaping an object that arrived in a request
body. Hold on to this one — the last section of this part explains why this direction is where a
mapper genuinely belongs.

Then do this, because it is the whole point: **find the generated file and read it.** Your IDE
can navigate to it. Paste a few lines of it into your write-up. It is ordinary C# — the same
assignments you were writing by hand, written for you.

### Make it fail on purpose

Add a new property to one of your DTOs that does not exist on the entity. Build.

You get a **compile error**, naming the property it could not map.

That is the reason to prefer a source generator. Older mappers work it out while the app is
running, using reflection, which means an unmapped property is discovered by a user, in
production, as a silently empty field. Here your build stops.

Write down that difference.

### Now the trap, and it is the important part of this section

A mapper turns **an object you already have** into another object. Read that again.

To map an `Order` into an `OrderDto`, you must first **load the whole `Order`** — every column,
whether you need it or not. The mapper runs afterwards, in memory. It cannot reduce what you
fetched, because the fetching already happened.

**The experiment (be exact):**

Write your order-details read two ways, and capture the SQL for each.

```csharp
// Way 1 — load the entity, then map it
var order = await db.Orders.FirstAsync(o => o.Id == id);
return mapper.ToDto(order);

// Way 2 — project straight into the DTO in the query
return await db.Orders
    .Where(o => o.Id == id)
    .Select(o => new OrderDto { Id = o.Id, OrderCode = o.OrderCode, Total = o.Total })
    .FirstAsync();
```

Look at the two SQL statements. Way 1 selects **every column of the table**. Way 2 selects the
three you asked for. Compare the logical reads too.

So the rule for the rest of the program:

> **For reading data out of the database, project inside the query.
> For everything else, use the mapper.**

The mapper is still worth having. It is right for shaping objects you already hold in memory —
request bodies, messages arriving from the broker, objects moving between your layers. It is the
wrong tool for deciding what to fetch.

In your write-up, list which of your endpoints should project and which should map, and say why.

---

## Part 8 — Put your indexes back, then write up

### Restore the database

You dropped your Day 9 indexes at the start of Part 1 and saved their `CREATE INDEX` statements.
Put them back now.

Then run the index list query from Part 1 again, and compare it against the list you saved at the
start. Your database should now hold:

- everything Day 1 created,
- everything you added on Day 9,
- and `IX_Orders_Customer_Created` from Part 3.

Nothing else. If anything from Part 2 or Part 4 is still there, drop it — those were built to
prove a point, and every one of them costs you on every insert.

### Write-up

`docs/day-10-database.md`:

- Your three numbers from Part 1 (indexed by Id, by OrderCode, and no index at all), and why the
  third is so much worse.
- Seek, scan and key lookup, each in one sentence of your own.
- Your three-step numbers from Part 2, and what a covering index costs you.
- The phone book rule for column order, and your three sets of numbers from Part 3.
- All four pairs from Part 4, with the reads for each, and the one-line rule for keeping a query
  sargable.
- Your two timings from Part 5, and the number of products at which the loop stops being usable.
- The four numbers from Part 6 — estimate and actual, for the literal and for the variable — and
  why the variable version guesses so badly. Plus what `OPTION (RECOMPILE)` fixes and costs.
- Your Part 3 answer: what would this query have to do if the index were `(CreatedAtUtc,
  CustomerId)` instead, and why the Sort step disappeared when you got the order right.
- Mapperly: a few lines of the generated code, the compile error you caused on purpose, and why a
  compile error beats a runtime surprise.
- The projection experiment: both SQL statements, side by side, and your rule for when to project
  and when to map.
- **Your index ledger** — every index you created today, which part it belonged to, and whether
  you dropped it or kept it.
- **Every index that exists in your database now that you have restored it**, and the exact query
  each one serves. If you cannot name a query for one, say so — you will deal with it soon enough.
- **What an index costs on write**, in your own words. Why is a table with fifteen indexes usually
  a mistake?
- The hardest thing today, and how you worked it out.

---

## Definition of Done

- [ ] The database is back at full size: ~300,000 orders and ~900,000 lines.
- [ ] `SET STATISTICS IO ON` and actual execution plans are being used, and every claim in the
      write-up has a logical-read number attached.
- [ ] Every existing index was listed before starting, the Day 9 indexes were dropped (with their
      `CREATE` statements saved), and the Day 1 indexes were left alone.
- [ ] Part 1's three queries are run and compared, with plans and reads recorded.
- [ ] Part 2's three steps are done: a clean seek, a seek plus key lookup, and a covering index
      that removes the lookup. All three sets of numbers recorded.
- [ ] Part 3 proves that a composite index helps the first column and not the second, with three
      sets of numbers, and a `TOP 20 ... ORDER BY` query that needs no Sort step.
- [ ] All four sargability pairs from Part 4 are run, with numbers for both halves of each pair —
      including Pair 4, with a written answer on whether their own schema is at risk.
- [ ] Every index built only to prove a point (Part 2's two, Part 4's three) has been dropped
      again, and the index ledger in the write-up says so.
- [ ] The Day 9 indexes are restored, and the final index list matches what Part 8 describes.
- [ ] Part 5's loop versus single statement is timed both ways, with statement counts.
- [ ] The audit trail and `UpdatedAtUtc` are checked after the bulk update, the reason they were
      skipped is understood, and a decision about it is written down.
- [ ] The literal-versus-variable pair from Part 6 is run, with all four numbers written down
      (estimate and actual, for each), plus one line on what `OPTION (RECOMPILE)` fixes and costs.
- [ ] Mapperly is added, three mappings converted, the generated code is read and quoted, and an
      unmapped property is made into a compile error on purpose.
- [ ] The projection experiment shows both SQL statements, and the project-versus-map rule is
      written down.
- [ ] Every index in the database is listed with the query it serves.
- [ ] `docs/day-10-database.md` complete. Branch merged; clean clone still runs.

---

## Stretch goals (only if you still have fuel)

1. **Find the indexes nobody uses.** SQL Server records how often each index is read and how often
   it is updated. Research `sys.dm_db_index_usage_stats`, and write a query that lists indexes
   which are written to often and read rarely. Those cost you on every insert and give nothing
   back. Would you drop them? Note the catch: those counters reset when SQL Server restarts, so
   think about how much you can trust them.
2. **Make the same query fast for one customer and slow for another.** Take a customer with 5
   orders and one with 5,000. Run the same parameterised query for each, in both orders, and see
   whether the plan built for the first one hurts the second. This is called **parameter
   sniffing**, and it is one of the most confusing problems a developer meets in real life.
3. **Three ways to ask the same question.** Write "which customers have placed at least one
   order?" three ways: with `IN (subquery)`, with `EXISTS`, and with a `JOIN` plus `DISTINCT`.
   Compare the plans and the reads. Are they the same? Should they be?
4. **Compare Mapperly with AutoMapper.** Set up the same mapping in AutoMapper. What is different at build time, at startup, and at run time? Which one tells you sooner when you have made a mistake, and what do you give up for that?

Stop trusting your database and start reading it. It has been telling you exactly what it does
this whole time.
