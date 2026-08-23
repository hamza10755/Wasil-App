# Day 8 — Notifications: Reaching Someone Who Is Not Looking

## Where we are

Yesterday you found a wall. A socket only reaches an app that is open. Close the app and there
is no way to tell the customer anything. Their driver is outside and they do not know.

Today you get past that wall.

### This really happened at MyThings

This is not a made-up example. Here is the real history of the system you are copying.

We started with **SignalR** — the same tool you used yesterday. It worked well, until we hit
exactly the wall you found: the customer closes the app, and we cannot reach them.

So we added **OneSignal**, a service that sends real push notifications. Order tracking moved
onto it.

Then OneSignal's pricing changed. So we are now moving to **Google's Firebase** instead.

And here is the part worth noticing. **SignalR did not go away.** It is still running today,
carrying support chat between customers, drivers and our staff. The two tools did not replace
each other. They ended up doing two different jobs.

That is today's whole lesson: not "which tool is better", but "which tool for which job".

### How a push notification actually works

This is the idea that makes everything else make sense, so read it twice.

With a socket, **your server holds a connection to the app**. No app, no connection.

A push notification works completely differently. **Your server never talks to the phone at
all.** Instead:

1. Google (for Android and Chrome) and Apple (for iPhone) already keep **one** connection open
   to every phone. The phone's operating system holds it open, all the time.
2. That one connection is **shared by every app on the phone**. Not one per app. One in total.
3. You hand your message to Google. Google sends it down that connection.
4. The operating system shows it, or wakes your app up.

So the reason a push reaches a closed app is simple: **it is not your app's connection. It is
the operating system's connection.** Your app does not need to be running at all.

That also explains the cost. A socket costs you memory for every connected person, every minute,
even when nothing is happening. A push costs you nothing while you wait, because you are not
holding anything open. You only pay when you send.

**Everything builds on your Day 1–7 repo. Branch: `day-08-notifications`.**

---

## Ground rules (carried, plus one)

Thin controllers, DTOs, defend every choice, clean clone, write-ups. One new rule today:

**A notification is a nudge, not the truth.**

You can never be sure a push notification arrived. So the app must always be correct when the
customer opens it, whether or not any notification was ever delivered. Never let a push be the
only way something important reaches someone.

This is the same shape as yesterday's rule that the socket carries changes and the API carries
truth.

---

## Part 0 — Set up Firebase

You need a Firebase project. Each of you makes your own. It is free.

**Do this, exactly:**

1. Go to the Firebase console and create a new project.
2. Add a **Web app** to it. Firebase shows you a config block with `apiKey`, `projectId`,
   `messagingSenderId` and `appId`. Keep it.
3. Open **Project settings → Cloud Messaging**. Under **Web Push certificates**, generate a key
   pair. Copy the public key. This is called the **VAPID key**.
4. Open **Project settings → Service accounts** and generate a new private key. This downloads a
   **JSON file**. Your server uses this to prove it is allowed to send.

### That JSON file is a real secret

Anyone holding it can send notifications to all of your users. Treat it exactly like the JWT
signing secret from Day 2:

- **Do not commit it to git.** Add it to `.gitignore` before you download it.
- Load it the same way you load your other secrets.
- In your write-up, say where you put it and why.

Add the `FirebaseAdmin` NuGet package to your server.

### One honest note about testing

You have no mobile app, so you will test with **web push in Chrome on your computer**. That is
real push — it goes through Google exactly the same way. But be honest about one difference:
Chrome must still be running, even if minimised. On a real phone the operating system holds the
connection, so the app can be fully closed.

Your test proves the mechanism. It does not prove the phone case. Say that in your write-up.

---

## Part 1 — The device registry

Here is the first thing that surprises people:

> **A push notification is not sent to a user. It is sent to a device.**

Firebase gives each installed app on each device a long string called a **device token**. To
notify a customer, you must know their devices' tokens. Firebase does not know who your
customers are. That mapping is **your** job, and it is most of today's backend work.

