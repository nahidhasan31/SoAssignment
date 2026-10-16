using MaltaShop.Contracts;
using MaltaShop.Orchestration;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaltaShop.Tests;

/// <summary>
/// Unit tests for the process manager, run against MassTransit's in-memory test
/// harness: no broker, no database, no containers. They are the reason the saga can
/// be changed with confidence, because every branch - including the compensating
/// one - is asserted here in milliseconds.
/// </summary>
public class OrderStateMachineTests
{
    private static ServiceProvider Harness() =>
        new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(x =>
                x.AddSagaStateMachine<OrderStateMachine, OrderState>().InMemoryRepository())
            .BuildServiceProvider(true);

    private static OrderSubmitted Submitted(Guid orderId, long totalCents = 73_800) => new()
    {
        OrderId = orderId,
        CustomerId = "MT-D-5520",
        TotalCents = totalCents,
        Lines = new List<OrderLine> { new("MICRO-25L", 2), new("DISHW-60", 1) },
        CorrelationId = "corr-1",
    };

    [Fact]
    public async Task A_submitted_order_starts_the_saga_and_asks_for_stock()
    {
        await using var provider = Harness();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(Submitted(orderId));

        var saga = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderStateMachine, OrderState>>();
        Assert.True(await saga.Consumed.Any<OrderSubmitted>());
        var instance = saga.Sagas.Contains(orderId);
        Assert.NotNull(instance);
        Assert.Equal("Reserving", instance!.CurrentState);
        Assert.True(await harness.Sent.Any<ReserveStock>(),
            "the saga must send ReserveStock before anything else happens");
    }

    [Fact]
    public async Task Reserved_stock_moves_the_saga_on_to_payment()
    {
        await using var provider = Harness();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var saga = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderStateMachine, OrderState>>();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(Submitted(orderId));
        await saga.Consumed.Any<OrderSubmitted>();
        await harness.Bus.Publish(new StockReserved { OrderId = orderId, CorrelationId = "corr-1" });
        await saga.Consumed.Any<StockReserved>();

        Assert.Equal("Authorising", saga.Sagas.Contains(orderId)!.CurrentState);
        Assert.True(await harness.Sent.Any<AuthorisePayment>());
    }

    [Fact]
    public async Task An_authorised_payment_confirms_the_order()
    {
        await using var provider = Harness();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var saga = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderStateMachine, OrderState>>();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(Submitted(orderId));
        await saga.Consumed.Any<OrderSubmitted>();
        await harness.Bus.Publish(new StockReserved { OrderId = orderId, CorrelationId = "corr-1" });
        await saga.Consumed.Any<StockReserved>();
        await harness.Bus.Publish(new PaymentAuthorised
        { OrderId = orderId, PspReference = "psp_abc123", CorrelationId = "corr-1" });
        await saga.Consumed.Any<PaymentAuthorised>();

        var instance = saga.Sagas.Contains(orderId)!;
        Assert.Equal("Confirmed", instance.CurrentState);
        Assert.Equal("Reserving -> Authorising -> Confirmed", instance.Path);
        Assert.Equal("psp_abc123", instance.PspReference);
        Assert.True(await harness.Published.Any<OrderConfirmed>());
    }

    [Fact]
    public async Task A_declined_payment_compensates_the_reservation()
    {
        await using var provider = Harness();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var saga = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderStateMachine, OrderState>>();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(Submitted(orderId, 129_900));
        await saga.Consumed.Any<OrderSubmitted>();
        await harness.Bus.Publish(new StockReserved { OrderId = orderId, CorrelationId = "corr-1" });
        await saga.Consumed.Any<StockReserved>();
        await harness.Bus.Publish(new PaymentDeclined
        { OrderId = orderId, Reason = "card_ceiling_exceeded", CorrelationId = "corr-1" });
        await saga.Consumed.Any<PaymentDeclined>();

        Assert.Equal("Compensating", saga.Sagas.Contains(orderId)!.CurrentState);
        Assert.True(await harness.Sent.Any<ReleaseStock>(),
            "a declined payment must hand the held stock back");

        await harness.Bus.Publish(new StockReleased { OrderId = orderId, CorrelationId = "corr-1" });
        await saga.Consumed.Any<StockReleased>();

        var instance = saga.Sagas.Contains(orderId)!;
        Assert.Equal("Cancelled", instance.CurrentState);
        Assert.Equal("Reserving -> Authorising -> Compensating -> Cancelled", instance.Path);
        Assert.Equal("card_ceiling_exceeded", instance.FailureReason);
        Assert.True(await harness.Published.Any<OrderCancelled>());
    }

    [Fact]
    public async Task Rejected_stock_never_reaches_payment()
    {
        await using var provider = Harness();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var saga = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderStateMachine, OrderState>>();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(Submitted(orderId));
        await saga.Consumed.Any<OrderSubmitted>();
        await harness.Bus.Publish(new StockRejected
        { OrderId = orderId, Reason = "insufficient_stock:AWAIT-STOCK", CorrelationId = "corr-1" });
        await saga.Consumed.Any<StockRejected>();

        Assert.Equal("Rejected_", saga.Sagas.Contains(orderId)!.CurrentState);
        Assert.False(await harness.Sent.Any<AuthorisePayment>(),
            "payment must never be asked for stock that was not held");
        Assert.True(await harness.Published.Any<OrderCancelled>());
    }

    [Fact]
    public async Task An_event_for_an_unknown_order_creates_nothing()
    {
        await using var provider = Harness();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var saga = provider.GetRequiredService<ISagaStateMachineTestHarness<OrderStateMachine, OrderState>>();

        var strayOrderId = Guid.NewGuid();
        await harness.Bus.Publish(new PaymentAuthorised
        { OrderId = strayOrderId, PspReference = "psp_stray", CorrelationId = "corr-x" });
        await saga.Consumed.Any<PaymentAuthorised>();

        Assert.Null(saga.Sagas.Contains(strayOrderId));
    }
}
