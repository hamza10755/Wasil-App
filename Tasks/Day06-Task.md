# Day 6 — The Outbox: Turning Two Writes Into One

## Where we are

Right now, PlaceOrder does two things:

1. It saves the order in SQL.
2. It sends the `OrderPlaced` message to RabbitMQ.

SQL and RabbitMQ are two different systems. You cannot make both writes happen as one unit.
There is no way to do it.

So there is a gap between step 1 and step 2. If your app dies inside that gap, the order is
saved but the message is never sent. You found this gap on Day 5. I told you to write it down
and leave it alone.

Today you close it.

### The fix, in one idea

Do not send the message from PlaceOrder. Instead, **save the message in your own database**, in
the same transaction as the order.

Now PlaceOrder writes to one system only: SQL. It writes two rows, in one transaction. Both
rows are saved, or neither is. There is no gap left to crash in.

Then a small loop reads those saved messages and sends them to RabbitMQ.

The table that holds the waiting messages is called an **outbox**. The loop that sends them is
called a **relay**. These are the normal names for them, and you will meet them again in real
jobs.

### Two things to know before you start

**1. This does not give you "exactly-once" delivery.** It gives you at-least-once, the same as
Day 5. The relay can send a message and then crash before it writes down that it sent it. When
it starts again, it sends that same message a second time. Your idempotent consumers from
Day 5 are what make this safe. You need both halves.

**2. Your system gets a small delay.** The message now goes out *after* the order is saved,
not at the same moment. So there is a short time when the order exists and nothing has reacted
yet. You will measure that delay today and write down the number.

**Everything builds on your Day 1–5 repo. Branch: `day-06-outbox`.**

---

## Ground rules (carried, plus one)

Thin controllers, DTOs, defend every choice, clean clone, write-ups. One new rule today:

**No code inside a request may send to RabbitMQ.**

Here is how to check it. At the end of the day, search your whole solution for the method you
call to publish. It must have exactly one caller: the relay. If a controller or a service still
calls it, your gap is still open.

---

## Part 0 — Break it on purpose and watch an event disappear

Never fix a bug you have not seen. On Day 5 you only thought about this crash. Today you cause
it.

**Do this, exactly:**

1. Open PlaceOrder. Find the line where `SaveChanges` finishes. Find the line where you publish
   `OrderPlaced`.
2. Between those two lines, add this line: `Environment.Exit(1)`.
   That kills the app on the spot. No exception. No `finally` block. No cleanup. This is what a
   server being killed really looks like.
3. Place one order. Your app dies. The HTTP call fails.
4. Start the app again. Now check three places:
   - **The database.** The order is there. It is real. Its status is `Pending`. Its stock was
     taken.
   - **The RabbitMQ dashboard.** Nothing arrived. Both queues are empty.
   - **Your store sales tally.** It did not move. Analytics never heard about this order.
5. Now go looking for something in your system that says there is a problem.

You will not find one. No error row. No failed job. No alert. That order will never get a
confirmation, and nobody will ever know.

Write all of this in your incident log **before you build anything**.

That silence is the reason today exists.

**Did you already patch this on Day 5?** Some of you may have added a try/catch, a retry, or a
"look for missing events later" check. Remove your patch first. Then run the test above. You
need to see the real failure with your own eyes. In your write-up, say what you tried and why it
did not close the gap.

Leave `Environment.Exit(1)` in the code for now. You need it again in Part 3.

---

## Part 1 — The outbox table

### Build the table

Create an `OutboxMessage` entity and table. Generate the migration yourself.

One row is one message waiting to be sent. Each row holds:

- **An id that goes up by one on every insert.** This gives you the order the events happened
  in.
- **The message id.** This is the Guid that goes on the wire as RabbitMQ's `MessageId`.
  **Create it here, when you save the row, and store it in the row.** Do not create it later.
  Part 3 shows you exactly why this matters.
