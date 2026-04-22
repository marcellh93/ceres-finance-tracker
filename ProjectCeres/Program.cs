using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Filters;
using ProjectCeres.ModelBinders;
using ProjectCeres.Services;
using ProjectCeres.Services.Reports;
using Vite.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<NumberFormatActionFilter>();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<NumberFormatActionFilter>();
    options.ModelBinderProviders.Insert(0, new DecimalModelBinderProvider());
});

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(err => new
                {
                    field = e.Key,
                    message = err.ErrorMessage
                }));

            return new Microsoft.AspNetCore.Mvc.UnprocessableEntityObjectResult(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "One or more fields are invalid.",
                    details = errors
                }
            });
        };
    });

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ILiabilityPaymentService, LiabilityPaymentService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<IRecurringTransactionService, RecurringTransactionService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<NetWorthGenerator>();
builder.Services.AddScoped<IncomeExpenseGenerator>();
builder.Services.AddScoped<ExpenseBreakdownGenerator>();
builder.Services.AddScoped<TransactionHistoryGenerator>();
builder.Services.AddScoped<ReportGeneratorFactory>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IFileAttachmentService, FileAttachmentService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<ICategoryBudgetService, CategoryBudgetService>();
builder.Services.AddViteServices();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.UseViteDevelopmentServer(useMiddleware: true);

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Guarantee the Settings row exists before handling any requests.
using (var scope = app.Services.CreateScope())
{
    var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
    await settingsService.EnsureExistsAsync();
}

app.Run();

public partial class Program { }
