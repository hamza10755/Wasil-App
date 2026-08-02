# Day 2 — Identity, Authorization, and Breaking Into Your Own System

## Where we are

On Day 1 you built an open platform. Anyone who knows the URL can place an order as
any customer, edit any store's products, and read any customer's order history. That
is not a product — it's a data breach with a Swagger page.

Today you fix that. You turn the platform into a real multi-actor system where every
caller must **prove who they are** and is allowed to touch **only what belongs to
them**. Then — and this is the part that makes today harder than yesterday — you become
the attacker. You try to break into your own API, write down every attack, and prove
each one fails. Any attack that succeeds is a bug you fix before the day is done.

Security is unforgiving in a way features are not. A feature that works 95% of the
time is "mostly done." An authorization check that works 95% of the time is a breach.
Today, "it works when I test the happy path" earns you nothing.

**Everything builds on your Day 1 repo. Same branch discipline: `day-02-identity`.**

---

## Ground rules (still in force from Day 1)

Thin controllers, DTOs everywhere, one config class per entity, clean clone, defend
every library choice, 45-minute troubleshooting log. Plus one new rule for today:

**Never trust the client. Ever.** Not the request body, not the URL, not the headers,
not even a value that came from a token you issued. Every "who are you / what are you
allowed to do" decision is made on the server against the database — never by believing
what the caller sent you. If you find yourself thinking "but the frontend already
checks that," stop: the frontend is the client, and the client is the attacker.

---

## Part 0 — Design the actors first (write before you code)

A delivery platform has more than one kind of human. Define these three roles:

- **Customer** — signs themselves up, places orders, sees their own orders and
  addresses. Nothing else.
- **Partner** — operates exactly one store. Manages that store's products, sees and
  progresses that store's orders, sees that store's dashboard. Cannot see or touch any
  other store, and cannot see the platform as a whole. (This is the vendor/store owner —
  the same "Partner" you know from MyThings.)
- **Admin** — the platform operator. Can do everything, across every store and every
  customer.