- **The event name and the version.** Use the same ones from your Day 5 message contract.
- **The payload.** This is the JSON body you would have sent, saved as text.
- **The time it happened.** Your `BaseEntity` already gives you `CreatedAtUtc`. Use that.
- **The time it was sent.** This stays empty until the relay sends it.
- **How many times sending has failed**, and **the last error message**. You need both in
  Part 6.

You must be able to look at this table and see three groups: rows still waiting, rows already
sent, and rows that gave up. How you store that is your choice. Be ready to defend it.

### Write both rows together

Change PlaceOrder. Saving the order and saving the outbox row must happen in **one
transaction**.

One `SaveChanges` call does this. One transaction you open yourself around both also does this.
Pick one. Then be ready to explain what makes it all-or-nothing. This is the same question you
answered on Day 1 about half-saved orders.

Now **delete the publish call from PlaceOrder**. PlaceOrder saves two rows and returns. It sends
nothing.

Do the same in every other place that publishes today. When this part is done, no request path
talks to RabbitMQ.

---

## Part 2 — The relay: the loop that sends the messages

The relay runs on its own. Nobody waits for it. It repeats three steps, over and over:

1. Read **50** rows that have not been sent yet, **oldest first**.
2. Send each one to RabbitMQ. Use the same exchange and routing as before. Use the message id
   saved in the row. Mark it persistent, the same as your Day 5 contract.
3. Mark each row as sent.

**You choose where the relay runs.** It can be a `BackgroundService`, or a Hangfire recurring
job. Day 4 gave you both tools and the rule for picking between them. Use that rule, and defend
your choice in the write-up.

One firm requirement will help you choose:

> **The time from the order being saved to the tally changing must be under 2 seconds.**

Measure it properly. Log the time when the order commits. Log the time when the consumer handles
it. Subtract one from the other. Put that number in your write-up.

If the tool you picked cannot reach 2 seconds, that is the tool telling you something. Say so in
the write-up. Do not quietly lower the bar.

### See what you just changed for your users

Place an order. Straight away, read the store sales tally through the endpoint you built on
Day 5.

The tally may not have moved yet.

That is not a bug. That is the price you paid. The event now goes out after the order is saved.

In your write-up, list every part of your system that now needs a moment to catch up. For each
one, write the longest delay a user could see.

---

## Part 3 — Four tests that prove it works

### Test A — the crash that used to lose an event

`Environment.Exit(1)` is still sitting after your commit. Place one order. The app dies again.

Start it again and wait. The relay finds the outbox row and sends it. Notifications fire. The
tally moves.

**Nothing was lost.** The crash that silently destroyed an event in Part 0 now costs you a few
seconds and nothing else.

Remove `Environment.Exit(1)` from PlaceOrder once you have proven this.

### Test B — the crash that cannot happen anymore

On Day 5 you also thought about the opposite crash: send the message first, then the database
save fails. That leaves a message about an order that does not exist.

Try to make that happen now. You cannot.

In your write-up, explain in one sentence why your new design makes it impossible.

### Test C — the relay dies halfway

Now crash the relay on purpose. Send the message, then throw an exception **before** you mark
the row as sent.

Start it again. The relay sees a row that is not marked sent, so it sends the same message
**again**. The same event goes out twice.

Now check the tally. It is still correct.

Why? Because your Day 5 consumer saw the same message id twice, and skipped the second one.

Write this in your own words in the write-up:

> **The outbox makes sure an event is never lost. The idempotent consumer makes sure it is never
> counted twice. One without the other is not enough.**

### Test D — move the message id and watch everything break

Now break it on purpose. This is how you learn why the id is saved in the row.

Change the relay. Instead of using the message id from the row, make it create a **new** Guid
every time it sends.

Run Test C again: send, crash before marking, restart, send again.

The tally is now wrong.

Here is why. The second send carried a different id. The consumer had never seen that id before,
so it did the work a second time. Your idempotency did nothing, because the thing it compares
kept changing.

Put the id back in the row. Then write the rule you just learned, in one line.

---

## Part 4 — The same bug is hiding in your consumer

You just fixed two writes in the publisher. Now look at your consumer with the same eyes.