### Build the table

Create a `DeviceToken` entity and table. Generate the migration yourself. Each row holds:

- **The token** itself. Put a **unique constraint** on it.
- **Which user it belongs to.**
- **The platform** (`web`, `android`, `ios`).
- **When it was registered**, and **when it was last seen**.
- **Whether it is still active.**

### The endpoints

1. **Register a device.** Protected — the caller must be logged in. Takes a token and a
   platform. The user is taken from `ICurrentUser`, never from the request body.
2. **Unregister a device.** Called when the user logs out. Takes the token and deactivates it.

### Three rules that are easy to get wrong

**One user has many devices.** A phone, a tablet, a laptop. Sending to a customer means sending
to all of their active devices, not one.

**Tokens change on their own.** Firebase can replace a device's token at any time. The client
re-registers with the new one. So a user slowly collects dead tokens. Part 6 deals with those.

**A device can change hands — this one is a privacy bug.** Think about a shared phone. User A
logs in and registers device token `XYZ`. User A logs out. User B logs in on the same phone and
registers the **same** token `XYZ`, because it belongs to the device, not the person.

If your register endpoint just inserts a row, that token now belongs to two users. User B's phone
starts buzzing with **user A's order updates**.

So your register endpoint must handle this: if that token already exists under a different user,
it now belongs to the new user only. The unique constraint on the token is what makes this
possible. Decide how you do it, and explain it in your write-up.

---

## Part 2 — Send your first notification

### The test page (given to you — do not build your own)

Save this as `wwwroot/push.html`. Paste your own Firebase values into it.

```html
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><title>Push test</title></head>
<body>
  <h3>Push notification test</h3>
  <p>Your login token: <input id="jwt" size="60"></p>
  <button onclick="setup()">Turn on notifications</button>
  <p>Device token: <code id="device"></code></p>
  <ul id="log"></ul>

  <script src="https://www.gstatic.com/firebasejs/10.12.2/firebase-app-compat.js"></script>
  <script src="https://www.gstatic.com/firebasejs/10.12.2/firebase-messaging-compat.js"></script>
  <script>
    const firebaseConfig = {
      apiKey: "PASTE", authDomain: "PASTE", projectId: "PASTE",
      messagingSenderId: "PASTE", appId: "PASTE"
    };
    const VAPID_KEY = "PASTE";

    function log(t) {
      const li = document.createElement('li');
      li.textContent = new Date().toLocaleTimeString() + ' — ' + t;
      document.getElementById('log').appendChild(li);
    }

    async function setup() {
      firebase.initializeApp(firebaseConfig);
      const messaging = firebase.messaging();

      const permission = await Notification.requestPermission();
      log('permission: ' + permission);
      if (permission !== 'granted') return;

      const deviceToken = await messaging.getToken({ vapidKey: VAPID_KEY });
      document.getElementById('device').textContent = deviceToken;
      log('got device token');

      const res = await fetch('/api/devices/register', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': 'Bearer ' + document.getElementById('jwt').value
        },
        body: JSON.stringify({ token: deviceToken, platform: 'web' })
      });
      log('register returned ' + res.status);

      messaging.onMessage(m => log('app open: ' + JSON.stringify(m.notification)));
    }
  </script>
</body>
</html>
```

You also need a **service worker** file. This is the small script Chrome keeps running in the
background so a notification can appear while your page is closed. Save it as
`wwwroot/firebase-messaging-sw.js`:

```js
importScripts('https://www.gstatic.com/firebasejs/10.12.2/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/10.12.2/firebase-messaging-compat.js');

firebase.initializeApp({
  apiKey: "PASTE", authDomain: "PASTE", projectId: "PASTE",
  messagingSenderId: "PASTE", appId: "PASTE"
});

firebase.messaging();
```

Four things that will waste your time if you miss them:

