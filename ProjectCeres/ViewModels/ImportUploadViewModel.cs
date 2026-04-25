using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProjectCeres.ViewModels;

public class ImportUploadViewModel
{
    [Required(ErrorMessage = "Please select a CSV file.")]
    public IFormFile? File { get; set; }

    [Required(ErrorMessage = "Please select an account.")]
    public Guid? AccountId { get; set; }

    [Required(ErrorMessage = "Please select a default category.")]
    public Guid? CategoryId { get; set; }

    public Guid? ProfileId { get; set; }

    // Manual column mapping (used when no profile is selected)
    public string? DateColumn { get; set; }
    public string? AmountColumn { get; set; }
    public string? DescriptionColumn { get; set; }
    public string? CategoryColumn { get; set; }
    public bool FlipDebitSign { get; set; }
}
