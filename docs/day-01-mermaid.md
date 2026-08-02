```mermaid
erDiagram
    BaseEntity {
        int Id PK
        DateTime CreatedAtUtc
        DateTime UpdatedAtUtc
        bool IsDeleted
        DateTime DeletedAtUtc
    }

    Customer {
        string firstName
        string lastName
        string phoneNumber
        string email
    }

    Address {
        int customerId FK
        string street
        string city
        string zipCode
    }

    Order {
        string orderCode
        int customerId FK
        int storeId FK
        int driverId FK
        string status
        decimal subtotal
        decimal deliveryFee
        decimal total
        int paymentMethod
    }

    OrderLine {
        int orderId FK
        int productId FK
        string productName
        decimal unitPrice
        int quantity
        decimal totalPrice
        string itemNote
    }

    Product {
        int storeId FK
        int categoryId FK
        string name
        decimal price
        int stockQuantity
        bool availability
    }

    AuditTrail {
        string entityName
        string entityId
        string action
        string changesJson
        DateTime timestampUtc
        int userId
    }

    Customer ||--o{ Address : has
    Customer ||--o{ Order : places
    Driver ||--o{ Order : delivers
    Store ||--o{ StoreHour : operates_during
    Store ||--o{ Product : sells
    Store ||--o{ Order : receives
    StoreHour ||--o{ Break : has_breaks
    StoreHour ||--o{ Occasion : has_occasions
    Category ||--o{ Product : categorizes
    Product ||--o{ ProductModifier : has_modifiers
    Product ||--o{ OrderLine : contains_in
    Order ||--o{ OrderLine : has_lines
    Order ||--o{ OrderStatusHistory : tracks_status
    OrderLine ||--o{ OrderLineModifier : has_modifiers
