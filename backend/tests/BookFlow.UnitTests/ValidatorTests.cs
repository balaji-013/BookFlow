using BookFlow.Application.Features.Auth;
using BookFlow.Application.Features.Catalog;
using BookFlow.Application.Features.Finance;
using BookFlow.Application.Features.Parties;
using BookFlow.Application.Features.Sales;
using BookFlow.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace BookFlow.UnitTests;

public class ValidatorTests
{
    [Fact]
    public void Invoice_requires_lines_and_positive_quantities()
    {
        var v = new CreateInvoiceValidator();
        v.Validate(new CreateInvoiceRequest(null, null, null, null, DiscountType.None, 0, null, new(), null)).IsValid.Should().BeFalse();
        var bad = new CreateInvoiceRequest(null, null, null, null, DiscountType.None, 0, null, new() { new(Guid.NewGuid(), 0, null, 0) }, null);
        v.Validate(bad).IsValid.Should().BeFalse();
        var ok = new CreateInvoiceRequest(null, null, null, null, DiscountType.Percentage, 10, null, new() { new(Guid.NewGuid(), 1, 100, 0) }, new(PaymentMethod.Cash, 90, null));
        v.Validate(ok).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Invoice_percentage_discount_cannot_exceed_100()
    {
        var v = new CreateInvoiceValidator();
        var r = new CreateInvoiceRequest(null, null, null, null, DiscountType.Percentage, 150, null, new() { new(Guid.NewGuid(), 1, 100, 0) }, null);
        v.Validate(r).Errors.Should().Contain(e => e.PropertyName == "DiscountValue");
    }

    [Fact]
    public void Payment_allocations_cannot_exceed_amount()
    {
        var v = new CreatePaymentValidator();
        var r = new CreatePaymentRequest(null, PaymentMethod.Cash, 100, null, null, null, new() { new(Guid.NewGuid(), 150) }, null);
        v.Validate(r).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("33AAAAA0000A1Z5", true)]
    [InlineData("33aaaaa0000a1z5", false)]
    [InlineData("1234", false)]
    public void Gstin_format(string gstin, bool valid)
    {
        var v = new SaveCustomerValidator();
        var r = new SaveCustomerRequest("Test", null, null, gstin, null, CustomerType.Retail, 0, null, true, null);
        v.Validate(r).IsValid.Should().Be(valid);
    }

    [Fact]
    public void Book_requires_title_and_valid_gst_rate()
    {
        var v = new SaveBookValidator();
        var r = new SaveBookRequest(null, null, "", null, null, null, null, null, null, null, null, "Tamil", null, 10, 20, 20, "4901", 40, 1, true, null, null);
        var result = v.Validate(r);
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
        result.Errors.Should().Contain(e => e.PropertyName == "GstRate");
    }

    [Fact]
    public void Registration_requires_strong_enough_password()
    {
        var v = new RegisterTenantValidator();
        v.Validate(new RegisterTenantRequest("Shop", "Owner", "o@x.com", "short", null, null, null, null)).IsValid.Should().BeFalse();
        v.Validate(new RegisterTenantRequest("Shop", "Owner", "o@x.com", "longenough1", null, null, null, "33")).IsValid.Should().BeTrue();
    }
}
