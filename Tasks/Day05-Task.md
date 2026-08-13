# Day 5 — Messaging with RabbitMQ: Events, Fan-out, and Surviving Redelivery

## Where we are

Day 4 gave you **jobs**. A job is a *command*: you decide something must run, you hand it to
Hangfire, you own it. One actor, one task.

Today is the opposite shape. Something happens — an order is placed — and you **announce it**.
You call nobody. Whoever cares is already listening, and reacts on their own. Next month
someone adds a new reaction to orders, and nobody touches the code that places them.

That is an **event**, and the tool for it is a **message broker**. Today you run one, publish
your first event, and then spend most of the day on the single rule that makes messaging hard:

> A message can be delivered **more than once**. Every consumer must be safe to handle the
> same message twice.

You met that rule on Day 3 (a customer double-taps Place Order) and again on Day 4 (Hangfire
retries a job). Today it stops being an edge case and becomes normal behaviour.

**Everything builds on your Day 1–4 repo. Branch: `day-05-messaging`.**

---

## Ground rules (carried, plus two)

Thin controllers, DTOs, defend every choice, clean clone, write-ups. Two new laws today:

**1. Every consumer must survive getting the same message twice.** RabbitMQ guarantees
*at-least-once* delivery. It does not guarantee exactly-once — nothing does. If handling a
message twice does damage, the consumer is broken.

**2. Use the raw `RabbitMQ.Client` library. Do not use MassTransit, NServiceBus, or any other
messaging framework for the core work.** You are here to learn what RabbitMQ actually does —
exchanges, queues, bindings, acknowledgements, dead-letter routing. A framework does all of
that for you and hides it, and you cannot debug what you have never seen. There is one stretch
goal at the very end where you may try MassTransit, only to compare it against what you built
by hand. Not before.

---

## Part 0 — Get the broker running, and learn the model

### What RabbitMQ is

**RabbitMQ is a message broker** — a separate server program whose only job is to accept
messages, hold them in queues, and hand them to consumers. It does **not** run inside your API.
It runs on its own, and your app talks to it over the network.

### Start it

Run it with Docker, using the official image **`rabbitmq:management`**. The `:management` part
bundles RabbitMQ's management plugin — a small **web dashboard** where you can watch your
exchanges, queues and messages, and push messages in by hand. It is the same kind of window the
Hangfire dashboard gave you on Day 4.

```bash
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:management
```

- Port **5672** is the door your app uses to talk to the broker.
- Port **15672** is the dashboard. Open `http://localhost:15672` and log in with the built-in
  `guest` / `guest`.

Then change those credentials. `guest/guest` only works from your own machine, and you would
never put this dashboard on the public internet — the same lesson as the Hangfire dashboard.

### The model — learn this before you write any code

This trips up everyone at first, so read it twice:

> A producer **never** publishes straight to a queue. It publishes to an **exchange**. The
> exchange **routes** the message into zero or more **queues** bound to it. Consumers read
> from queues.

**producer → exchange → binding → queue → consumer**

The exchange *type* decides the routing. You use two today:

- **fanout** — copy the message into *every* bound queue. One event, many different reactions.
- **direct** — used here as a **work queue**: one queue, many workers, each message handled by
  exactly one of them.

### How your app connects (get this right now — it bites later)

- A **connection** is a TCP connection to the broker. It is expensive to open. Open **one** for
  the lifetime of the app and share it.
- A **channel** is a lightweight session inside that connection. Channels are **not
  thread-safe**. Use one channel per consumer, and never share a channel across threads.
- Opening a connection or a channel per message is the most common beginner mistake with this library. Do not do it.

### Where your consumers live

A consumer is a loop that runs the whole time the app is up, with nobody waiting on it. You
already built that exact shape on Day 4: a **`BackgroundService`**. Host every consumer today
as a `BackgroundService`, one per consumer.

Before moving on: broker up, app connected, and your connection visible in the dashboard.

---

## Part 1 — Your first event: `OrderPlaced` fan-out

### First, define the message

Decide what a message *is* before you publish one. Every message you publish today carries:

- **A unique message id** — a fresh Guid per message, set on RabbitMQ's `MessageId` property.
  Part 2 depends on this. Without it you cannot tell a redelivery from a new message.
