namespace ProjectCeres.Common;

/// <summary>
/// Financial-record tables that erasure anonymises-and-retains instead of deleting, per
/// legal.md § Financial Records — Legal Minimums (Código de Comercio 6yr / Ley General
/// Tributaria 4-6yr). Categories is included though not itself a statutory record: it is
/// anonymise-retained-for-coherence so the retained Transactions/Transfers/LiabilityPayments
/// rows keep a valid category reference instead of leaking a custom category name via a
/// dangling/nulled FK.
/// </summary>
public static class StatutoryRetentionSet
{
    public static readonly IReadOnlyCollection<string> Tables = new HashSet<string>(StringComparer.Ordinal)
    {
        "Transactions",
        "Transfers",
        "LiabilityPayments",
        "Accounts",
        "TransactionAttachments",
        "TransferAttachments",
        "Categories",
    };
}
