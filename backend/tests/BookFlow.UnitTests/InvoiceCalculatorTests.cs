using BookFlow.Domain.Common;
using BookFlow.Domain.Enums;
using BookFlow.Domain.Pricing;
using FluentAssertions;
using Xunit;

namespace BookFlow.UnitTests;

public class InvoiceCalculatorTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();

    [Fact]
    public void Books_at_zero_gst_total_equals_quantity_times_price()
    {
        var t = InvoiceCalculator.Calculate(new[] { new PricedLine(A, 2, 250m, 0, 0) }, NoDiscountPolicy.Instance, interState: false);
        t.SubTotal.Should().Be(500m);
        t.TaxableTotal.Should().Be(500m);
        t.CgstTotal.Should().Be(0);
        t.GrandTotal.Should().Be(500m);
        t.RoundOff.Should().Be(0);
    }

    [Fact]
    public void Intra_state_splits_gst_equally_between_cgst_and_sgst()
    {
        var t = InvoiceCalculator.Calculate(new[] { new PricedLine(A, 1, 100m, 0, 12m) }, NoDiscountPolicy.Instance, interState: false);
        t.CgstTotal.Should().Be(6m);
        t.SgstTotal.Should().Be(6m);
        t.IgstTotal.Should().Be(0);
        t.GrandTotal.Should().Be(112m);
    }

    [Fact]
    public void Inter_state_uses_igst_only()
    {
        var t = InvoiceCalculator.Calculate(new[] { new PricedLine(A, 1, 100m, 0, 18m) }, NoDiscountPolicy.Instance, interState: true);
        t.IgstTotal.Should().Be(18m);
        t.CgstTotal.Should().Be(0);
        t.SgstTotal.Should().Be(0);
    }

    [Fact]
    public void Line_discount_applies_before_tax()
    {
        var t = InvoiceCalculator.Calculate(new[] { new PricedLine(A, 1, 200m, 10m, 5m) }, NoDiscountPolicy.Instance, false);
        t.DiscountTotal.Should().Be(20m);
        t.TaxableTotal.Should().Be(180m);
        (t.CgstTotal + t.SgstTotal).Should().Be(9m);
        t.GrandTotal.Should().Be(189m);
    }

    [Fact]
    public void Invoice_percentage_discount_is_distributed_proportionally_and_sums_exactly()
    {
        // 3 lines of odd amounts so that proportional shares need rounding.
        var lines = new[] { new PricedLine(A, 1, 99.99m, 0, 0), new PricedLine(B, 3, 33.33m, 0, 0), new PricedLine(Guid.NewGuid(), 1, 1m, 0, 0) };
        var t = InvoiceCalculator.Calculate(lines, new PercentageDiscountPolicy(10m), false, roundToRupee: false);
        var subtotal = 99.99m + 99.99m + 1m;
        var expectedDiscount = Money.Round2(subtotal * 0.10m);
        t.DiscountTotal.Should().Be(expectedDiscount);
        t.Lines.Sum(l => l.DiscountAmount).Should().Be(expectedDiscount);
        t.TaxableTotal.Should().Be(subtotal - expectedDiscount);
    }

    [Fact]
    public void Fixed_discount_cannot_exceed_subtotal()
    {
        var t = InvoiceCalculator.Calculate(new[] { new PricedLine(A, 1, 50m, 0, 0) }, new FixedDiscountPolicy(500m), false);
        t.DiscountTotal.Should().Be(50m);
        t.GrandTotal.Should().Be(0);
    }

    [Fact]
    public void Grand_total_is_rounded_to_rupee_with_round_off_recorded()
    {
        var t = InvoiceCalculator.Calculate(new[] { new PricedLine(A, 1, 100m, 0, 5m) }, new PercentageDiscountPolicy(3m), false);
        // taxable 97, tax 4.85 => 101.85 => 102, round off +0.15
        t.GrandTotal.Should().Be(102m);
        t.RoundOff.Should().Be(0.15m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_non_positive_quantity(int qty)
    {
        var act = () => InvoiceCalculator.Calculate(new[] { new PricedLine(A, qty, 10m, 0, 0) }, NoDiscountPolicy.Instance, false);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Rejects_empty_invoice()
    {
        var act = () => InvoiceCalculator.Calculate(Array.Empty<PricedLine>(), NoDiscountPolicy.Instance, false);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Factory_returns_expected_policies()
    {
        DiscountPolicyFactory.Create(DiscountType.None, 0).Should().BeOfType<NoDiscountPolicy>();
        DiscountPolicyFactory.Create(DiscountType.Percentage, 5).Should().BeOfType<PercentageDiscountPolicy>();
        DiscountPolicyFactory.Create(DiscountType.Fixed, 5).Should().BeOfType<FixedDiscountPolicy>();
        var act = () => new PercentageDiscountPolicy(101);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class FinancialYearTests
{
    [Theory]
    [InlineData("2026-04-01", "2026-27")]
    [InlineData("2027-03-31", "2026-27")]
    [InlineData("2026-03-31", "2025-26")]
    [InlineData("2099-12-25", "2099-00")]
    public void Indian_financial_year_label(string date, string expected)
        => FinancialYear.Of(DateOnly.Parse(date)).Should().Be(expected);
}

public class MoneyTests
{
    [Theory]
    [InlineData(1.005, 1.01)]
    [InlineData(2.675, 2.68)]
    [InlineData(-1.005, -1.01)]
    public void Round2_is_half_away_from_zero(double input, double expected)
        => Money.Round2((decimal)input).Should().Be((decimal)expected);

    [Fact]
    public void RoundRupee_rounds_half_up() => Money.RoundRupee(101.50m).Should().Be(102m);
}

public class GuidGeneratorTests
{
    [Fact]
    public void Sequential_guids_are_unique()
    {
        var set = new HashSet<Guid>();
        for (var i = 0; i < 10_000; i++) set.Add(GuidGenerator.NewSequential()).Should().BeTrue();
    }
}
