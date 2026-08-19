# Day 7 — Live Order Tracking: Pushing Events to a Screen

## Where we are

Your system now sends events safely. An order is placed, the outbox saves it, the relay sends
it, consumers react. All of that happens on the server, where nobody can see it.

Today you push those events onto a person's screen. The customer opens their order page. The
Partner accepts the order. The customer's screen changes **by itself**, with no refresh button
and no waiting.

### The problem with normal HTTP

Every request you have written works the same way. The client asks. The server answers. The
server can never speak first.

So how does a page find out that something changed? The usual answer is **polling**: ask the
server every few seconds, "anything new?" Almost every answer is "no". If you poll every 5
seconds, 1,000 customers make 720,000 requests an hour, and nearly all of them are wasted. And
the customer still waits up to 5 seconds to see a change.

### What a WebSocket is

A **WebSocket** fixes this. The client makes one normal HTTP request and asks to upgrade it. The
server agrees. That one connection then stays open, and **both sides can send at any time**.

No polling. No waiting. The server speaks when it has something to say.

Today you build that, and then you meet its three hard parts:

1. **Who is allowed to listen?** A connection is not permission.
2. **A socket is not a queue.** Messages sent while someone is disconnected are gone forever.
3. **Two copies of your app do not share connections.** This one surprises everybody.

**Everything builds on your Day 1–6 repo. Branch: `day-07-realtime`.**

---

## Ground rules (carried, plus one)

Thin controllers, DTOs, defend every choice, clean clone, write-ups. One new rule today:

**A connected user is still an untrusted user.**

On Day 2 you learned that a valid token is not permission to see everything. That rule does not
change just because the transport changed. Every check you do on an HTTP endpoint, you do again
here.

---

## Part 0 — See a WebSocket with your own eyes

Before any library, build one tiny thing by hand. This takes about half an hour and it stops
SignalR from looking like magic later.

**Do this, exactly:**

1. Add one endpoint at `/ws/echo`. It accepts a WebSocket connection. Whatever text the client
   sends, it sends the same text back. That is all it does.
2. Open Chrome. Open DevTools, go to the **Network** tab, and click the **WS** filter.
3. Connect to it. You can do this from the DevTools console:
   ```js
   const s = new WebSocket('ws://localhost:5000/ws/echo');
   s.onmessage = e => console.log('got:', e.data);
   s.onopen = () => s.send('hello');
   ```
4. Now look at the Network tab and find these three things:
   - The first request is a normal **HTTP GET**, with a header `Upgrade: websocket`.
   - The response status is **101 Switching Protocols**. Not 200. This is the server saying
     "yes, let's keep this line open."
   - After that, a **Messages** tab shows every frame sent each way.

Write down what you saw. That 101 response is the whole trick. One HTTP request goes in, and a
permanent two-way line comes out.

### Now switch to SignalR

For the rest of today, use **SignalR**. It is a Microsoft library that sits on top of
WebSockets and does three jobs you would otherwise write yourself:

- It reconnects when the connection drops.
- It lets you put connections into named **groups** and send to a whole group at once.
- It falls back to other transports when a real WebSocket cannot be made.

You have now seen what it is built on, so you know what it is doing for you.

---

## Part 1 — The test page (this one is given to you)

You need a page to see this working. **Do not spend time building one.** Copy the file below
into your project as `wwwroot/tracker.html`, turn on static files, and move on. Your work today
is the server.

```html
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><title>Order tracker</title></head>
<body>
  <h3>Live order tracker</h3>
  <p>Token: <input id="token" size="60"></p>
  <p>Order id: <input id="orderId" size="10"> <button onclick="start()">Watch</button></p>
  <h4>Status: <span id="status">unknown</span></h4>
  <ul id="log"></ul>

  <script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@8/dist/browser/signalr.min.js"></script>
  <script>
    let conn;
    function write(text) {
      const li = document.createElement('li');
      li.textContent = new Date().toLocaleTimeString() + ' — ' + text;
      document.getElementById('log').appendChild(li);
    }
    async function start() {
      const token = document.getElementById('token').value;
      const orderId = Number(document.getElementById('orderId').value);

      conn = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/orders?access_token=' + token)
        .withAutomaticReconnect()
        .build();

      conn.on('OrderStatusChanged', m => {
        document.getElementById('status').textContent = m.status;
        write('status -> ' + m.status);
      });

      conn.onreconnected(() => write('reconnected'));
      conn.onclose(() => write('connection closed'));

      await conn.start();
      write('connected');
      await conn.invoke('WatchOrder', orderId);
      write('watching order ' + orderId);
    }
  </script>
</body>
</html>
```

