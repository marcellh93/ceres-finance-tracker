using ProjectCeres.Models;

namespace ProjectCeres.Services.Reports;

public record ReportParameters(
    int? CurrencyId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? AccountId = null,
    Guid? CategoryId = null,
    int Limit = 50,
    int Offset = 0);

public interface IReportGenerator
{
    Task<object> GenerateAsync(ReportParameters parameters);
}
