using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaltaShop.Orchestration;

/// <summary>The saga's own database. No other service connects to it.</summary>
public class OrchestrationDbContext(DbContextOptions<OrchestrationDbContext> options)
    : SagaDbContext(options)
{
    protected override IEnumerable<ISagaClassMap> Configurations
    {
        get { yield return new OrderStateMap(); }
    }
}

public class OrderStateMap : SagaClassMap<OrderState>
{
    protected override void Configure(EntityTypeBuilder<OrderState> entity, ModelBuilder model)
    {
        entity.Property(x => x.CurrentState).HasMaxLength(64);
        entity.Property(x => x.CustomerId).HasMaxLength(64);
        entity.Property(x => x.BusinessCorrelationId).HasMaxLength(64);
        entity.Property(x => x.FailureReason).HasMaxLength(128);
        entity.Property(x => x.PspReference).HasMaxLength(64);
        entity.Property(x => x.Path).HasMaxLength(512);
    }
}
