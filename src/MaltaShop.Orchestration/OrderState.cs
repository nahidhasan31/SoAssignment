using MassTransit;

namespace MaltaShop.Orchestration;

/// <summary>
/// The saga's persisted instance: one row per order, holding where the process has
/// reached and the few facts the later steps need. It is deliberately small - the
/// process manager coordinates, it does not own the order.
/// </summary>
public class OrderState : SagaStateMachineInstance
{
    /// <summary>The order id. Using it as the correlation id means every message
    /// about an order finds this row without a lookup table.</summary>
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = null!;
    public string CustomerId { get; set; } = string.Empty;
    public long TotalCents { get; set; }
    public int LineCount { get; set; }
    public string BusinessCorrelationId { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public string? PspReference { get; set; }

    /// <summary>Every state the instance has passed through, for support and audit.</summary>
    public string Path { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
