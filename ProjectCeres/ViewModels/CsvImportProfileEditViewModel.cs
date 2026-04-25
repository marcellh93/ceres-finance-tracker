// ProjectCeres/ViewModels/CsvImportProfileEditViewModel.cs
using System.ComponentModel.DataAnnotations;
using ProjectCeres.Models;

namespace ProjectCeres.ViewModels;

public class ImportProfileEditViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public ImportFormat Format { get; set; }
    public ImportColumnMappings Mappings { get; set; } = new();
}
