using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers;

public class DashboardController(IDashboardService dashboardService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var data     = await dashboardService.GetDashboardDataAsync();
        var snapshot = await dashboardService.GetHealthSnapshotAsync();
        ViewBag.HealthSnapshot = snapshot;
        return View(data);
    }
}
