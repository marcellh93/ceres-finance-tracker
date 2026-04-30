namespace ProjectCeres.ViewModels;

/// <summary>
/// Routing keys consumed by TransactionService.UpdateAsync to dispatch
/// to the regular-transaction or liability-payment service branch.
/// </summary>
public static class TransactionTypes
{
    public const string Regular          = "Regular";
    public const string LiabilityPayment = "LiabilityPayment";
}
