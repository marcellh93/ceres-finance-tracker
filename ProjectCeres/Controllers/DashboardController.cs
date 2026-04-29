using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class DashboardController : Controller
{
    public IActionResult Index() => Redirect("/app/");
}
