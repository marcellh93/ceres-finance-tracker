namespace ProjectCeres.Models;

public class ImportTransferExclusion
{
    public Guid Id { get; set; }
    public string DescriptionPattern { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
