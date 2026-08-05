# Authorization Matrix

| Operation | Customer | Partner | Admin |
|---|---|---|---|
| **Catalog** | | | |
| List stores | All | All | All |
| Store details | All | All | All |
| Search products | All | All | All |
| Create/edit product | — | Own store's | All |
| **Customers** | | | |
| Create customer | All (Public) | — | All |
| Update customer | Own | — | All |
| Add address | Own | — | All |
| List addresses | Own | — | All |
| View any customer's profile | Own | — | All |
| **Orders** | | | |
| Place order | Own | — | — |
| View order details | Own | Own store's | All |
| Customer order history | Own | — | All |
| Update order status | — | Own store's | All |
| **Reporting** | | | |
| Store dashboard | — | Own store's | All |
| **Identity & Authentication** | | | |
| Request OTP | Public | — | — |
| Verify OTP | Public | — | — |
| Staff Login | — | Public | Public |
| Refresh Token | Own | Own | Own |
| Logout | Own | Own | Own |
| Get Current User (Me) | Own | Own | Own |
| Create Partner | — | — | All |