using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

[Authorize]
public class DashboardController : Controller
{
    public IActionResult Index() => Redirect("/app/");
}
