using BookFlow.Domain.Common;
using BookFlow.Domain.Enums;

namespace BookFlow.Domain.Pricing;

/// <summary>Strategy: invoice-level discount applied on the sum of line taxable amounts.</summary>
public interface IDiscountPolicy
{
    decimal Calculate(decimal subtotal);
}

public sealed class NoDiscountPolicy : IDiscountPolicy
{
    public static readonly NoDiscountPolicy Instance = new();
    public decimal Calculate(decimal subtotal) => 0m;
}

public sealed class PercentageDiscountPolicy : IDiscountPolicy
{
    private readonly decimal _percentage;
    public PercentageDiscountPolicy(decimal percentage)
    {
        if (percentage < 0 || percentage > 100) throw new ArgumentOutOfRangeException(nameof(percentage));
        _percentage = percentage;
    }
    public decimal Calculate(decimal subtotal) => Money.Round2(subtotal * _percentage / 100m);
}

public sealed class FixedDiscountPolicy : IDiscountPolicy
{
    private readonly decimal _amount;
    public FixedDiscountPolicy(decimal amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        _amount = amount;
    }
    public decimal Calculate(decimal subtotal) => Math.Min(Money.Round2(_amount), subtotal);
}

public static class DiscountPolicyFactory
{
    public static IDiscountPolicy Create(DiscountType type, decimal value) => type switch
    {
        DiscountType.Percentage => new PercentageDiscountPolicy(value),
        DiscountType.Fixed => new FixedDiscountPolicy(value),
        _ => NoDiscountPolicy.Instance
    };
}

public sealed record PricedLine(Guid BookId, int Quantity, decimal UnitPrice, decimal LineDiscountPercent, decimal GstRate);

public sealed class CalculatedLine
{
    public Guid BookId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Gross { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal DiscountAmount { get; set; }   // line + share of invoice discount
    public decimal TaxableAmount { get; set; }
    public decimal GstRate { get; init; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class InvoiceTotals
{
    public List<CalculatedLine> Lines { get; } = new();
    public decimal SubTotal { get; set; }          // Σ gross
    public decimal DiscountTotal { get; set; }     // Σ all discounts
    public decimal TaxableTotal { get; set; }
    public decimal CgstTotal { get; set; }
    public decimal SgstTotal { get; set; }
    public decimal IgstTotal { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
}

/// <summary>
/// Pure calculation, see API.md "Pricing rules". Order: line discount → invoice discount distributed
/// proportionally → GST per line → sum → round to rupee.
/// </summary>
public static class InvoiceCalculator
{
    public static InvoiceTotals Calculate(IReadOnlyList<PricedLine> lines, IDiscountPolicy invoiceDiscount, bool interState, bool roundToRupee = true)
    {
        if (lines.Count == 0) throw new ArgumentException("An invoice needs at least one line.", nameof(lines));
        var totals = new InvoiceTotals();

        foreach (var l in lines)
        {
            if (l.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(l.Quantity));
            if (l.UnitPrice < 0) throw new ArgumentOutOfRangeException(nameof(l.UnitPrice));
            if (l.LineDiscountPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(l.LineDiscountPercent));

            var gross = Money.Round2(l.Quantity * l.UnitPrice);
            var lineDisc = Money.Round2(gross * l.LineDiscountPercent / 100m);
            totals.Lines.Add(new CalculatedLine
            {
                BookId = l.BookId, Quantity = l.Quantity, UnitPrice = l.UnitPrice, Gross = gross,
                DiscountPercent = l.LineDiscountPercent, DiscountAmount = lineDisc,
                TaxableAmount = gross - lineDisc, GstRate = l.GstRate
            });
        }

        var subtotalAfterLineDiscounts = totals.Lines.Sum(x => x.TaxableAmount);
        var invoiceDisc = invoiceDiscount.Calculate(subtotalAfterLineDiscounts);

        if (invoiceDisc > 0 && subtotalAfterLineDiscounts > 0)
        {
            // distribute proportionally; push rounding remainder to the largest line
            decimal distributed = 0;
            CalculatedLine? largest = null;
            foreach (var line in totals.Lines)
            {
                var share = Money.Round2(invoiceDisc * line.TaxableAmount / subtotalAfterLineDiscounts);
                line.DiscountAmount += share;
                line.TaxableAmount -= share;
                distributed += share;
                if (largest is null || line.TaxableAmount > largest.TaxableAmount) largest = line;
            }
            var remainder = invoiceDisc - distributed;
            if (remainder != 0 && largest is not null)
            {
                largest.DiscountAmount += remainder;
                largest.TaxableAmount -= remainder;
            }
        }

        foreach (var line in totals.Lines)
        {
            var tax = Money.Round2(line.TaxableAmount * line.GstRate / 100m);
            if (interState)
            {
                line.IgstAmount = tax;
            }
            else
            {
                line.CgstAmount = Money.Round2(tax / 2m);
                line.SgstAmount = tax - line.CgstAmount;
            }
            line.LineTotal = line.TaxableAmount + line.CgstAmount + line.SgstAmount + line.IgstAmount;
        }

        totals.SubTotal = totals.Lines.Sum(x => x.Gross);
        totals.DiscountTotal = totals.Lines.Sum(x => x.DiscountAmount);
        totals.TaxableTotal = totals.Lines.Sum(x => x.TaxableAmount);
        totals.CgstTotal = totals.Lines.Sum(x => x.CgstAmount);
        totals.SgstTotal = totals.Lines.Sum(x => x.SgstAmount);
        totals.IgstTotal = totals.Lines.Sum(x => x.IgstAmount);
        var exact = totals.Lines.Sum(x => x.LineTotal);
        totals.GrandTotal = roundToRupee ? Money.RoundRupee(exact) : exact;
        totals.RoundOff = Money.Round2(totals.GrandTotal - exact);
        return totals;
    }
}
