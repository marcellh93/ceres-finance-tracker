using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

/// <summary>
/// Razor accounts UI is replaced by the SPA at /app/accounts. This controller
/// exists only to 302-redirect any in-flight bookmarks. Use 302 (not 301) so
/// browsers don't aggressively cache during the SPA migration window. The
/// redirects are removed entirely in the final SPA-cutover cleanup batch.
/// </summary>
[Authorize]
public class AccountsController : Controller
{
    public IActionResult Index()             => Redirect("/app/accounts");
    public IActionResult Create()            => Redirect("/app/accounts/new");
    public IActionResult Edit(Guid id)       => Redirect($"/app/accounts/{id}/edit");
    public IActionResult Deactivate(Guid id) => Redirect("/app/accounts");
    public IActionResult Ledger(Guid id)     => Redirect($"/app/accounts/{id}/ledger");
}
