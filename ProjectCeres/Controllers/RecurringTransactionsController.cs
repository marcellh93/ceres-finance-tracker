using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

[Authorize]
public class RecurringTransactionsController : Controller
{
    public IActionResult Index()             => Redirect("/app/recurring");
    public IActionResult Create()            => Redirect("/app/recurring/new");
    public IActionResult Edit(Guid id)       => Redirect($"/app/recurring/{id}/edit");
    public IActionResult Confirm(Guid id)    => Redirect("/app/recurring");
    public IActionResult Dismiss(Guid id)    => Redirect("/app/recurring");
    public IActionResult Deactivate(Guid id) => Redirect("/app/recurring");
    public IActionResult Upcoming()          => Redirect("/app/recurring");
}
