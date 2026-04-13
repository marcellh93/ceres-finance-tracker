using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers;

public class DashboardController(IDashboardService dashboardService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var data = await dashboardService.GetDashboardDataAsync();
        return View(data);
    }
}
