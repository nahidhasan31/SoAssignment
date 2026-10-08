namespace MaltaShop.Contracts;

// The contract between services, versioned by namespace. Every message is an
// immutable record: a consumer cannot change what a producer sent, and adding an
// optional member is a backward-compatible change the consumers' tests prove.

public record OrderLine(string Sku, int Quantity);

/// <summary>Raised by the Order service when a customer's order has been accepted.</summary>
public record OrderSubmitted
{
    public required Guid OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required long TotalCents { get; init; }
    public string Currency { get; init; } = "EUR";
    public required IReadOnlyList<OrderLine> Lines { get; init; }
    public required string CorrelationId { get; init; }
}

// ---------------------------------------------------------------- commands
// Commands are sent to one endpoint and name an action. The process manager is
// the only thing that sends them.

public record ReserveStock
{
    public required Guid OrderId { get; init; }
    public required IReadOnlyList<OrderLine> Lines { get; init; }
    public required string CorrelationId { get; init; }
}

public record ReleaseStock
{
    public required Guid OrderId { get; init; }
    public required string CorrelationId { get; init; }
}

public record AuthorisePayment
{
    public required Guid OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required long AmountCents { get; init; }
    public required string CorrelationId { get; init; }
}

// ------------------------------------------------------------------ events
// Events are published and name something that has happened. Anyone may subscribe.

public record StockReserved
{
    public required Guid OrderId { get; init; }
    public required string CorrelationId { get; init; }
}

public record StockRejected
{
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
    public required string CorrelationId { get; init; }
}

public record StockReleased
{
    public required Guid OrderId { get; init; }
    public required string CorrelationId { get; init; }
}

public record PaymentAuthorised
{
    public required Guid OrderId { get; init; }
    public required string PspReference { get; init; }
    public required string CorrelationId { get; init; }
}

public record PaymentDeclined
{
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
    public required string CorrelationId { get; init; }
}

public record OrderConfirmed
{
    public required Guid OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required long TotalCents { get; init; }
    public required int LineCount { get; init; }
    public required string CorrelationId { get; init; }
}

public record OrderCancelled
{
    public required Guid OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required string Reason { get; init; }
    public required string CorrelationId { get; init; }
}
