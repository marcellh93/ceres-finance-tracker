using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class BudgetsController : Controller
{
    [HttpGet] public IActionResult Index()                  => Redirect("/app/budgets?type=category");
    [HttpGet] public IActionResult Goals()                  => Redirect("/app/budgets?type=goal");
    [HttpGet] public IActionResult Create()                 => Redirect("/app/budgets/new?type=category");
    [HttpGet] public IActionResult CreateGoal()             => Redirect("/app/budgets/new?type=spending");
    [HttpGet] public IActionResult Edit(Guid id)            => Redirect($"/app/budgets/{id}/edit");
    [HttpGet] public IActionResult EditGoal(Guid id)        => Redirect($"/app/budgets/{id}/edit");
    [HttpGet] public IActionResult Deactivate(Guid id)      => Redirect($"/app/budgets/{id}/edit");
    [HttpGet] public IActionResult DeactivateGoal(Guid id)  => Redirect($"/app/budgets/{id}/edit");
}