- The service worker file **must sit at the very top of your site**, reachable at
  `/firebase-messaging-sw.js`. Not in a folder.
- Chrome only allows push on **https** or on **localhost**. Localhost is fine.
- Change `/api/devices/register` to match your own `docs/api-guidelines.md`.
- The Firebase SDK version in those script tags will go out of date. If it does not work, use the
  version Firebase's own setup page shows you.

### Send one by hand

Add a temporary Admin-only endpoint that sends a notification to one device token. Use it to
send yourself a test message. Watch it appear in Chrome. Get this working before anything else.

### Now wire it to your events

Add a **new consumer** on your fanout exchange, for `OrderStatusChanged`. It looks up the
customer's active device tokens and sends them a notification.

Once again: you did **not** touch PlaceOrder. You did not touch the status service. You added a
new way of reaching people by adding one consumer to an exchange. That is the third time this has
paid off. Say so in your write-up.

---

## Part 3 — Who is connected right now?

To choose between a socket and a push, your server needs to answer one question: **does this
customer have a live connection open at this moment?**

That is harder than it sounds, because with two copies of your app running, each copy only knows
about its own connections. Copy A cannot see copy B's.

**Build a shared record.** A `UserConnection` table with the user id, the connection id, which
copy of the app holds it, and when it was last seen.

- When a SignalR connection opens, insert a row.
- When it closes, delete the row.

Now any copy can ask "is this user connected?" by looking at the table.

### The experiment that breaks it (be exact)

1. Start one copy of your app. Open the tracker page from yesterday and connect as a customer.
2. Check the `UserConnection` table. Your row is there.
3. Now **kill that copy of the app with `Environment.Exit(1)`.** Do not stop it politely. This is
   a server crashing, which is the normal case.
4. Look at the table again. **The row is still there.** Nobody deleted it, because the code that
   deletes it never ran.
5. Start the app again and place an order for that customer. Your router asks "are they
   connected?" The table says yes. So it sends over the socket, to a connection that no longer
   exists.

The customer gets **nothing at all**. No live update, and no notification either — because your
system thinks it already reached them.

That is worse than having no router.

### The fix

A record of "who is connected" cannot be trusted unless something keeps it fresh. Add a
heartbeat: the connected client tells the server it is still there every 30 seconds, and the
server updates `last seen`. Then treat any row not seen for **2 minutes** as dead, and ignore it.

Also clear out that copy's rows when the app starts up again.

Re-run the experiment. After the crash, the customer must get a push notification instead of
silence.

---

## Part 4 — The router: socket or push?

Now make Day 4's `INotificationEngine` do the job it was always pretending to do. Until now it
just wrote to a log. Today it becomes the thing that **decides how to reach someone**.

The rule:

> If the customer has a live connection right now, send it over the socket.
> If not, send a push notification.

Every consumer that needs to tell a human something goes through this one place. No consumer
picks a channel by itself.

### But "is connected" is always a guess

Here is the hard part. Yesterday you learned it takes about 30 seconds for the server to notice
a dead connection. So when your router asks "are they connected?", the answer can be wrong. The
customer may have walked into a lift two seconds ago.

So sometimes you will send over a socket that is already dead, and the customer gets nothing.

You cannot remove this problem. You can only choose which mistake you prefer:

- **Send only over the socket when it looks connected.** Risk: the customer misses "your driver
  is outside" completely.
- **Send both, every time.** Risk: the customer gets the same message twice.

Pick one and defend it in the write-up. Think about which mistake a real customer would forgive.

This is the same question you have now answered three times: on Day 5 with redelivery, on Day 6
with publish-then-mark, and on Day 7 with join-then-fetch. **Choose the failure you can live
with.**

### Which tool for which job?

Here is the cost of each, in one line each:

- **A socket costs you per connected person, per minute** — even when nothing happens.
- **A push costs you per message.** Waiting costs nothing.

