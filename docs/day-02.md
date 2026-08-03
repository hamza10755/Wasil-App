# Day 02: Identity, Authentication & Security Architecture

## 1. Architectural Decision: Separated Identity vs. Single-Table (Fat Model)
We evaluated two primary approaches for handling user identities and actor types (Customers, Partners, and Admins):
* **Single-Table Approach:** Storing credentials, roles, and all domain fields (delivery addresses, order history, store links) in a single large table with nullable columns.
* **Separated Identity & Domain Approach (Chosen):** Keeping a thin, dedicated `User` table strictly for authentication and authorization (IDs, contact details, password hashes, roles), linked via strict database relations to domain profiles (such as a 1-to-1 relationship with `Customer` or store management associations for `Partners`).

### Defense:
* **No Null Pollution:** Administrative and partner accounts do not require delivery addresses or customer order histories. Separating identity prevents irrelevant columns from littering non-customer rows as `NULL`.
* **Clean Separation of Concerns:** Security credentials and authentication mechanics are isolated from business logic and domain entities.
* **Extensibility:** Adding new actor roles (e.g., Drivers or Support Agents) requires introducing new lightweight domain tables rather than altering core authentication structures.

---

## 2. Primary Key Type Strategy (`Guid` vs. `int`)
* **Decision:** The `User` entity uses a `Guid` as its primary key.
* **Defense:** Integer IDs (`int`) are sequential and predictable, making them vulnerable to ID enumeration attacks where malicious actors can guess or scrape user records by incrementing IDs in endpoints. Using `Guid` ensures unpredictable, collision-resistant identifiers across distributed environments and secures token claims.

---

## 3. Password Hashing Mechanism
* **Decision:** Implementation of ASP.NET Core's built-in `IPasswordHasher<User>` (utilizing PBKDF2 with HMAC-SHA256, salted hashes, and high iteration counts).
* **Defense:** Rolling custom cryptographic hashing implementations introduces vulnerabilities. ASP.NET Core's built-in provider is battle-tested, automatically handles unique per-user salts, and provides robust defense against brute-force and rainbow table attacks. Customer accounts rely entirely on phone + OTP authentication (requiring no password hash), while staff (Admins/Partners) use securely hashed credentials.

---

## 4. Secret Management & JWT Security
* **Decision:** JWT signing keys, issuers, and audiences are decoupled from source code and managed via **.NET User Secrets** during development (and environment variables in production).
* **Defense:** Committing symmetric signing keys to version control is a critical security risk. By enforcing external configuration retrieval via `builder.Configuration`, the application maintains a strict security boundary. Furthermore, `TokenValidationParameters` explicitly enforce issuer, audience, lifetime validation, and zero `ClockSkew` to ensure tokens expire precisely on schedule.