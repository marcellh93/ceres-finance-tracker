using ProjectCeres.Common;

namespace ProjectCeres.Services;

public static class AccountPolicies
{
    public const string InvalidLiabilityRepaymentCode = "INVALID_LIABILITY_REPAYMENT";
    public const string InvalidAccountTypeCode        = "INVALID_ACCOUNT_TYPE";
    public const string InvalidCurrencyCode           = "INVALID_CURRENCY";

    /// <summary>
    /// A liability with Amortising repayment must have an interest rate;
    /// a liability with FullMonthly repayment must NOT have an interest rate.
    /// </summary>
    public static Result ValidateLiabilityRepayment(string? repaymentType, decimal? interestRate)
    {
        if (repaymentType == "Amortising" && interestRate is null)
            return Result.Fail(InvalidLiabilityRepaymentCode, "An Amortising liability must have an interest rate.");
        if (repaymentType == "FullMonthly" && interestRate is not null)
            return Result.Fail(InvalidLiabilityRepaymentCode, "A FullMonthly liability must not have an interest rate.");
        return Result.Ok();
    }
}
