# Day 2 — Attack Log

This document logs the results of the security penetration testing performed against the Wasil API endpoints. Every attack was executed using `security_attacks.http`.

| # | Attack Scenario | Request Details | Observed Response | Verdict |
|---|---|---|---|---|
| 1 | Call protected endpoint with **no token** | `PUT /api/v1/customers/1` without `Authorization` header | `401 Unauthorized` | **BLOCKED** |
| 2 | Call protected endpoint with **garbage token** | `GET /api/v1/orders/1` with `Authorization: Bearer abc.def.ghi` | `401 Unauthorized` | **BLOCKED** |
| 3 | Fetch **Customer B's order** as Customer A | `GET /api/v1/orders/2` with Customer A's JWT token | `403 Forbidden` | **BLOCKED** |
| 4 | **Edit Store 2 product** as Partner of Store 1 | `PUT /api/v1/products/200` with Store 1 Partner's JWT token | `403 Forbidden` | **BLOCKED** |
| 5 | **Tampered token claims** (e.g. role flipped to Admin) | Token with tampered payload and unmodified signature | `401 Unauthorized` (Signature validation failure) | **BLOCKED** |
| 6 | **`alg: none` attack** | JWT header set to `"alg": "none"` to bypass validation | `401 Unauthorized` (Signature required validation) | **BLOCKED** |
| 7 | **Expired access token** call + refresh rotation | Call with expired access token → `401`. Trade refresh token for new access token → succeeds | Access: `401 Unauthorized`. Refresh: `200 OK` (New tokens issued) | **BLOCKED** |
| 8 | **Reuse rotated refresh token** | Re-authenticating using a refresh token that has already been rotated out | `401 Unauthorized` and all refresh tokens revoked for safety | **BLOCKED** |
| 9 | **Brute-force OTP guesses** | Sending 30 concurrent incorrect OTP validation guesses fast | First 3 attempts: `401 Unauthorized`. Subsequent attempts: `429 Too Many Attempts` | **BLOCKED** |
| 10 | **Reuse spent OTP** | Submitting the same OTP code again after successful verification | `401 Unauthorized` (OTP key deleted on first use) | **BLOCKED** |
| 11 | **Flood OTP requests** | Hammering the OTP generation endpoint for the same phone number | First request: `200 OK`. Subsequent requests under 60s: `429 Too Many Requests` | **BLOCKED** |
| 12 | **Probe for accounts** | Observing API responses/timing for known vs unknown phone numbers | `200 OK` + `"message": "OTP sent successfully."` returned for both known and unknown numbers (No email/phone lookup leaked) | **BLOCKED** |
