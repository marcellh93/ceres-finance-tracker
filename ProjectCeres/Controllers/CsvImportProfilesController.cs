// ProjectCeres/Controllers/CsvImportProfilesController.cs
//
// Migrated to SPA on 2026-05-06 — see docs/planning-phase3-spa-migration.md §8 row 7.
// Profile CRUD lives on /api/import-profiles (ImportProfilesApiController). The
// remaining GETs 302-redirect legacy URLs to the SPA at /app/import/profiles*.
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class ImportProfilesController : Controller
{
    public IActionResult Index()              => Redirect("/app/import/profiles");
    public IActionResult Create()             => Redirect("/app/import/profiles/new");
    public IActionResult Edit(Guid id)        => Redirect($"/app/import/profiles/{id}/edit");
    public IActionResult Delete(Guid id)      => Redirect("/app/import/profiles");
}