So many people who are mostly idle favours push. A few people needing constant updates favours a
socket. But before you ask what it costs, ask the more important question: **can it even reach
them?**

Now decide for each of these five, and justify every one:

1. The driver's location moves on the customer's map, updating every 5 seconds while they watch.
2. "Your order is on the way" — the customer's app is closed.
3. A promotion sent to 50,000 customers: "20% off this weekend."
4. A Partner's dashboard in the shop, showing new orders arriving. The screen is open all day.
5. "Your driver is outside" — and the customer has the app open right now.

There is no trick. Each has a clearly better answer, and one of them is the reason the router in
this part exists.

---

## Part 5 — Never send the same notification twice

Your events are at-least-once. You have known that since Day 5.

Until today, a duplicate meant a wrong number in a table. Today a duplicate means **a customer's
phone buzzing twice at 11pm.** Same bug. It now has a face.

**Do this, exactly:**

1. Force a redelivery of an `OrderStatusChanged` message, the same way you did on Day 5: do the
   work, then throw before you ack, on the first delivery only.
2. Watch Chrome.

If two notifications appear, your consumer is not idempotent. Use the `ProcessedMessage` table
you already built, keyed on the message id, with the unique constraint.

Then re-run it. Exactly **one** notification appears.

### The part you cannot fix

You met this on Day 4 with the SMS sender, and it is worth saying again clearly.

A notification is an **outside effect you cannot take back**. Once Google has it, it is gone. You
can make the **decision** to send idempotent. You can never un-send.

So the honest goal is not "exactly once". It is "we do not decide to send twice". Write that
difference down in your own words.

---

## Part 6 — Dead tokens, and the two kinds of failure

Every send can fail, and **the reason matters more than the failure**.

Firebase answers per token. Some errors mean *this token is dead forever* — the app was
uninstalled, or the token was replaced. Others mean *something went wrong just now* — a network
blip, or Firebase having a bad minute.

Treating those two the same is the bug:

- Delete a token on a temporary error, and you have just switched off notifications for a real
  customer, permanently.
- Keep sending to a dead token forever, and you waste quota on every send, and your numbers lie
  to you about how many people you are reaching.

**Do this:**

1. Find the specific error Firebase returns when a token is no longer valid. Look it up in the
   `FirebaseAdmin` SDK — do not guess it.
2. On **that** error only, mark the token inactive and stop sending to it.
3. On any other error, leave the token alone and let your existing retry handling deal with it.

**Prove it (be exact):**

1. Register a device token from the test page.
2. In Chrome, revoke the notification permission for your site, or clear the site data. This kills
   that token.
3. Send a notification to that customer.
4. Firebase reports the token is dead. Your code marks it inactive. Send again — that token is no
   longer used.

This is the same decision you made on Day 5 with the dead-letter queue: **is this worth retrying,
or is it never going to work?** Answer it the same way here.

---

## Part 7 — Preferences and quiet hours

Not every notification is equally welcome. Split them into two kinds:

- **Order updates.** The customer asked for these by placing an order. They always go out.
- **Marketing.** Offers and promotions. These are opt-in, and the customer can switch them off.

Every notification your system sends must be marked as one or the other. There is no third kind
and no "unknown".

**Build this:**

1. A per-customer setting for whether marketing notifications are allowed. Default it to off.
2. **Quiet hours: no marketing between 22:00 and 08:00.** Order updates ignore quiet hours
   completely — an order update at 1am is exactly what the customer wants.

### The catch: whose 22:00?

Since Day 1 you have stored every time in UTC, and that was right. But quiet hours are not a UTC
question. 22:00 in Amman is not 22:00 in London. A customer in Amman must not be woken at 1am
because your server is on UTC.

So store the customer's timezone, and work out quiet hours in **their** local time.

The rule to remember:

> **Store in UTC. Decide in the customer's own time.**

