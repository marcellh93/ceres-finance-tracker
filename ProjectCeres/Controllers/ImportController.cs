// ProjectCeres/Controllers/ImportController.cs
//
// Migrated to SPA on 2026-05-06 — see docs/planning-phase3-spa-migration.md §8 row 7.
// All POST actions live on /api/import (ImportApiController) and /api/import-profiles
// (ImportProfilesApiController). The remaining GETs 302-redirect legacy URLs to the SPA.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

[Authorize]
public class ImportController : Controller
{
    public IActionResult Index()   => Redirect("/app/import");
    public IActionResult Summary() => Redirect("/app/import");
}
