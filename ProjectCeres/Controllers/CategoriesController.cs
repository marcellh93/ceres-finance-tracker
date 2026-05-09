using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

/// <summary>
/// Razor categories UI is replaced by the SPA at /app/categories. This
/// controller exists only to 302-redirect any in-flight bookmarks. Use 302
/// (not 301) so browsers don't aggressively cache during the SPA migration
/// window. The redirect is removed entirely in the final SPA-cutover
/// cleanup batch.
/// </summary>
[Authorize]
public class CategoriesController : Controller
{
    public IActionResult Index() => Redirect("/app/categories");
    public IActionResult Create() => Redirect("/app/categories/new");
    public IActionResult Edit(Guid id) => Redirect($"/app/categories/{id}/edit");
    public IActionResult Deactivate(Guid id) => Redirect("/app/categories");
}
