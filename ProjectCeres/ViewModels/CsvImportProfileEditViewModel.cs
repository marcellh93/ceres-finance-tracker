using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class CsvImportProfileEditViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public CsvColumnMappings Mappings { get; set; } = new();
}