This page is deliberately simple, and it has bugs in it. You will find them in Part 4. Do not
try to fix it before then.

---

## Part 2 — The hub, and who is allowed to listen

### Build the hub

Create a SignalR hub at `/hubs/orders`. It has one method the client can call:
`WatchOrder(long orderId)`.

### Get the token onto the socket

Here is a real problem you have not met before. **A browser cannot send an `Authorization`
header when it opens a WebSocket.** The browser's WebSocket API simply does not allow custom
headers. So your normal Day 2 setup will not see any token.

The standard answer is the one the given page uses: put the token in the **query string**, and
tell your JWT setup to read it from there for hub requests only.

This is a trade-off, not a free win. Query strings end up in server logs, proxy logs, and
browser history in a way headers do not. Write down in your write-up what that risk is, and one
thing you could do to reduce it.

### Connecting is not permission

Now the part that matters most today.

A customer connects with a valid token. Good — you know who they are. That tells you **nothing**
about which orders they may watch.

So when a client calls `WatchOrder(orderId)`, the server must:

1. Read who the caller is, from `ICurrentUser` — the same one seam from Day 2.
2. Look up that order **in the database**.
3. Check the order really belongs to that customer.
4. Only then add the connection to a group named for that order.
5. If the order is not theirs, refuse. Do not add them to the group.

**Do this attack, exactly:**

1. Log in as customer A. Open the page. Put A's token in.
2. Find an order id that belongs to **customer B**.
3. Try to watch it.
4. You must be refused. Then have the Partner move customer B's order forward, and confirm that
   **nothing appears on customer A's screen**.

If A sees B's order updates, you have built the same hole you closed on Day 2, on a new pipe.
This is the single most important check of the day.

### The Partner feed

Partners need this too. A Partner watching their dashboard should see new orders appear on their
own store, live. Add a second group for that.

A Partner may only join the group for their own store. Check it against the database, the same
way. A Partner for store 1 who asks for store 2's feed is refused.

---

## Part 3 — Send the events to the screen

You already have events flowing. Now one more thing reacts to them.

### Publish the status event

Your status-change code needs to announce itself. When an order's status changes, publish an
`OrderStatusChanged` event. Write it to the **outbox**, in the same transaction as the status
change, exactly like Day 6. Everything you built yesterday applies here with no exceptions.

The message body carries the order id, the customer id, the store id, the new status, and the
UTC time.

### Add the consumer that pushes

Add a **new consumer** on your fanout exchange. It handles `OrderPlaced` and
`OrderStatusChanged`. For each one it sends the update to the right SignalR group — the order's
group, and the store's group.

Notice what you did **not** have to do. You did not touch PlaceOrder. You did not touch the
status-change service. You did not touch the notifications consumer or the analytics consumer.
You added a whole new reaction by adding one consumer to an exchange.

That is exactly what you built the fanout for on Day 5. Say so in your write-up.

### Prove it

1. Open the page as a customer. Watch one of their orders.
2. In another window, log in as the Partner of that store.
3. Move the order forward: `Accepted`, then `Preparing`, then `OutForDelivery`, then
   `Delivered`.
4. The customer's page updates every time, by itself. No refresh.

---

## Part 4 — A socket is not a queue

Your page works. Now break it, twice, with one experiment.

**Do this, exactly:**

1. Open the page as a customer and watch an order.
2. Now cut the connection. Turn off wifi, or use DevTools → Network → set throttling to
   **Offline**.
3. While it is disconnected, have the Partner move that order forward **two** steps.
4. Wait about 30 seconds. Then put the connection back.
5. Look at the page.

You will find **two** separate bugs. Find both before you read on.

### Bug one — the missed updates are gone forever

RabbitMQ holds messages for a consumer that is away. That is what a queue does. **A WebSocket
does not.** If nobody is connected, the server pushes into nothing and the update is gone. It
is not stored anywhere. It will never arrive.

So the screen now shows an old status, and it will stay wrong forever.

### Bug two — after reconnecting, you are a stranger again

SignalR reconnected on its own. But a reconnect creates a **new connection**, with a new
connection id. Group membership belongs to the old, dead connection. So the page is connected
and receiving nothing, even for **new** changes.

This one is worse than bug one, because it looks like everything is fine.

### The fix

The rule is this, and it is worth memorising:

> **The socket carries changes. The API carries truth.**

Never treat a socket as the only way you learn something. On every connect **and every
reconnect**, the page must:

1. **Join the group first.** Call `WatchOrder` again.
2. **Then fetch the current state** over normal HTTP, using your Day 1 order details endpoint.
3. Then keep listening.

