using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProjectCeres.Services;

namespace ProjectCeres.Filters;

/// <summary>
/// Global action filter that sets ViewData["NumberFormat"] before every controller action
/// so all views can format and pre-fill decimal inputs using the user's setting.
/// </summary>
public class NumberFormatActionFilter(ISettingsService settingsService) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.Controller is Controller controller)
        {
            var settings = await settingsService.GetAsync();
            controller.ViewData["NumberFormat"] = settings.NumberFormat;
        }

        await next();
    }
}
