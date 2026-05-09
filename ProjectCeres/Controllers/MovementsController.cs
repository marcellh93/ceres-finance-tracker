using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

[Authorize]
public class MovementsController : Controller
{
    public IActionResult Index() => Redirect("/app/movements");
}
