using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesApiController(
    AppDbContext db,
    ICategoryService categoryService,
    ICurrentUserAccessor user) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryListItemDto>>> Get(
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? typeId = null)
    {
        var query = db.Categories
            .Include(c => c.CategoryType)
            .OwnedOrShared(user)
            .AsQueryable();

        if (!includeInactive) query = query.Where(c => c.IsActive);
        if (typeId.HasValue)  query = query.Where(c => c.CategoryTypeId == typeId.Value);

        var rows = await query
            .OrderBy(c => c.CategoryType.Name)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryListItemDto(
                c.Id, c.Name, c.CategoryTypeId, c.CategoryType.Name,
                c.LifestyleTag, c.IsActive, c.IsSystem))
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("active")]
    public async Task<IReadOnlyList<CategoryOptionDto>> GetActive()
    {
        return await db.Categories
            .OwnedOrShared(user)
            .Where(c => c.IsActive && !c.IsSystem)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryOptionDto(c.Id, c.Name, c.CategoryType.Name))
            .ToListAsync();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryDetailDto>> GetById(Guid id)
    {
        var c = await db.Categories
            .Include(x => x.CategoryType)
            .OwnedOrShared(user)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        return new CategoryDetailDto(
            c.Id, c.Name, c.CategoryTypeId, c.CategoryType.Name,
            c.LifestyleTag, c.IsActive, c.IsSystem);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        var result = await categoryService.TryCreateAsync(request);
        return result.IsSuccess
            ? Created($"/api/categories/{result.Value!.Id}", ToDetail(result.Value!))
            : ToErrorResponse(result.Error!.Value);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCategoryRequest request)
    {
        var result = await categoryService.TryUpdateAsync(id, request);
        return result.IsSuccess
            ? Ok(ToDetail(result.Value!))
            : ToErrorResponse(result.Error!.Value);
    }

    [HttpPatch("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id)
    {
        var result = await categoryService.TryDeactivateAsync(id);
        return result.IsSuccess
            ? NoContent()
            : ToErrorResponse(result.Error!.Value);
    }

    [HttpPatch("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var result = await categoryService.TryReactivateAsync(id);
        return result.IsSuccess
            ? NoContent()
            : ToErrorResponse(result.Error!.Value);
    }

    private static CategoryDetailDto ToDetail(Models.Category c) => new(
        c.Id, c.Name, c.CategoryTypeId, c.CategoryType.Name,
        c.LifestyleTag, c.IsActive, c.IsSystem);

    private IActionResult ToErrorResponse(ResultError error) => error.Code switch
    {
        "NOT_FOUND"                  => NotFound(),
        CategoryPolicies.CategoryInUseCode    => Conflict(Envelope(error)),
        CategoryPolicies.SystemImmutableCode  => UnprocessableEntity(Envelope(error)),
        "INVALID_CATEGORY_TYPE"      => UnprocessableEntity(Envelope(error)),
        _                            => UnprocessableEntity(Envelope(error)),
    };

    private static object Envelope(ResultError error) => new
    {
        error = new { code = error.Code, message = error.Message, details = Array.Empty<object>() }
    };
}

[ApiController]
[Route("api/category-types")]
[Authorize]
public class CategoryTypesApiController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<CategoryTypeDto>> Get() =>
        await db.CategoryTypes
            .OrderBy(t => t.Id)
            .Select(t => new CategoryTypeDto(t.Id, t.Name))
            .ToListAsync();
}
