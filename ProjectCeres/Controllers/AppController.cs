using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

[AllowAnonymous]
public class AppController : Controller
{
    public IActionResult Index() => View();
}