**The order of those two steps is not a detail — get it the wrong way round and you have a
hole.** Work out for yourself what happens if you fetch first and join second, and what exactly
gets lost in between. Write your answer in the write-up.

On the page, add the missing call inside `conn.onreconnected(...)`. That is one line. That is
all the page work you are allowed to do today.

Then run the same experiment again. After reconnecting, the page must show the correct current
status, and must keep updating from then on.

One thing to hold on to: this fix only helps a customer who **comes back**. Part 7 asks what
happens when they do not.

---

## Part 5 — Two copies of the app, one customer

This is the big one. Everything so far assumed one copy of your app is running.

**Do this, exactly:**

1. Start **two** copies of your app:
   `dotnet run --urls http://localhost:5101`
   `dotnet run --urls http://localhost:5102`
2. Open the page against copy **5101** and watch an order.
3. Now place or advance orders until the update is handled by copy **5102**. Watch your logs to
   see which copy handled it.
4. When copy 5102 handles it, **nothing appears on the screen**.

### Why

SignalR only knows about the connections **its own copy of the app is holding**. Copy 5102 has
the event. The customer's connection lives inside copy 5101. Copy 5102 sends the message to its
own connections, which do not include that customer. The message goes nowhere.

This is called the **backplane problem**. Every real system that uses sockets on more than one
server has to solve it.

### The fix, using what you already know

Every copy of the app must hear about every event, so that whichever copy is holding the
connection can push it.

Look carefully at your Day 5 queues, because this is the exact opposite of the work queue you
built then:

- **A work queue** is one queue shared by many workers. Each message goes to **one** of them.
  That is what you want when the job must happen once.
- **Here you want the opposite.** Every copy must get its **own copy** of every message. So each
  copy needs **its own queue**, bound to the fanout exchange.

So: when a copy of the app starts, it creates a queue **just for itself**, with a name nothing
else will use, and binds it to the exchange. When that copy shuts down, the queue goes away with
it. Research which queue settings give you that.

**Warning:** if you use one shared queue for this consumer, only one copy receives each event,
and you have built the bug you are trying to fix.

### Prove it

1. Two copies running.
2. Two browser windows: one connected to 5101, one to 5102, both watching the **same** order.
3. Move the order forward.
4. **Both windows update**, every time, no matter which copy handled the event.

One more thing to find out, and answer in your write-up. SignalR can solve this same problem for
you, with a few lines of setup instead of the queues you just built. Find out what that feature
is called and roughly how it works. Then answer one question: what would it have done for you
here?

---

## Part 6 — What a lot of connections costs you

An HTTP request arrives, is answered, and is gone. A socket **stays**. Every connected user
holds memory on your server for as long as they keep the page open. That is a new kind of cost.

**Do this, exactly:**

1. Write a small script that opens **200** connections to your hub and keeps them open.
2. Watch your app's memory before and after. Write both numbers down.
3. While all 200 are connected, call a normal HTTP endpoint. It must still answer normally.

Then answer this in your write-up: a laptop closes without warning. The connection is not closed
politely, so the server does not get told. How does your server ever find out that connection is
dead, and how long does it take? Look up what SignalR sends to check.

---

## Part 7 — The wall you must find, and cannot get past today

**Find it, prove it, write it down. Do not try to fix it.** Naming it exactly is the whole job of
this part.

In Part 4 you took care of the customer who disconnected and **came back**. Now deal with the
customer who does not come back.

**Do this, exactly:**

1. Open the tracker page as a customer and watch an order.
2. Now **close the browser tab completely.** Not offline mode — closed. This is a customer
   closing the app and putting their phone in their pocket.
3. As the Partner, move that order all the way from `Accepted` to `Delivered`.
4. Watch your server logs while you do it.

Look at what your server did. The consumer received every event. It sent every update to the
order's group. The group had **nobody in it**. Every message went nowhere.

And notice this: nothing failed. No error. No warning. Your code did its job perfectly, and the
customer learned nothing.

5. Write down every single update that reached nobody. In a normal delivery that is four or five
   messages, including "your order is on the way."

### Why this is not a bug you can fix

Your Part 4 fix works only **when the customer comes back**. Reopening the page fetches the
truth, and everything looks fine again.

But think about what an order update is actually for. "Your driver is outside" is only useful
**at that moment**. If the customer has to open the app to find out, the message already failed
at its job. The whole point was to reach someone who was not looking.

A socket cannot do that, and better code will not change it:

- A socket needs an app that is **open and running** on both ends. Close the app and the line is
  gone.
- On a phone it is worse. Phone operating systems suspend apps that sit in the background, to
  save battery. So even an app the customer thinks is open usually has no live connection at all.