Write `docs/authorization-matrix.md`: a table with every operation from Day 1 (plus
today's new ones) down the side, and the three roles across the top. In each cell put
one of: **All** (any row), **Own** (only their own rows), or **—** (forbidden). This
matrix is the specification for Part 4. If it's vague, your code will be vague. Build
the matrix first, then make the code obey it exactly.

Examples to get you started (you complete the rest):

| Operation | Customer | Partner | Admin |
|---|---|---|---|
| Place order | Own | — | — |
| View order details | Own | Own store's | All |
| Update order status | — | Own store's | All |
| Create/edit product | — | Own store's | All |
| Store dashboard | — | Own store's | All |
| View any customer's profile | Own | — | All |

---

## Part 1 — Identity foundation

### One identity, more than one way to prove it (the idea the day turns on)

Not everyone logs in the same way. In our world:

- **Customers** sign in with their **phone number and a one-time code (OTP)** — no
  password at all. This is how customers here actually log in; email is rare for them.
- **Partners and Admins** (and later Drivers) sign in with **email + password**.

Here is the insight: *how* someone proves who they are (**authentication**) is a
separate thing from *who they are and what they may do* (**identity + authorization**).
Two different front doors, but once inside, everyone is the same kind of citizen — one
`User`, one role, one kind of token, one set of rules. If you end up building two
parallel worlds that each track "who is logged in" their own way, you've designed it
wrong. Both doors lead into the same room.

### The user model (a real modeling decision — defend it)

Introduce a `User` — the **identity** record: how someone logs in and the role they hold.
A **customer** User has a **phone** (unique) and **no password**. A **staff** User
(Partner / Admin) has an **email** (unique) and a **password hash**, and needs no phone.
All roles authenticate through this **one `User` table** — one login system, not three.

Rules the model must satisfy:

- Everyone logs in through the single `User` table — customers, Partners, and Admins are
  all rows in that one table. Do not make a separate login table per role; they drift
  apart.
- Phone is unique among the users that have one; email is unique among the users that
  have one.
- A Partner belongs to exactly one Store (a required link from partner to store). Whether
  a single Store can have several Partners (an owner plus staff) or exactly one is your
  call — decide it and defend it. An Admin belongs to no store.
- Each customer has exactly one `User` row, and each customer `User` belongs to exactly
  one customer — no shared logins, and no single person showing up as two customers.

**Now the decision you must make and defend — the spicy one.** Logging in (`User`:
credentials, role) is one job. Being a *customer* (addresses, orders, order history —
your Day 1 `Customer`) is a different job. Do those two jobs live in **one table** (a
single `User` with a role/type enum, customer fields sitting nullable on it) or in
**two** (a thin `User` for identity, linked one-to-one to a rich `Customer` for the
domain)? Reason it through before you answer:

- A Partner and an Admin are not customers — no delivery address, no order history. In a
  one-table design, what are those columns doing on their rows?
- Your Day 1 `Order` and `Address` already point at a customer. Should your business
  domain hang off the login table, or off a domain table?
- When you authenticate, how much unrelated domain data loads with the identity — and the
  other way around?

Careful: "one login table" and "one table for everything" are **not** the same question —
you can have a single `User` table AND a separate `Customer` table. Pick a shape, draw it,
and defend it against the other. "It compiled" is not a defense.

### Your UserId type is a decision, not a default

Pick the primary key type for `User` — `long` or `Guid` — and **defend it in your
write-up**. Before you pick, think about this: if user ids are `1, 2, 3, 4...`, and an
endpoint ever leaks by id, an attacker can walk the entire user base by counting up.
Where have you seen this exact reasoning before this week? (Look at your own OrderCode.)
Whatever you choose, your Day 1 `AuditTrail.UserId` column — the nullable one you
stubbed and left empty — changes type to match, because today it stops being empty.

### Passwords (staff only)

Only staff have passwords, so this is about Partner/Admin accounts. Never store a
password. Store a hash produced by a real password-hashing algorithm (research: bcrypt,
Argon2, PBKDF2 — ASP.NET Core ships a password hasher that does one of these). In your
write-up, name the algorithm and explain, in one sentence each: what a **salt** is and
why it matters, and why a good password hash is **deliberately slow**. Hold onto that
last fact — it has a consequence for seeding you'll meet in Part 5.

### Build the auth here — libraries welcome, black boxes not

You are building authentication and authorization into *this* project. Use a
well-established library for the security primitives — ASP.NET Core's password hasher,
the JWT bearer middleware, and the like. Do **not** hand-roll password hashing or
token-signature validation; rolling your own crypto is one of the most reliable ways to
ship a hole.

But "I used a library" is not the same as "it's secure." Whatever you pull in, you must:
- configure it to meet every requirement in this document — short-lived tokens, full
  token validation, the OTP rules, the ownership checks. The defaults will not give you
  these.
- be able to explain what each piece does and why. If any part is magic to you, you are
  not done. Part 6's attack log is where black-box assumptions go to die.

If you have auth code from an earlier exercise, you may borrow from it — but it must
satisfy everything here (it likely used email + password for everyone and had no OTP), so
read before you copy. Fresh or reused, you own and defend every line.

---

## Part 2 — Issuing and proving identity (two front doors, one token)

You have two login flows. They look different at the door and end **identically**: both
produce the same access token + refresh token. Everything after login is blind to how
the caller got in.

### Flow A — Customer: phone + OTP

1. **Request OTP** — public. Input: a phone number. Generate a 6-digit code, store it
   server-side against that phone with a short expiry (e.g. 5 minutes), and "send" it
   (see the sender note). The response is a flat "if that number is valid, a code was
   sent" — it must NOT reveal whether the phone belongs to a known customer, and it must
   NOT contain the code.
2. **Verify OTP** — public. Input: phone + code. If the code is correct, unexpired, and
   unused → issue tokens. The code is **single-use**: consume it so it can never work
   twice. Whether a brand-new phone creates the customer here or needs a separate
   register step is your design decision — defend it.

The OTP rules are not decoration; they are the entire security of this door:
- 6 digits is only a million possibilities — guessable fast. So: **limit attempts**
  (a few wrong tries kills that code), **expire quickly**, and keep each code
  **single-use**.
- Generate the code with a **cryptographically secure** random source, never `Random`.
- **Throttle requests**: someone hammering "request OTP" at a victim's number floods
  their phone and burns your SMS budget. Rate-limit per phone.
- You need somewhere to keep the codes that gives fast lookup, automatic expiry, and an
  easy way to mark one as used — you already know a tool with expiry built in (Redis).
  Use it, or defend your alternative.

**The SMS sender.** You have no real SMS gateway and don't need one. Put sending behind
an interface (`ISmsSender`) with one implementation for now that writes the code to the
logs — plainly a development stand-in — so a real provider could drop in later without
touching login logic. The code must never appear in an API response and never be logged
in production. (Note for later: "send a message to a human" is a textbook job for a
background queue — remember this when we reach RabbitMQ.)

### Flow B — Staff: email + password

3. **Login** — public. Input: email + password. Correct → tokens. Wrong → one generic
   "invalid credentials" error. Do NOT reveal whether the email or the password was
   wrong, or whether the email exists at all. Think about why.

### Shared by both flows

4. **Refresh** — trades a valid, unexpired, unrevoked refresh token for a new access
   token (and a rotated refresh token — see Part 5).
5. **Logout** — revokes the current refresh token so it can never be used again.
6. **Me** — protected. Returns the current user's identity as the server understands it.

### What goes in the access token

Claims: the user id (`sub`), the role, and for a Partner, their `storeId`. Nothing about
*how* they logged in — a token from the OTP door and a token from the password door are
indistinguishable and equally powerful within their role.

Know this cold, because it drives a decision: **a JWT is signed, not encrypted.** Anyone
holding the token can read every claim in it (it's just base64 — paste one into jwt.io
and see). The signature stops them *changing* claims, not *reading* them. Therefore: no
secret, no password, no OTP, nothing sensitive ever goes in a claim.

Configure token validation properly: signature, issuer, audience, and expiry all
verified. Your signing secret is a real secret — it does **not** belong in a file you
commit to git. State in your write-up where you put it and why.

### Provisioning the privileged roles

Partners and Admins are **not** self-service. There is no public "register as a partner
or admin" endpoint — that would be an open door. Instead:

7. **Admin creates a Partner** — protected, Admin only. Creates a staff User with the
   Partner role and an email, tied to a given store, plus a way to set the first
   password.

Seed exactly one Admin in your seeder so the system is usable from a cold start. Every
other privileged account is created through the protected endpoint above.

---

## Part 3 — The current-user service (this is the spine of the day)

Almost every rule today depends on one question: **who is making this request?** You do
not want that answer dug out separately in twenty different places, each its own way. You
want **one** trustworthy place that answers it, and everything else asks that place.

Build an `ICurrentUser` service that exposes the authenticated caller — their user id,
role, and (for partners) store id. It reads that identity **only** from the token the
auth middleware already validated — never from the request body, a query parameter, or a
header you parse yourself. Those all come from the client, and the client is the attacker
(today's ground rule). The validated token is the single source of truth for "who".

Inject `ICurrentUser` wherever a decision depends on who is asking. **Controllers and
services never touch `HttpContext` directly** — they depend on `ICurrentUser`. Why add
the extra layer? Because it gives you one *seam*: a single place that knows how the
current user is found. That keeps the logic easy to follow, and makes it easy to hand the
code a *fake* user in a test later (you will thank yourself in testing week).

Then wire `ICurrentUser` into the **audit interceptor from Day 1**. Every insert /
update / soft-delete now records *who did it* in `AuditTrail.UserId`. Yesterday that
column was always empty; today it is filled on every change — except where there
genuinely is no user (the seeder, below).

**Two hard spots. These are the real lesson of this part, not footnotes:**

- **Getting the current user into the interceptor safely.** Here is the tension. The
  audit interceptor is built once and lives a long time — it is wired into the database
  plumbing, not created fresh for each request. But "the current user" is different for
  every request, and many requests run at the *same moment*. So if the interceptor reaches
  for the user the wrong way, it can grab one request's user and quietly reuse it for a
  *different* request running right beside it — stamping the wrong person onto the audit
  row. The trap: this usually looks flawless while you test alone (one request at a time)
  and only breaks under real, concurrent traffic — silently. Research **service lifetimes
  in ASP.NET Core** ("singleton" vs "scoped") and how a long-lived object can safely reach
  a value that belongs to a single request. Get it right, and in your write-up explain how
  you *know* it can never grab the wrong user. "It worked when I tried it" is not knowing.
- **The seeder has nobody logged in.** When your seeder runs there is no web request and
  no signed-in user — so "who is the current user?" has no real answer. `ICurrentUser`
  must handle that without crashing and without inventing a fake person. Decide what "who
  did it" means for data the system creates itself (seeding today; background jobs in a
  couple of weeks), and apply that choice consistently.

---

## Part 4 — Authorization: two layers, both required

Retrofit **every** endpoint from Day 1 plus today's, so they obey your Part 0 matrix.
There are two distinct layers, and you need both — one is not a substitute for the other.

### Layer 1 — Authentication + role (coarse)

Every endpoint except the public auth ones (request-OTP, verify-OTP, staff login,
refresh) requires a valid token. Then role gates the coarse question "is this kind of
user even allowed to call this operation?" — e.g. only a Partner or Admin may update
order status; only an Admin may create a Partner.

### Layer 2 — Ownership (fine — this is the one people get wrong)

A valid token and the right role are **not enough**. Customer #12, logged in with a
perfectly valid token, must not be able to read Customer #13's order — same role, same
endpoint, different owner. A Partner for Store #4 must not edit Store #7's product,
even though "a Partner may edit products" is true in general.

This means: for every operation marked **Own** in your matrix, after you know who the
caller is, you check on the server that the specific row they're touching actually
belongs to them, and return **403 Forbidden** if not. This check is made against the
database, not against anything the client sent.

One trap to think hard about: for a Partner you have their `storeId` in the token.
It is tempting to trust it forever. What happens the day an admin moves a Partner to a
different store, or removes them — while their old token is still valid for 15 minutes?
Decide how much you trust a claim versus re-checking the database, and defend it.

Do not scatter copy-pasted ownership checks across 15 endpoints. Find one clean place
and shape for this logic. Ad-hoc `if (order.CustomerId != currentUserId) return 403;`
pasted everywhere is how one forgotten paste becomes a breach.

### Correct failures

- No token / bad token / expired token → **401 Unauthorized**.
- Valid token, but this role/owner may not do this → **403 Forbidden**.
- Asking for a row that doesn't exist → **404** — but think about whether "not yours"
  should look different from "doesn't exist" to an attacker probing for what exists.

---

## Part 5 — Refresh tokens done properly

Access tokens are short-lived on purpose, so a leaked one dies fast. Refresh tokens are
how a user stays logged in without re-typing their password — which makes them valuable,
so treat them carefully:

- Stored server-side (a table, or Redis — your call, defend it) so they can be revoked.
- **Rotation:** every refresh issues a new refresh token and invalidates the old one.
- **Reuse detection:** if a refresh token that was already used (already rotated away)
  is presented again, that's a red flag — someone is replaying a stolen token. The right
  response is aggressive: revoke the whole chain for that user so both the attacker and
  the victim are logged out. Implement this.
- Logout revokes the current refresh token.

### The seeding consequence of slow hashing

Remember password hashes are deliberately slow. Now count who actually has a password:
only staff — a handful of Partners and one Admin. Your ~20,000 customers have **no
password** (they use OTP), so there's almost nothing to hash and seeding stays fast.

Here's the trap. If your reseed suddenly crawls, that's a signal you modeled customers as
having passwords and are now hashing 20,000 of them at a real work factor — which would
wreck your Day 1 "under 3 minutes." That slowdown is your model telling you it's wrong:
correct model → fast seed. If for any reason you did give seed customers a credential,
explain how you kept it fast without weakening a single real login. (OTP codes are never
seeded — they're short-lived and created on demand.)

---

## Part 6 — Attack your own API (the centerpiece — do not skip)

Now you switch hats. You are no longer the developer; you are someone trying to break in.
Create `docs/day-02-attack-log.md`. For each attack below: write what you sent, what you
got back, and a verdict — **BLOCKED** (good) or **GOT IN** (a bug — fix it, then re-run
and update the log). Use your `requests.http` or a tool like Postman; capture real
responses, not "I think it would return 401."

1. Call a protected endpoint (e.g. order details) with **no token**. Expect 401.
2. Call it with a **garbage token** (`Bearer abc.def.ghi`). Expect 401.
3. Log in as Customer A. Use A's valid token to fetch **Customer B's order** by id.
   Expect 403. If you get B's data — that's the single most common real-world API breach.
   Fix it before moving on.
4. Log in as the Partner of Store 1. Try to **edit a product of Store 2**. Expect 403.
5. Take a valid token and **tamper with a claim** — flip your role to `Admin`, or change
   the user id — without re-signing. Expect 401 (signature check catches it).
6. Research the **`alg: none` attack** (an attacker sets the token's algorithm header to
   "none" to skip signature checking). Craft one and fire it. Expect rejection. If your
   validation accepts it, you have a critical hole — understand why and fix it.
7. Let an access token **expire**, then call a protected endpoint. Expect 401. Then use
   **refresh** to get a new one and succeed.
8. **Reuse a refresh token** you already rotated away. Expect rejection and the whole
   chain revoked (Part 5).
9. **Brute-force an OTP.** Request a code for a phone, then submit wrong codes in a loop.
   You must be blocked long before you could try a meaningful fraction of the million
   possibilities — by attempt-limiting AND by expiry. If you can keep guessing, the OTP
   door is wide open, and a million tries is nothing to a script.
10. **Reuse a spent OTP.** Verify successfully, then submit the same code again. Expect
    rejection — it must be single-use.
11. **Flood OTP requests.** Call request-OTP for one phone many times fast. You should be
    throttled; otherwise you're a free tool for spamming someone's phone and burning SMS
    cost.
12. **Probe for accounts.** Does request-OTP (or verify, or staff login) answer
    differently for a real vs a made-up phone/email — different message, different timing,
    different status? Any visible difference lets an attacker map out who has an account.
    All of them should stay tight-lipped.

Every **GOT IN** that you found and fixed is worth more than any feature you shipped
today. List them proudly in the write-up — finding your own holes is the skill.

---

## Part 7 — Write-up and peer attack

### `docs/day-02.md`
- Your final identity model diagram (how User, Customer, Store relate, and how one table
  holds both phone-only customers and email+password staff) and why that shape beat the
  alternatives.
- Your UserId type decision and its security reasoning.
- Password algorithm (staff); one line each on salt and on why-slow.
- Your OTP design: length, expiry, attempt limit, where you stored codes, how you stop
  brute-force and request-flooding — and why the code never leaves the SMS sender.
- Where the signing secret lives and why not in git.
- How you wired `ICurrentUser` into the audit interceptor without the scoped/singleton
  trap, and how you know the audited user is never wrong.
- Why seeding stayed fast (hint: who actually has a password to hash).
- Your one clean home for ownership checks, and how much you trust the token's storeId
  claim vs the database.
- The attack log summary: how many GOT INs you found and killed.
- Hardest problem of the day and how you cracked it.

### Peer attack (you are each other's pen-testers)
Swap running APIs (or repos) with the other trainee. Using the Part 6 attack list, spend
30–45 minutes trying to break into **their** system, and let them try to break yours.
Anything you get into, write up as a short report for them — endpoint, what you sent,
what you got, and which layer of defense was missing. Getting into a peer's system is not
a win against them; it's the most useful gift you can give them today. Fix whatever they
find in yours.

---

## Definition of Done

- [ ] `docs/authorization-matrix.md` complete; code obeys it exactly.
- [ ] User model implemented and defended; one table holds phone-only customers and
      email+password staff; Customer/Store/User relationships enforced.
- [ ] Staff passwords stored only as proper salted hashes; algorithm named in write-up.
- [ ] Customer OTP login works: codes are cryptographically random, short-lived,
      single-use, attempt-limited, request-throttled, stored server-side, and never
      returned in a response.
- [ ] Both login flows issue the same short-lived access + server-stored refresh token;
      claims correct and contain nothing sensitive.
- [ ] Customer phone/OTP flow public; Partner creation Admin-only; one Admin seeded.
- [ ] `ICurrentUser` is the only source of caller identity; no `HttpContext` in services.
- [ ] Audit trail now records the acting user on every change; correct under concurrent
      requests; sane for seeded/system data.
- [ ] Every endpoint enforces BOTH role and ownership per the matrix; 401 vs 403 correct.
- [ ] Refresh rotation + reuse-detection + logout revocation all work.
- [ ] Seeding still finishes near the Day 1 target (customers have no password to hash).
- [ ] `docs/day-02-attack-log.md` shows all 12 attacks BLOCKED (after fixes).
- [ ] Peer attack done both directions; findings fixed; `docs/day-02.md` complete.
- [ ] Branch merged, clean clone still runs (README updated: how each role logs in, the
      seeded admin's credentials, and how to grab a customer's OTP from the logs).

---

## Stretch goals (only if you still have fuel)

1. **Franchise scope + a CallCenter role (the big one).** Real stores come in franchises.
   Add a self-reference to `Store`: `BranchOfId` is null for a main store (the franchise
   head) and points at the main store for a branch (one level deep — a branch never has
   its own branches). Then add a **CallCenter** role, scoped to one franchise, that may
   act on every store in that franchise — the main store plus all its branches — and
   nothing in any other franchise.
   The catch is the lesson: your Partner ownership check is probably
   `resource.storeId == myStoreId`. That breaks for CallCenter, whose scope is a *set* of
   stores, not one. Refactor the check so scope becomes "the set of stores I may act on —
   is the target inside it?" The franchise of any store S is `S.BranchOfId ?? S.Id` (its
   main id), and the set is that main plus every store whose `BranchOfId` equals it. Seed
   a few real franchises (mains with branches), and add attacks: a CallCenter for
   franchise A must be blocked (403) from franchise B's stores and orders.
2. **Staff account lockout** — after N failed password attempts, lock a staff account
   for a cooldown. (Customer OTP attempt-limiting is already required in Part 2; this is
   its password-side cousin.)
3. **Redis-backed storage** — you know Redis; use it for the OTP codes and/or the refresh
   revocation list, and explain the trade-offs — including what happens if Redis is
   down. Can customers still log in? Should they be able to?
4. **A Driver role** — staff (email + password) who can see only orders assigned to them
   and can only move a status from OutForDelivery to Delivered. Extend the matrix and
   enforce it.
5. **Refresh-token binding** — tie a refresh token to a device/user-agent fingerprint so
   a stolen token used from a different client is rejected.

Lock every door. Then go around the building trying every one to be sure.