Your consumer does two things:

1. It does the work. It adds to the store's tally.
2. It writes down that it has handled this message id.

If those are **two separate saves**, you have built the same bug all over again. Two writes,
with a gap between them.

**Do this, exactly:**

1. In the analytics consumer, throw an exception **after** the tally is saved, but **before**
   the processed-message row is saved. Throw on the first delivery only. Use the `Redelivered`
   flag, the same way you did on Day 5.
2. Place one order.
3. RabbitMQ sends the message again. The processed-message row was never saved, so the consumer
   thinks it has never seen this message. It adds to the tally a second time.
4. Read the tally. It is wrong — **even though you added idempotency yesterday.**

**The fix:** save the work and the processed-message row in **one transaction**. Ack the message
only after that transaction commits.

Prove it. Cause the same crash again. The tally must now be exactly right.

**Then stop copying this code around.** You now have this pattern in more than one consumer. Put
it in one place, so that a new consumer cannot forget it. This is the same reason you found one
home for your ownership checks on Day 2. A rule that every developer has to remember is a rule
that will be forgotten.

---

## Part 5 — Two copies of the app, one outbox

Everything so far assumed one copy of your app is running. Real systems run several.

**Do this, exactly:**

1. Start **two** copies of your app. Both of them run the relay.
   `dotnet run --urls http://localhost:5101`
   `dotnet run --urls http://localhost:5102`
2. Place 10 orders.
3. Watch the RabbitMQ dashboard and your logs.

Both relays read the same rows. Most events get sent **twice**.

Your idempotent consumers catch the duplicates, so the tally stays correct. That safety net just
saved you, and it is worth noticing.

But sending everything twice is still wrong. It doubles the work for the broker and for every
consumer. And it will stop being harmless the day a consumer calls something outside your system
that charges money per call.

**The fix:** one outbox row must never go to two relays at the same time. A relay has to
**claim** its rows first, so no other relay can take the same ones.

This is the Day 3 lesson again, in a new place. Checking first and acting second is not safe.
The check and the act must be one single step that cannot be split.

Do not use a C# `lock` here. You have two separate processes now. A lock inside one process
means nothing to the other one.

Fix it. Run the 10 orders again with both copies running. Show that every event was sent exactly
once.

Then notice one more thing and write it down. You do **not** have to fix this one. With two
relays each grabbing batches of rows, are your events still sent in the order they were created?
Write down plainly what order your design really promises now.

---

## Part 6 — Keep the outbox healthy

Every order writes a row into the outbox. Left alone, it becomes your biggest table and your
slowest one.

### Make the relay's query fast

1. Insert **200,000** outbox rows that are already marked as sent. Use the bulk insert you built
   on Day 1. This should take seconds, not minutes.
2. Now time the relay's query — the one that finds the oldest unsent rows. Capture the SQL it
   produces. Look at what the database really does with it.
3. Make it fast. Put a before time and an after time in your write-up.

The database will tell you what it needs, if you read the query plan.

### Delete old rows

Add a recurring cleanup job. You built one on Day 4. This one deletes sent outbox rows older
than **7 days**.

Then check that it really worked. Count the rows with SQL in SSMS — not through EF.

If the count did not drop, go and look at your Day 1 soft-delete interceptor. It turns deletes
into something else.

Then decide: should outbox rows take part in soft delete at all? Make your cleanup really free
the space. Explain what you decided and why.

### Handle a row that can never be sent

One day, one outbox row will be impossible to send. Maybe the payload is bad. Maybe there is a
bug. Sending it fails every single time.

Your relay sends oldest first. So that one row now sits in front of every other event in your
system, and everything behind it stops.

This is the poison message from Day 5, standing on the other side of the broker.

Build the answer:

- Count the failed attempts on the row.
- After **5** failed attempts, park the row in a failed state. The relay skips it and keeps
  going.
- Keep the last error on the row, so a human can see what went wrong.

