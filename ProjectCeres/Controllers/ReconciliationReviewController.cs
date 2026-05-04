using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class ReconciliationReviewController : Controller
{
    public IActionResult Index()           => Redirect("/app/review?tab=reconciliations");
    public IActionResult Confirm(Guid id)  => Redirect("/app/review?tab=reconciliations");
    public IActionResult ConfirmAll()      => Redirect("/app/review?tab=reconciliations");
    public IActionResult Dispute(Guid id)  => Redirect("/app/review?tab=reconciliations");
}
