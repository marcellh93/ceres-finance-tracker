using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class TransferReviewController : Controller
{
    public IActionResult Index() => Redirect("/app/review?tab=transfers");
    public IActionResult LinkToExisting(Guid stagedId, Guid otherAccountId) => Redirect("/app/review?tab=transfers");
    public IActionResult CreateAsTransfer(Guid stagedId, Guid otherAccountId) => Redirect("/app/review?tab=transfers");
    public IActionResult DismissAsTransaction(Guid stagedId) => Redirect("/app/review?tab=transfers");
}
