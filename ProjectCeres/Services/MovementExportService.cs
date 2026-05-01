using System.Globalization;
using System.Text;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class MovementExportService(IMovementService movementService) : IMovementExportService
{
    public async Task<string> BuildCsvAsync(
        Guid? accountId,
        DateOnly? from,
        DateOnly? to,
        string? q,
        MovementType? type,
        string? currency)
    {
        // Pull the entire matching set; no pagination.
        var rows = await movementService.GetRecentAsync(accountId, from, to, int.MaxValue, 0, q, type, currency);

        var sb = new StringBuilder();
        sb.AppendLine("Date,Type,Amount,Currency,Account,Counterparty,Category,Description,Cleared");

        foreach (var r in rows)
        {
            string typeLabel = r.MovementType switch
            {
                MovementType.Transaction      => "Transaction",
                MovementType.Transfer         => "Transfer",
                MovementType.LiabilityPayment => "Debt Payment",
                _ => "?"
            };

            string account = r.MovementType switch
            {
                MovementType.Transaction      => r.AccountName ?? "",
                MovementType.Transfer         => r.SourceAccountName ?? "",
                MovementType.LiabilityPayment => r.AssetAccountName ?? "",
                _ => ""
            };

            string counterparty = r.MovementType switch
            {
                MovementType.Transfer         => r.DestAccountName ?? "",
                MovementType.LiabilityPayment => r.LiabilityAccountName ?? "",
                _ => ""
            };

            string category = r.MovementType == MovementType.Transaction ? r.CategoryName ?? "" : "";

            sb.Append(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Csv(typeLabel)).Append(',');
            sb.Append(r.Amount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Csv(r.CurrencySymbol ?? "")).Append(',');
            sb.Append(Csv(account)).Append(',');
            sb.Append(Csv(counterparty)).Append(',');
            sb.Append(Csv(category)).Append(',');
            sb.Append(Csv(r.Description ?? "")).Append(',');
            sb.AppendLine(r.IsCleared ? "Cleared" : "Pending");
        }
        return sb.ToString();
    }

    private static string Csv(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
