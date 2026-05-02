using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public record CategoryListItemDto(
    Guid    Id,
    string  Name,
    int     CategoryTypeId,
    string  CategoryTypeName,
    string? LifestyleTag,
    bool    IsActive,
    bool    IsSystem);

public record CategoryDetailDto(
    Guid    Id,
    string  Name,
    int     CategoryTypeId,
    string  CategoryTypeName,
    string? LifestyleTag,
    bool    IsActive,
    bool    IsSystem);

public record CategoryTypeDto(int Id, string Name);

public record CreateCategoryRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required] int? CategoryTypeId,
    string? LifestyleTag);

public record UpdateCategoryRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    string? LifestyleTag);