Prove it. Make a row that always fails to send. Watch everything behind it stop. Then show your
fix: the other rows flow again, and the bad row sits there, parked and easy to find.

---

## Part 7 — Write-up

`docs/day-06-outbox.md`:

- A diagram of the new path: PlaceOrder → SQL (order + outbox row, one transaction) → relay →
  exchange → queues → consumers.
- The Part 0 crash. What you lost, and what in your system reported it. (Nothing did.)
- Your outbox table design. How you tell waiting, sent, and failed rows apart.
- Where the relay runs, why you picked that over the other option, and your measured delay
  number.
- Every part of your system that now needs a moment to catch up, and the longest delay for each.
- The Test D rule about the message id, in one line.
- The consumer bug from Part 4, and where the shared fix now lives.
- How your relays claim rows, and what message order you really promise with two relays running.
- The relay query, before and after, and what you changed.
- What you found with the cleanup job and soft delete, and what you decided.
- **What the outbox does NOT do for you.** Be honest and clear. Cover at least these four: it is
  not exactly-once; it adds a delay; it adds load to your database; and it does nothing at all
  for a consumer whose own work is unsafe to repeat.
- **Where else do you still write to two places with no transaction?** Search your own system and
  name them. There is at least one left.
- The hardest thing today, and how you worked it out.

---

## Definition of Done

- [ ] Part 0's lost event was reproduced and written down **before** any fix, including the fact
      that nothing in the system reported it.
- [ ] The `OutboxMessage` table exists. The message id is created and stored when the row is
      saved. Waiting, sent and failed rows can be told apart.
- [ ] The order and its outbox row are saved in one transaction, and you can explain what makes
      that all-or-nothing.
- [ ] No code in any request path sends to RabbitMQ. Searching for the publish method finds only
      the relay.
- [ ] The relay reads 50 rows at a time, oldest first, sends them, and marks them sent.
- [ ] The measured delay from commit to consumer is under 2 seconds, and the number is written
      down.
- [ ] Test A: the Part 0 crash no longer loses the event. Shown, not claimed.
- [ ] Test C: the relay crashes after sending, sends again, and the tally stays correct.
- [ ] Test D: creating the id at send time breaks the tally, and the rule is written down.
- [ ] Part 4: the consumer double-count is reproduced. Then the work and the processed-message
      row are saved in one transaction, and it is proven fixed.
- [ ] The idempotency code lives in one shared place, not copied into each consumer.
- [ ] Two copies of the app run at once and every event is sent exactly once. Row claiming is one
      atomic step and does not use a C# `lock`.
- [ ] The relay query is fast on a table of 200,000 rows, with before and after timings.
- [ ] A cleanup job deletes sent rows older than 7 days, and the row count really drops.
- [ ] A row that can never be sent is parked after 5 attempts, keeps its error, and stops
      blocking the rows behind it.
- [ ] `docs/day-06-outbox.md` is complete, including the honest list of what the outbox does not
      solve. Branch merged. Clean clone still runs.

---

## Stretch goals (only if you still have fuel)

1. **Publisher confirms in the relay.** Right now the relay marks a row as sent the moment it
   hands the message over. The broker might never have accepted it. Turn on publisher confirms,
   and mark the row sent only after the broker confirms. Then explain what this fixes that the
   outbox alone did not.
2. **An outbox health endpoint.** Admin only. Show three numbers: how many rows are waiting, how
   old the oldest waiting row is, and how many have failed. Then say which one number you would
   put an alert on, and why. (Something to think about: a growing count is not always the first
   sign of trouble.)
3. **Stop asking the database.** Your relay asks "anything new?" over and over, and most of the
   time the answer is no. Find out how a database can *tell* you instead of being asked — read
   about change data capture and the transaction log. Compare it with what you built: what gets
   better, what gets harder, and what new thing do you now have to run and look after?
4. **Close the other gap.** Part 7 asked you to find another place where you write to two
   systems with no transaction. Take the one you found and close it the same way.

Two writes you cannot make safe. One write you can. Everything today is that trade, made on
purpose, and paid for honestly.
