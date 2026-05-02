using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class ImportTransferExclusion : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string DescriptionPattern { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
