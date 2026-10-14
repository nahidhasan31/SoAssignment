using MaltaShop.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace MaltaShop.Orchestration;

/// <summary>
/// The place-order process manager, declared as a state machine rather than
/// written as procedural code.
///
/// MassTransit supplies the machinery this relies on: correlation by order id,
/// a persisted instance per order, pessimistic row locking so two messages for the
/// same order cannot interleave, retry and redelivery policies on the endpoint,
/// and a transactional outbox so a state change and the message it causes commit
/// together. What is left here is the business sequence, and nothing else.
///
///   Reserving --StockReserved--> Authorising --PaymentAuthorised--> Confirmed
///       |                            |
///       | StockRejected              | PaymentDeclined -> ReleaseStock
///       v                            v
///    Rejected                    Compensating --StockReleased--> Cancelled
/// </summary>
public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State Reserving { get; private set; } = null!;
    public State Authorising { get; private set; } = null!;
    public State Compensating { get; private set; } = null!;
    public State Confirmed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;
    public State Rejected_ { get; private set; } = null!;

    public Event<OrderSubmitted> Submitted { get; private set; } = null!;
    public Event<StockReserved> Reserved { get; private set; } = null!;
    public Event<StockRejected> Rejected { get; private set; } = null!;
    public Event<PaymentAuthorised> Authorised { get; private set; } = null!;
    public Event<PaymentDeclined> Declined { get; private set; } = null!;
    public Event<StockReleased> Released { get; private set; } = null!;

    private static readonly Uri InventoryQueue = new("queue:maltashop-inventory-commands");
    private static readonly Uri PaymentQueue = new("queue:maltashop-payment-commands");

    public OrderStateMachine(ILogger<OrderStateMachine> logger)
    {
        InstanceState(x => x.CurrentState);

        // Every message carries the order id, so the instance is found without a
        // correlation table and a message for an unknown order is discarded.
        Event(() => Submitted, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => Reserved, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => Rejected, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => Authorised, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => Declined, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => Released, x => x.CorrelateById(m => m.Message.OrderId));

        Initially(
            When(Submitted)
                .Then(context =>
                {
                    context.Saga.CustomerId = context.Message.CustomerId;
                    context.Saga.TotalCents = context.Message.TotalCents;
                    context.Saga.LineCount = context.Message.Lines.Count;
                    context.Saga.BusinessCorrelationId = context.Message.CorrelationId;
                    context.Saga.StartedAt = DateTime.UtcNow;
                    Record(context.Saga, "Reserving");
                    logger.LogInformation("{Event} order={OrderId} total={TotalCents}",
                        "saga.started", context.Message.OrderId, context.Message.TotalCents);
                })
                .Send(InventoryQueue, context => new ReserveStock
                {
                    OrderId = context.Message.OrderId,
                    Lines = context.Message.Lines,
                    CorrelationId = context.Message.CorrelationId,
                })
                .TransitionTo(Reserving));

        During(Reserving,
            When(Reserved)
                .Then(context =>
                {
                    Record(context.Saga, "Authorising");
                    logger.LogInformation("{Event} order={OrderId}", "saga.stock_reserved", context.Message.OrderId);
                })
                .Send(PaymentQueue, context => new AuthorisePayment
                {
                    OrderId = context.Message.OrderId,
                    CustomerId = context.Saga.CustomerId,
                    AmountCents = context.Saga.TotalCents,
                    CorrelationId = context.Saga.BusinessCorrelationId,
                })
                .TransitionTo(Authorising),

            When(Rejected)
                .Then(context =>
                {
                    context.Saga.FailureReason = context.Message.Reason;
                    Record(context.Saga, "Rejected");
                    logger.LogWarning("{Event} order={OrderId} reason={Reason}",
                        "saga.stock_rejected", context.Message.OrderId, context.Message.Reason);
                })
                .Publish(context => new OrderCancelled
                {
                    OrderId = context.Saga.CorrelationId,
                    CustomerId = context.Saga.CustomerId,
                    Reason = context.Message.Reason,
                    CorrelationId = context.Saga.BusinessCorrelationId,
                })
                .TransitionTo(Rejected_));

        During(Authorising,
            When(Authorised)
                .Then(context =>
                {
                    context.Saga.PspReference = context.Message.PspReference;
                    Record(context.Saga, "Confirmed");
                    logger.LogInformation("{Event} order={OrderId}", "saga.payment_authorised", context.Message.OrderId);
                })
                .Publish(context => new OrderConfirmed
                {
                    OrderId = context.Saga.CorrelationId,
                    CustomerId = context.Saga.CustomerId,
                    TotalCents = context.Saga.TotalCents,
                    LineCount = context.Saga.LineCount,
                    CorrelationId = context.Saga.BusinessCorrelationId,
                })
                .TransitionTo(Confirmed),

            // The compensating branch. Nothing is rolled back: the stock that was
            // held is handed back by an ordinary command, which is itself idempotent.
            When(Declined)
                .Then(context =>
                {
                    context.Saga.FailureReason = context.Message.Reason;
                    Record(context.Saga, "Compensating");
                    logger.LogWarning("{Event} order={OrderId} reason={Reason}",
                        "saga.payment_declined", context.Message.OrderId, context.Message.Reason);
                })
                .Send(InventoryQueue, context => new ReleaseStock
                {
                    OrderId = context.Message.OrderId,
                    CorrelationId = context.Saga.BusinessCorrelationId,
                })
                .TransitionTo(Compensating));

        During(Compensating,
            When(Released)
                .Then(context =>
                {
                    Record(context.Saga, "Cancelled");
                    logger.LogInformation("{Event} order={OrderId}", "saga.stock_released", context.Message.OrderId);
                })
                .Publish(context => new OrderCancelled
                {
                    OrderId = context.Saga.CorrelationId,
                    CustomerId = context.Saga.CustomerId,
                    Reason = context.Saga.FailureReason ?? "payment_declined",
                    CorrelationId = context.Saga.BusinessCorrelationId,
                })
                .TransitionTo(Cancelled));

        // The instance is kept after it finishes rather than finalised away, so that
        // "where did order 8842 stop, and what was undone?" stays answerable from
        // one row. Production expires rows after ninety days.
    }

    private static void Record(OrderState saga, string state)
    {
        saga.Path = string.IsNullOrEmpty(saga.Path) ? state : $"{saga.Path} -> {state}";
        saga.UpdatedAt = DateTime.UtcNow;
    }
}
