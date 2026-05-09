using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

/// <summary>
/// Razor settings UI is replaced by the SPA at /app/settings. This controller
/// exists only to 302-redirect any in-flight bookmarks. Use 302 (not 301) so
/// browsers don't aggressively cache during the SPA migration window.
/// The redirect is removed entirely in the final SPA-cutover cleanup batch.
/// </summary>
[Authorize]
public class SettingsController : Controller
{
    public IActionResult Edit() => Redirect("/app/settings");
}
