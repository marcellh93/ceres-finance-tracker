using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class CategoryCreateViewModel
{
    [Required(ErrorMessage = "Category name is required.")]
    [StringLength(100, ErrorMessage = "Category name cannot exceed 100 characters.")]
    [Display(Name = "Category Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a category type.")]
    [Display(Name = "Category Type")]
    public int? CategoryTypeId { get; set; }

    [Display(Name = "Lifestyle Tag")]
    public string? LifestyleTag { get; set; }
}
