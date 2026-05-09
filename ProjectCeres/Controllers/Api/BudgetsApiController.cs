using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/budgets")]
[Authorize]
public class BudgetsApiController(AppDbContext db, ICurrentUserAccessor user) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BudgetDiscriminatorDto>> GetKind(Guid id)
    {
        if (await db.CategoryBudgets.Owned(user).AnyAsync(cb => cb.Id == id))
            return new BudgetDiscriminatorDto(id, "CategoryBudget");

        if (await db.Budgets.Owned(user).AnyAsync(b => b.Id == id))
            return new BudgetDiscriminatorDto(id, "GoalBudget");

        return NotFound();
    }
}
