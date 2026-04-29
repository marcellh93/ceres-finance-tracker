using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class AppController : Controller
{
    public IActionResult Index() => View();
}
