using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class CategoryEditViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Category name is required.")]
    [StringLength(100, ErrorMessage = "Category name cannot exceed 100 characters.")]
    [Display(Name = "Category Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Lifestyle Tag")]
    public string? LifestyleTag { get; set; }
}
