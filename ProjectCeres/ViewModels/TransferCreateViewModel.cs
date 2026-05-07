using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProjectCeres.ViewModels;

public class TransferCreateViewModel
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select a source account.")]
    [Display(Name = "From Account")]
    public Guid? SourceAccountId { get; set; }

    [Required(ErrorMessage = "Please select a destination account.")]
    [Display(Name = "To Account")]
    public Guid? DestAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    [Display(Name = "Attachments")]
    public List<IFormFile>? Attachments { get; set; }

    public bool IsCleared { get; set; }
}
