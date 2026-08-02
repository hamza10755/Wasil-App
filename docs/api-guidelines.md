# API Design Guidelines - Wasil Delivery Platform

This document outlines the API design standards for the Wasil project to ensure consistency, predictability, and maintainability across all endpoints.

---

## 1. REST Style & Resource Naming
We strictly follow **Resource-Oriented REST** conventions rather than RPC/Action styles.
* URLs represent nouns in the plural form (e.g., `customers`, `orders`, `products`).
* Actions are implied by the HTTP methods.
* **Bad**: `POST /api/Product/Add`
* **Good**: `POST /api/v1/products`

---

## 2. API Versioning
All endpoints must be versioned to prevent breaking changes for downstream applications (such as mobile clients).
* Use URL-based versioning prefixed with `v1`.
* **Pattern**: `/api/v1/[resource]`
* **Example**: `/api/v1/customers`

---

## 3. HTTP Methods
Always use the correct HTTP verb for the operation:
* `GET`: Retrieve resources. Safe and idempotent (must not alter database state).
* `POST`: Create new resources.
* `PUT`: Replace or update an existing resource.
* `PATCH`: Partially update a resource.
* `DELETE`: Remove a resource (converted to soft-delete internally).

---

## 4. HTTP Status Codes
Return uniform status codes to indicate operation outcomes:
* `200 OK`: Successful retrieval or update.
* `201 Created`: Successful creation (e.g., creating a customer or address).
* `204 No Content`: Successful request where no response body is returned (e.g., deletion).
* `400 Bad Request`: Validation failure or bad input format (regex violations, bad parameters).
* `404 Not Found`: Requesting a resource that does not exist.
* `409 Conflict`: Business rule violation or unique constraint collision (e.g., duplicate emails).
* `500 Internal Server Error`: Unhandled system exceptions.

---

## 5. Paging Envelope
Any endpoint returning a paginated list must wrap items in a uniform envelope to provide metadata to the client:
```json
{
  "items": [ ... ],
  "totalCount": 150,
  "page": 1,
  "pageSize": 10
}
```
* Query parameters for pagination must be named: `?page=1&pageSize=10`.

---

## 6. Error Response Schema
When a request fails, the API must return a structured error body so that clients can display meaningful messages.
```json
{
  "error": "Detailed error message describing the failure."
}
```
* **Validation errors**: For inputs that fail validation, return a `400 Bad Request` with fields indicating exactly what failed.

---

## 7. Concrete Endpoint Examples (For Testing)

### Create Customer
* **Method**: `POST`
* **URL**: `/api/v1/customers`
* **Request Body**:
  ```json
  {
    "firstName": "John",
    "lastName": "Doe",
    "email": "john.doe@example.com",
    "phoneNumber": "0791234567"
  }
  ```
* **Expected Response (`201 Created`)**:
  ```json
  {
    "id": 1,
    "firstName": "John",
    "lastName": "Doe",
    "email": "john.doe@example.com",
    "phoneNumber": "0791234567"
  }
  ```

### Add Address
* **Method**: `POST`
* **URL**: `/api/v1/customers/1/addresses`
* **Request Body**:
  ```json
  {
    "street": "123 Main St",
    "city": "Amman",
    "zipCode": "11118",
    "isDefault": true
  }
  ```
* **Expected Response (`201 Created`)**:
  ```json
  {
    "id": 1,
    "customerId": 1,
    "street": "123 Main St",
    "city": "Amman",
    "zipCode": "11118",
    "isDefault": true
  }
  ```

### Retrieve Order Details
* **Method**: `GET`
* **URL**: `/api/v1/orders/14`
* **Expected Response (`200 OK`)**:
  ```json
  {
    "id": 14,
    "orderCode": "260791489001",
    "status": "Pending",
    "paymentMethod": "Cash",
    "subtotal": 25.50,
    "deliveryFee": 3.00,
    "total": 28.50,
    "createdAtUtc": "2026-07-27T11:00:00Z",
    "customerName": "John Doe",
    "storeName": "Supermarket Express",
    "deliveryAddress": "123 Main St, Amman, 11118",
    "lines": [
      {
        "id": 5,
        "productId": 22,
        "productName": "Fresh Milk 1L",
        "unitPrice": 1.50,
        "quantity": 2,
        "totalPrice": 3.00
      }
    ],
    "statusHistory": [
      {
        "oldStatus": "Pending",
        "newStatus": "Pending",
        "timestampUtc": "2026-07-27T11:00:00Z"
      }
    ]
  }
  ```

### Get Customer Order History (Paged)
* **Method**: `GET`
* **URL**: `/api/v1/orders/customer/1?page=1&pageSize=5`
* **Expected Response (`200 OK`)**:
  ```json
  {
    "items": [
      {
        "orderCode": "260791489001",
        "date": "2026-07-27T11:00:00Z",
        "status": "Pending",
        "storeName": "Supermarket Express",
        "lineCount": 1,
        "total": 28.50
      }
    ],
    "totalCount": 1,
    "page": 1,
    "pageSize": 5
  }
  ```