Test it. Set one customer's timezone so that it is currently the middle of their night, and one
so it is the middle of their day. Send a marketing notification to both. Only one arrives. Then
send an order update to both. Both arrive.

---

## Part 8 — Write-up

`docs/day-08-notifications.md`:

- How a push notification reaches a closed app, in your own words. Whose connection is it?
- Your device registry: the table, and exactly how you handle the shared-phone case where one
  token moves from user A to user B.
- Where you put the Firebase service account file, and why not in git.
- Your router's rule, and the guess it is built on. Which mistake did you choose to make when the
  guess is wrong, and why would a customer forgive that one?
- Your answers to the five scenarios in Part 4, with a reason for each.
- The stale-connection crash from Part 3: what happened, and how you made the record trustworthy.
- The duplicate-notification test, and the difference between "exactly once" and "we never decide
  to send twice".
- The two kinds of send failure, which error you act on, and what would break if you got them the
  wrong way round.
- Your quiet-hours design, and why UTC alone was not enough.
- **What push notifications do NOT give you.** Be honest and clear. Cover at least these four:
  you cannot know it arrived; delivery can be delayed by the phone saving battery; the user can
  switch notifications off at the operating system level and you will never be told; and your
  Chrome test does not prove the fully-closed-app case.
- **A research question, answered properly.** MyThings used OneSignal and is moving to Firebase.
  Read about OneSignal and answer three things: what does it do that Firebase does not? Which of
  your tables would stop existing, because OneSignal would hold that data instead? And if you had
  to move off it in one week, what exactly would you have to rebuild?
- The hardest thing today, and how you worked it out.

---

## Definition of Done

- [ ] A Firebase project exists, and the service account JSON is loaded as a secret and is **not**
      in git.
- [ ] `DeviceToken` table with a unique constraint on the token; register and unregister endpoints
      that take the user from `ICurrentUser`, never from the body.
- [ ] The shared-phone case is handled: a token registered by a second user no longer belongs to
      the first.
- [ ] A notification sent by hand arrives in Chrome.
- [ ] A new consumer on the exchange sends order-status notifications, and neither PlaceOrder nor
      the status service was changed to make it work.
- [ ] `UserConnection` table records live connections; the crash experiment is reproduced, then
      fixed with a heartbeat and a staleness rule, and proven.
- [ ] `INotificationEngine` is the single place that decides socket or push. No consumer picks a
      channel itself.
- [ ] All five scenarios in Part 4 are classified with reasons.
- [ ] A forced redelivery produces exactly one notification.
- [ ] A dead token is detected on the correct Firebase error and deactivated; other errors do not
      deactivate anything.
- [ ] Marketing notifications respect an opt-in setting and quiet hours in the **customer's** local
      time; order updates ignore both. Both tested.
- [ ] `docs/day-08-notifications.md` complete, including the honest limits and the OneSignal
      research. Branch merged; clean clone still runs (README: how to set up a Firebase project
      and where the secret goes).

---

## Stretch goals (only if you still have fuel)

1. **Add OneSignal as a second sender.** Put both behind the same interface, and switch between
   them with one setting. Then write down what changed in your own code, and what changed in who
   holds your data.
2. **A notification history table.** Record every notification: who it went to, which channel, at
   what time, and whether the send succeeded. Then add an Admin-only endpoint to read it. Ask
   yourself what a support agent would want to see when a customer says "I never got told".
3. **Send to many people at once.** Send a promotion to 10,000 customers. Sending them one at a
   time in a loop will be far too slow — find out what Firebase offers for sending in batches, and
   measure the difference.
4. **Stop the pile-up.** A customer whose order moves through five statuses gets five
   notifications stacked on their lock screen, and only the last one matters. Find out how Firebase
   lets a new notification **replace** an older one instead of stacking, and use it so only the
   newest order update is showing.

Yesterday you learned you cannot reach someone who is not looking. Today you learned you can — as
long as you never forget that you are only asking someone else to try.
