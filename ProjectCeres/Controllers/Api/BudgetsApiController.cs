using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/budgets")]
public class BudgetsApiController(AppDbContext db) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BudgetDiscriminatorDto>> GetKind(Guid id)
    {
        if (await db.CategoryBudgets.AnyAsync(cb => cb.Id == id))
            return new BudgetDiscriminatorDto(id, "CategoryBudget");

        if (await db.Budgets.AnyAsync(b => b.Id == id))
            return new BudgetDiscriminatorDto(id, "GoalBudget");

        return NotFound();
    }
}