- **An event name** — `OrderPlaced`.
- **A version number**, starting at 1. Message shapes change, and consumers written before the
  change still have to read them.
- **A JSON body** holding the order id, store id, customer id, total, and the UTC timestamp.
  Nothing else. A message carries the facts a consumer needs — not your entity graph.
- **Persistent delivery mode**, so the broker writes it to disk.

Write the contract down in your write-up. Once other things consume this event, it is a public
promise and you cannot change it freely.

### Then build it

When an order is placed, publish `OrderPlaced` to a **fanout** exchange. Build **two
independent consumers**, each with its **own queue** bound to that exchange:

1. **Notifications** — send the customer their confirmation and notify the Partner, through
   your Day 4 `INotificationEngine`.
2. **Analytics** — keep a running sales tally for every store. This one is **new work you
   build today**; you have nothing like it yet, so build all three pieces:
   - **A new entity and table.** One row per store, holding the store id (unique), how many
     orders that store has had, the total revenue, and when the row was last updated. Generate
     the migration yourself.
   - **The consumer that fills it.** The analytics consumer is the only thing that ever writes
     to this table. For each `OrderPlaced` message it adds 1 to that store's order count and
     adds the order total to that store's revenue.
   - **A way to read it.** Add a read endpoint, in the same controller that already serves your
     Day 1 store dashboard — it answers the same kind of question, so it belongs next to it.
     Follow your own `docs/api-guidelines.md` for the URL and status codes, and your Day 2
     authorization matrix for who may call it: a Partner sees their own store's tally, an Admin
     sees any store's. Without this endpoint you have no way to show anyone that the consumer works.

**The rule that matters: PlaceOrder publishes once and returns.** It does **not** call
notifications. It does **not** call analytics. It does not know they exist. If you catch
yourself calling a consumer directly "just to be safe", you have thrown away the entire point
of today.

### Prove it

1. Place an order. Both consumers fire, independently. Show it in the dashboard and in your
   logs.
2. **Stop the analytics consumer.** Place 3 more orders. Notifications still work. The
   analytics messages **pile up in its queue**, waiting — not lost. Restart it; it drains the
   backlog.
3. Make the queues **durable** and the messages **persistent**, then **restart the broker
   container** while messages are still unprocessed. They must survive. Do it once *without*
   durability first, so you watch them disappear. That contrast is the lesson.

---

## Part 2 — Acknowledgements and at-least-once (the spine of the day)

A consumer **acknowledges** ("acks") a message only *after* the work is finished. If the
consumer dies before acking, the broker assumes the work never happened and **delivers the
message again** — to another consumer, or to the same one after it restarts.

That is **at-least-once**. It is not a rare accident. It is the guarantee.

There are exactly three outcomes, and you must know all three:

- **ack** — done, delete it.
- **nack with requeue** — failed, put it back, try again.
- **nack without requeue** — failed, do not try again. (This is what feeds Part 4.)

**Turn auto-ack off** and ack only after the work is done. If you ack on receipt, a crash
mid-work loses the message silently, and none of today's experiments will work.

### The experiment (be exact)

