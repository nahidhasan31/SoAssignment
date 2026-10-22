using MaltaShop.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace MaltaShop.Payment;

public class AuthorisePaymentConsumer(PaymentDbContext db, ILogger<AuthorisePaymentConsumer> logger)
    : IConsumer<AuthorisePayment>
{
    private static readonly long Ceiling =
        long.Parse(Environment.GetEnvironmentVariable("CARD_CEILING_EUR") ?? "1000") * 100;

    public async Task Consume(ConsumeContext<AuthorisePayment> context)
    {
        var orderId = context.Message.OrderId;

        // The unique index on OrderId is the real guard; this read makes the common
        // redelivery cheap rather than relying on a constraint violation.
        var existing = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId);
        if (existing is not null)
        {
            logger.LogInformation("{Event} order={OrderId} state={State}",
                "payment.already_decided", orderId, existing.State);
            await Publish(context, existing.State, existing.PspReference, existing.DeclineReason);
            return;
        }

        var (reference, decline) = CallProvider(context.Message.AmountCents);
        var state = decline is null ? "AUTHORISED" : "DECLINED";
        db.Payments.Add(new PaymentRecord
        {
            OrderId = orderId,
            CustomerId = context.Message.CustomerId,
            AmountCents = context.Message.AmountCents,
            State = state,
            PspReference = reference,
            DeclineReason = decline,
        });
        await db.SaveChangesAsync();

        if (decline is null)
            logger.LogInformation("{Event} order={OrderId} amount={AmountCents} correlation_id={CorrelationId}",
                "payment.authorised", orderId, context.Message.AmountCents, context.Message.CorrelationId);
        else
            logger.LogWarning("{Event} order={OrderId} amount={AmountCents} reason={Reason}",
                "payment.declined", orderId, context.Message.AmountCents, decline);

        await Publish(context, state, reference, decline);
    }

    /// <summary>Stands in for the provider's API. Above the ceiling it declines,
    /// which is what makes the compensating path easy to demonstrate.</summary>
    private static (string? reference, string? decline) CallProvider(long amountCents) =>
        amountCents > Ceiling
            ? (null, "card_ceiling_exceeded")
            : ($"psp_{Guid.NewGuid().ToString("N")[..12]}", null);

    private static Task Publish(ConsumeContext<AuthorisePayment> context, string state,
                                string? reference, string? decline) =>
        state == "AUTHORISED"
            ? context.Publish(new PaymentAuthorised
            {
                OrderId = context.Message.OrderId,
                PspReference = reference ?? "",
                CorrelationId = context.Message.CorrelationId,
            })
            : context.Publish(new PaymentDeclined
            {
                OrderId = context.Message.OrderId,
                Reason = decline ?? "declined",
                CorrelationId = context.Message.CorrelationId,
            });
}