This is not a flaw in SignalR, and it is not a flaw in your code. It is the edge of what this
kind of tool can do. Every delivery app in the world hits this same wall.

### Your job today

Answer these three in your write-up, precisely:

1. Which order updates does a customer miss if they close the app right after ordering?
2. Your server sent those messages to a group with nobody in it. How would you even find out
   that happened? Is there anything in your system today that would tell you?
3. Say in one sentence what a socket fundamentally cannot do.

This is a well-understood problem and it does have a real answer. That answer is not a socket,
and it is not today's job. **Today you only name the wall.**

---

## Part 8 — Write-up

`docs/day-07-realtime.md`:

- What you saw in DevTools in Part 0: the `Upgrade` header, the 101 response, and what that
  means in your own words.
- Why polling is a poor answer, with the request numbers from your own system.
- How the token reaches the socket, why the header trick does not work in a browser, and the
  risk of putting a token in a query string.
- Your ownership check for `WatchOrder`, and the result of the customer A / customer B attack.
- The new consumer you added, and the list of files you did **not** have to touch to add it.
- Both Part 4 bugs, in your own words. Then the rule: the socket carries changes, the API carries
  truth.
- Which order you do "join group" and "fetch state" in, and exactly what breaks in the other
  order.
- The backplane problem, and why this consumer needs one queue **per copy** while your Day 5 work
  queue needed one queue **shared**. This contrast is the best thing you learned today — explain
  it properly.
- Your 200-connection memory numbers, and how a dead connection is eventually noticed.
- **The wall from Part 7.** The updates a customer misses when they close the app, whether
  anything in your system would tell you it happened, and one sentence saying what a socket
  fundamentally cannot do.
- The hardest thing today, and how you worked it out.

---

## Definition of Done

- [ ] A raw `/ws/echo` endpoint exists, and the write-up describes the `Upgrade` header and the
      101 response seen in DevTools.
- [ ] A SignalR hub at `/hubs/orders`, with the JWT read from the query string for hub requests.
- [ ] `WatchOrder` checks the order belongs to the caller, against the database, before joining
      the group. Customer A is refused customer B's order, and never receives B's events.
- [ ] Partners get a live feed of their own store's new orders, and cannot join another store's
      feed.
- [ ] `OrderStatusChanged` is published through the outbox, in the same transaction as the status
      change.
- [ ] A new consumer pushes `OrderPlaced` and `OrderStatusChanged` to the right groups, and
      PlaceOrder and the status-change service were not modified to make it work.
- [ ] The customer's page follows an order from `Accepted` to `Delivered` with no refresh.
- [ ] Both Part 4 bugs are reproduced and written down: the updates lost while disconnected, and
      the lost group membership after reconnect.
- [ ] After a 30-second disconnect with two status changes, the page shows the correct current
      status and keeps updating.
- [ ] Two copies of the app run at once, and two browsers connected to different copies both
      receive every update.
- [ ] The push consumer uses one queue **per app copy**, not one shared queue, and the write-up
      explains why.
- [ ] 200 connections held open, with memory before and after recorded, and HTTP still working.
- [ ] The Part 7 wall is proven with the tab closed, and the missed updates are listed. It is
      documented, **not** fixed.
- [ ] `docs/day-07-realtime.md` complete. Branch merged. Clean clone still runs (README: how to
      open the tracker page and where to get a token).

---

## Stretch goals (only if you still have fuel)

1. **Let SignalR do Part 5 for you.** In Part 5 you fixed the two-copies problem yourself, by
   giving each copy its own queue. SignalR can do that job for you instead. Turn its version on,
   delete your own queue code, and check that the two-browser test still passes.
   Then answer two questions. What work did SignalR take over from you? And if you had used it
   from the start, which part of the problem would you never have understood?

2. **Show who is watching right now.** Keep track of which customers currently have a live
   connection open. Then show a Partner whether the customer is watching this order at this
   moment.
   Watch out: with two copies of the app running, no single copy knows the full answer. Copy A
   only knows about its own connections. You have to solve that part too.

3. **Send data the other way.** So far the server sends and the client only listens. A socket
   works in both directions.
   Add a driver who sends their location **up** to the server every 5 seconds, through the same
   connection. The server then sends that location **down** to the customer watching that order.

4. **Build it again with Server-Sent Events, then choose.** Server-Sent Events (SSE) is another
   way for a server to push data to a browser. It goes one way only, server to client, and it
   uses a normal HTTP connection instead of upgrading to a socket.
   Build the same order tracking with SSE. Then say which of the two you would pick for this
   feature, and why.

One request goes in. A line that stays open comes out. Everything hard today comes from that
line: it stays open, it belongs to one person, and it breaks easily.
