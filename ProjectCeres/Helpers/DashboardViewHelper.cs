namespace ProjectCeres.Helpers;

public static class DashboardViewHelper
{
    public static string RunwayCssClass(decimal months) => months switch
    {
        > 6m  => "text-green-600",
        >= 3m => "text-amber-600",
        _     => "text-red-600"
    };
}
