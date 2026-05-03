using FluentAssertions;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Verifies AccountPolicies.ValidateLiabilityRepayment covers all
/// three invalid combinations of repayment type and interest rate:
///   - Amortising must have a rate
///   - FullMonthly must NOT have a rate
///   - null repayment type must NOT have a rate (Layer 2 guard added
///     during the Accounts SPA brainstorm; closes a silent invariant gap)
/// </summary>
public class AccountPoliciesTests
{
    [Fact]
    public void ValidateLiabilityRepayment_WithAmortisingAndRate_Succeeds()
    {
        var result = AccountPolicies.ValidateLiabilityRepayment("Amortising", 0.035m);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateLiabilityRepayment_WithAmortisingAndNoRate_Fails()
    {
        var result = AccountPolicies.ValidateLiabilityRepayment("Amortising", null);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(AccountPolicies.InvalidLiabilityRepaymentCode);
    }

    [Fact]
    public void ValidateLiabilityRepayment_WithFullMonthlyAndNoRate_Succeeds()
    {
        var result = AccountPolicies.ValidateLiabilityRepayment("FullMonthly", null);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateLiabilityRepayment_WithFullMonthlyAndRate_Fails()
    {
        var result = AccountPolicies.ValidateLiabilityRepayment("FullMonthly", 0.035m);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Message.Should().Contain("FullMonthly");
    }

    [Fact]
    public void ValidateLiabilityRepayment_WithNoneAndNoRate_Succeeds()
    {
        var result = AccountPolicies.ValidateLiabilityRepayment(null, null);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateLiabilityRepayment_WithNoneAndRate_Fails()
    {
        var result = AccountPolicies.ValidateLiabilityRepayment(null, 0.035m);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Message.Should().Contain("no repayment type");
    }
}
