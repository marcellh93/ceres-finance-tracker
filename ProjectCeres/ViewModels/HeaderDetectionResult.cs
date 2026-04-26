namespace ProjectCeres.ViewModels;

public class HeaderDetectionResult
{
    public IReadOnlyList<string> Headers { get; init; } = [];
    public string? DateColumn { get; init; }
    public string? AmountColumn { get; init; }
    public string? DescriptionColumn { get; init; }
    public string? CategoryColumn { get; init; }
}
