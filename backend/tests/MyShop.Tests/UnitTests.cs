using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Modules.Catalog;
using MyShop.Api.Modules.Orders;
using MyShop.Api.Modules.Payments;

namespace MyShop.Tests;

public class OrderStateMachineTests
{
    private static Order NewOrder() => new() { Number = "T1", Email = "a@b.c" };

    [Theory]
    [InlineData(OrderStatus.Created, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Created, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Created, OrderStatus.Shipping, false)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipping, true)]
    [InlineData(OrderStatus.Shipping, OrderStatus.Completed, true)]
    [InlineData(OrderStatus.Shipping, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Completed, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed, false)]
    public void Transitions(OrderStatus from, OrderStatus to, bool allowed) =>
        Assert.Equal(allowed, OrderStateMachine.CanTransition(from, to));

    [Fact]
    public void HappyPath_RecordsHistory()
    {
        var o = NewOrder();
        OrderStateMachine.Transition(o, OrderStatus.Confirmed);
        OrderStateMachine.Transition(o, OrderStatus.Shipping);
        OrderStateMachine.Transition(o, OrderStatus.Completed);
        Assert.Equal(OrderStatus.Completed, o.Status);
        Assert.Equal(3, o.History.Count);
    }

    [Fact]
    public void InvalidTransition_Throws409()
    {
        var ex = Assert.Throws<DomainException>(() => OrderStateMachine.Transition(NewOrder(), OrderStatus.Completed));
        Assert.Equal(409, ex.StatusCode);
    }
}

public class PricingTests
{
    private static readonly PricingOptions Opt = new() { FreeShippingThreshold = 500_000, FlatShippingFee = 30_000 };
    private static readonly DateTime Now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BelowThreshold_ChargesShipping()
    {
        var p = Pricing.Calculate(200_000, null, Opt, Now);
        Assert.Equal(30_000, p.ShippingFee);
        Assert.Equal(230_000, p.Total);
    }

    [Fact]
    public void PercentPromotion_RespectsCap_AndShippingUsesDiscountedSubtotal()
    {
        var promo = new Promotion { Code = "P", Type = PromotionType.Percent, Value = 10, MaxDiscount = 100_000, StartsAt = Now.AddDays(-1) };
        var p = Pricing.Calculate(2_000_000, promo, Opt, Now);
        Assert.Equal(100_000, p.Discount);
        Assert.Equal(0, p.ShippingFee);
        Assert.Equal(1_900_000, p.Total);
    }

    [Fact]
    public void FixedPromotion_NeverExceedsSubtotal()
    {
        var promo = new Promotion { Code = "F", Type = PromotionType.Fixed, Value = 1_000_000, StartsAt = Now.AddDays(-1) };
        var p = Pricing.Calculate(100_000, promo, Opt, Now);
        Assert.Equal(100_000, p.Discount);
        Assert.Equal(30_000, p.Total);
    }

    [Fact]
    public void ExpiredOrMinSubtotal_Throws()
    {
        var expired = new Promotion { Code = "E", Type = PromotionType.Fixed, Value = 1, StartsAt = Now.AddDays(-10), EndsAt = Now.AddDays(-1) };
        Assert.Throws<DomainException>(() => Pricing.Calculate(100, expired, Opt, Now));
        var min = new Promotion { Code = "M", Type = PromotionType.Fixed, Value = 1, StartsAt = Now.AddDays(-1), MinSubtotal = 300_000 };
        Assert.Throws<DomainException>(() => Pricing.Calculate(100_000, min, Opt, Now));
    }
}

public class WebhookSignatureTests
{
    [Fact]
    public void Valid_Tampered_Expired()
    {
        var now = DateTimeOffset.UtcNow;
        var sig = WebhookSignature.Sign("secret", "{\"a\":1}", now.ToUnixTimeSeconds());
        Assert.True(WebhookSignature.Verify("secret", "{\"a\":1}", sig, now, 300));
        Assert.False(WebhookSignature.Verify("secret", "{\"a\":2}", sig, now, 300));
        Assert.False(WebhookSignature.Verify("other", "{\"a\":1}", sig, now, 300));
        Assert.False(WebhookSignature.Verify("secret", "{\"a\":1}", sig, now.AddMinutes(10), 300));
        Assert.False(WebhookSignature.Verify("secret", "{\"a\":1}", "garbage", now, 300));
    }
}

public class MiscTests
{
    [Fact]
    public void Csv_HandlesQuotes() =>
        Assert.Equal(["a", "b,c", "say \"hi\"", ""], Csv.ParseLine("a,\"b,c\",\"say \"\"hi\"\"\","));

    [Theory]
    [InlineData(1_000_000, 1, "VND", 1_000_000)]
    [InlineData(1_000_000, 0.0000394, "USD", 39.40)]
    public void Currency_Convert(double vnd, double rate, string cur, double expected) =>
        Assert.Equal((decimal)expected, CurrencyService.Convert((decimal)vnd, (decimal)rate, cur));

    [Fact]
    public void MaskEmail() => Assert.Equal("j***@example.com", LogMasking.MaskEmail("john@example.com"));
}
