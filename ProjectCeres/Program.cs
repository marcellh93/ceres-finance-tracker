using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Filters;
using ProjectCeres.ModelBinders;
using ProjectCeres.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<NumberFormatActionFilter>();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<NumberFormatActionFilter>();
    options.ModelBinderProviders.Insert(0, new DecimalModelBinderProvider());
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<IRecurringTransactionService, RecurringTransactionService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IFileAttachmentService, FileAttachmentService>();
builder.Services.AddScoped<IBudgetService,         BudgetService>();

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
