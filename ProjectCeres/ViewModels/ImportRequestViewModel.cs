using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProjectCeres.ViewModels;

public class ImportRequestViewModel
{
    [Required]
    public IFormFile? File { get; set; }

    [Required]
    public Guid? AccountId { get; set; }

    [Required]
    public Guid? CategoryId { get; set; }

    [Required]
    public string? DateColumn { get; set; }

    [Required]
    public string? AmountColumn { get; set; }

    [Required]
    public string? DescriptionColumn { get; set; }

    public string? CategoryColumn { get; set; }
    public bool FlipDebitSign { get; set; }
}
