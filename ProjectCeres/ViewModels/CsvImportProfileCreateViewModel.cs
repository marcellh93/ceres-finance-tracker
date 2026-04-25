// ProjectCeres/ViewModels/CsvImportProfileCreateViewModel.cs
using System.ComponentModel.DataAnnotations;
using ProjectCeres.Models;

namespace ProjectCeres.ViewModels;

public class ImportProfileCreateViewModel
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public ImportFormat Format { get; set; } = ImportFormat.Csv;
    public ImportColumnMappings Mappings { get; set; } = new();
}
