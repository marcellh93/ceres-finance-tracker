using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class MovementsController : Controller
{
    public IActionResult Index() => Redirect("/app/movements");
}