1. In your **analytics** consumer, do the work (increment the store's tally), then **throw an
   exception before you ack**. Throw on the **first delivery only** — RabbitMQ marks a repeat
   with a `Redelivered` flag, so use that flag to decide. If you throw every time, you will
   loop forever.
2. Place **one** order.
3. Watch the message return to the queue and get picked up a second time.
4. Read the tally. It went up **twice**. One order, counted two times.

### The fix

Make the consumer **idempotent**: handling the same message twice leaves the same result as
handling it once. Keep a `ProcessedMessage` table keyed by the message id, and generate that
migration yourself.

**One warning, straight from Day 3:** "SELECT to check whether I have seen this id, then
INSERT" is a check-then-act race — two deliveries arriving together both pass the check. Put a
**unique constraint** on the message id and let the database be the judge, exactly like the
idempotency key you built on Day 3.

Think carefully about *which* id you key on. A message id is unique per message. An order id is
not — the same order can legitimately produce more than one event.

Then prove it: force the crash again, let it redeliver, and the tally is exactly right.

Every consumer you write from here on gets this treatment.

---

## Part 3 — Work queue: many workers sharing one job stream

Fan-out gives every consumer its own *copy* — different reactions to one event. A **work
queue** is the opposite: **one** job done **once**, but spread across many workers for speed.
One queue, many competing consumers, each message delivered to exactly one of them.

Build it for the order confirmation. The topology has two stages:

- Your **notifications** consumer handles `OrderPlaced` and publishes one
  `SendOrderConfirmation` message to the work queue.
- **Confirmation workers** read that queue and do the slow part: rendering and sending.

You will also need a way to drop many messages onto that queue at once, because placing 30 real
orders by hand to test this would waste your day. So build one small test hook: an
**Admin-only** endpoint that takes a count and publishes that many `SendOrderConfirmation`
messages. Number them 1, 2, 3 and so on, and put that number in each message body. The
experiment right below explains what you do with it and why the number matters.

### The experiment (be exact)

1. Make the worker take **200 ms** per message normally, and **3 seconds** when the message's
   sequence number leaves a remainder of 1 after dividing by 3 — messages 1, 4, 7, 10, and so
   on. Real work is never evenly sized, and that is the whole point of this part.
2. Start **three** copies of your app, so three consumers compete on the one queue. Give each
   its own port: `dotnet run --urls http://localhost:5101`, then `5102`, then `5103`.
3. Publish **30** messages from your Admin endpoint.
4. Watch the three workers share the load. Every message is handled exactly once, by one
   worker. Record how long the whole batch took.

### Now add prefetch

Run the batch first with **no prefetch set** — the default is unlimited. The broker hands the
messages out in strict rotation, one each, and never looks at how long a message takes to
handle. Watch what that does to your batch: the slow messages all land on the same worker,
which grinds through them one at a time, while the other two finish their fast messages in a
few seconds and then sit idle with nothing left to take.

Now set **prefetch (QoS) to 1**, so a worker holds one unacked message at a time, and run the
same batch again. Work now goes to whoever is actually free. Record the time again, and put
both numbers in your write-up.

One more thing to notice and write down: with three competing consumers, messages are no longer handled in the order they were published. If two events about the same order must be applied in order, a work queue is the wrong shape for them.

Stop the extra app copies when you finish this part.

---

## Part 4 — Poison messages and the dead-letter queue

A message that *always* fails — bad data, or a bug — is redelivered forever. It clogs the
queue, blocks every message behind it, and burns CPU going round in a loop. That is a **poison
message**, and a queue with no plan for one is a time bomb.

The plan is a **dead-letter exchange and queue (DLQ)**: after a message has failed a set number
of times, send it to a *separate* queue instead of retrying it forever.

**RabbitMQ does not count attempts for you.** Deciding where that count lives is part of this
task: either read the `x-death` header the broker adds when a message is dead-lettered, or
carry your own attempt count on the message and republish it. Pick one and defend it.

### The experiment (be exact)

1. Publish a deliberately broken message: an `OrderPlaced` whose order id does not exist, so
   processing always throws.
2. Run it **with no limit** first. It fails, requeues, fails, requeues — forever — and good
   messages stack up behind it. Watch it happen. Do not skip this step; seeing the loop is what
   makes the DLQ make sense.
3. Now add a limit of **3 attempts** and a dead-letter route. On the third failure the message
   goes to a `...dlq` queue instead of back to the main one.
4. Re-run. The poison message fails 3 times, lands in the DLQ, and the main queue flows again.

In the write-up: where does the poison message end up, and how does the on-call engineer find
it at 3am and decide what to do with it?

---

## Part 5 — Command or event? Now that you have used both

You have built with both tools, so now write the rule down. One line each:

- **Hangfire is a command:** *"run this task."* You own the task, you know it must run, you
  know what it does. One actor.
- **RabbitMQ is an event:** *"this happened."* You announce it; whoever cares reacts. The
  publisher does not know the consumers, and a new consumer can appear without the publisher
  changing at all.

Now classify each of these four, and justify every one:

1. An order is placed. The customer must be notified, analytics must update, and soon loyalty
   points must be awarded — with more reactions likely later.
2. Every night at 02:00, generate the per-store sales reports.
3. Send the OTP text right after request-OTP.
4. A separate shipping system, run by another team, must learn whenever an order is delivered.

There is no trick — each one has a clearly better fit. The marks are in the *why*: is this a
task I own and want run later, or an announcement that others react to?

---

## Part 6 — The gap you must find and name, and must not fix

**Find it, document it, do not fix it.** Naming it precisely is the whole point of this part.

PlaceOrder now writes to **two different systems**: it saves the order to SQL, and it publishes
`OrderPlaced` to RabbitMQ. No transaction covers both. So reason through two crashes and write
down exactly what is lost in each:

- The app **commits the order, then dies before the publish.** The order exists. No event ever
  goes out. No confirmation, no analytics — and nothing anywhere reports a problem.
- The app **publishes first, then the database save fails and rolls back.** Now there is an
  event for an order that does not exist, and your consumers act on a ghost.

This is the **dual-write problem**: two writes that must both happen or neither, with no
transaction spanning them. It is a known, well-understood, and genuinely hard problem. Today
your only job is to document it precisely in `docs/day-05-messaging.md` — where exactly the gap
sits, and what is lost or orphaned in each of the two crashes.

**Do not patch it.** A try/catch, a retry, or a "check afterwards" is not a fix, and it usually
hides a race instead of closing one. (If you did Day 4's enqueue-gap stretch, this is the same
problem, bigger.)

---

## Part 7 — Write-up

`docs/day-05-messaging.md`:

- A diagram of your topology — exchanges, queues, bindings, consumers. A Mermaid diagram in
  markdown is fine.
- Your message contract, and why a message id and a version are on it.
- The redelivery story: the double-count you caused in Part 2, and exactly how you made the
  consumer idempotent.
- Fanout vs work queue in your own words, and where you used each.
- Your two prefetch timings, and the ordering guarantee you gave up by using competing
  consumers.
- Your DLQ setup: where the attempt count lives, and what happens to a poison message.
- Your Hangfire-vs-RabbitMQ rule, and your call on the four scenarios.
- The dual-write gap: where it is, and what is at risk.
- The hardest thing today, and how you worked it out.

---

## Definition of Done

- [ ] RabbitMQ running in Docker, management UI reachable, default credentials changed.
- [ ] One shared connection, one channel per consumer, and every consumer hosted as a
      `BackgroundService`.
- [ ] Every message carries a unique message id, an event name, a version, and a JSON body, and
      is published persistently. The contract is written down.
- [ ] `OrderPlaced` published to a fanout exchange; two independent consumers on their own
      queues; PlaceOrder calls neither of them directly.
- [ ] The per-store sales tally is a new table, written only by the analytics consumer, and
      readable through an endpoint that obeys your Day 2 authorization matrix.
- [ ] Proven independent and durable: stopping one consumer doesn't affect the other, its
      messages wait in its queue, and messages survive a broker restart.
- [ ] Auto-ack off, ack after the work. The crash-before-ack redelivery is reproduced, the
      double-count is shown, the consumer is then made idempotent behind a unique constraint,
      and proven correct under a forced redelivery.
- [ ] A work queue with 3 competing consumers sharing 30 messages; run with no prefetch and
      with prefetch 1; both timings recorded.
- [ ] A poison message loops forever with no DLQ; then a 3-attempt limit plus a dead-letter
      route sends it to the DLQ and unblocks the main queue. Both shown.
- [ ] The four scenarios classified, with reasons.
- [ ] The dual-write gap documented, not fixed.
- [ ] `docs/day-05-messaging.md` complete with a topology diagram. Branch merged; clean clone
      still runs (README: how to start RabbitMQ and open the dashboard).

---

## Stretch goals (only if you still have fuel)

1. **Rebuild one flow with MassTransit** and compare it against what you wired by hand. What
   did it handle for you — retries, dead-lettering, serialization, idempotency helpers? What
   visibility did you lose by not wiring the exchanges yourself? Now you can defend when a
   framework is worth it.
2. **Topic exchange for order status.** Publish `order.placed`, `order.delivered` and
   `order.cancelled` to a **topic** exchange. One consumer subscribes to `order.*`
   (everything), another to `order.cancelled` only. Show the routing working.
3. **Publisher confirms.** Turn them on so you *know* the broker accepted a message. Then
   explain precisely which hole they close, and which hole they leave wide open.
4. **A second event and a version bump.** Publish `OrderStatusChanged` as well, and have the
   analytics consumer handle both. Then change the shape of one event, raise its version, and
   keep the old consumer working. This is where the version field earns its place.

Announce what happened. Let the listeners sort themselves out. And assume every one of them
will hear you twice.
