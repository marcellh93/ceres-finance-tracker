using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/categories")]
public class CategoriesApiController(AppDbContext db) : ControllerBase
{
    [HttpGet("active")]
    public async Task<IReadOnlyList<CategoryOptionDto>> GetActive()
    {
        return await db.Categories
            .Where(c => c.IsActive && !c.IsSystem)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryOptionDto(
                c.Id,
                c.Name,
                c.CategoryType.Name))
            .ToListAsync();
    }
}
