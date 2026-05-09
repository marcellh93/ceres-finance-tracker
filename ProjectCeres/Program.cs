using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Filters;
using ProjectCeres.ModelBinders;
using ProjectCeres.Models;
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

// Phase 3 Stage 6a: HttpContext-backed accessor replaces SingleUserAccessor.
// SingleUserAccessor stays in the codebase because Stage 7's data remap references
// the sentinel constant; only the DI registration changes here.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
builder.Services.AddScoped<UserOwnershipInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.AddInterceptors(sp.GetRequiredService<UserOwnershipInterceptor>());
});

// === Stage 6a: ASP.NET Identity + Argon2id + custom session model ===

builder.Services.Configure<Argon2idOptions>(
    builder.Configuration.GetSection("Authentication:Argon2id"));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;

        options.Lockout.MaxFailedAccessAttempts = 10;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;

        // Length minimum is enforced dynamically by MfaAwareLengthValidator
        // (15 pre-MFA, 8 post-MFA). The 8 here is the Identity floor.
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddPasswordValidator<MfaAwareLengthValidator>()
    .AddPasswordValidator<BreachedPasswordValidator>()
    .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>();

builder.Services.Configure<SecurityStampValidatorOptions>(o =>
{
    o.ValidationInterval = TimeSpan.FromMinutes(5);
});

// Replace Identity's PBKDF2 hasher with Argon2id (pinned m=19456 t=2 p=1).
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, Argon2idPasswordHasher>();
builder.Services.AddScoped<Argon2idPasswordHasher>();
builder.Services.AddScoped<PersistentTokenService>();
builder.Services.AddScoped<MfaBackupCodeService>();
builder.Services.AddScoped<TotpReplayGuard>();
builder.Services.AddScoped<FailedLoginRecorder>();

builder.Services.AddHttpClient<IBreachedPasswordChecker, HaveIBeenPwnedPasswordChecker>();

// SecurePolicy: Always in production (browser enforces __Host- prefix Secure attribute);
// SameAsRequest outside production so WebApplicationFactory tests over HTTP can exercise
// the antiforgery + cookie pipeline. Browsers don't enter the picture in tests.
var cookieSecurePolicy = builder.Environment.IsProduction()
    ? CookieSecurePolicy.Always
    : CookieSecurePolicy.SameAsRequest;

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = SessionConstants.SessionCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.LoginPath = PathString.Empty;
    options.AccessDeniedPath = PathString.Empty;
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
    options.Events.OnValidatePrincipal = SessionRevocationValidator.ValidateAsync;

});

builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
    IdentityConstants.TwoFactorUserIdScheme,
    options =>
    {
        // Stage 6b.2: tighten the gap between password step and TOTP step.
        // Default inherited 30-min sliding TTL is far too long — a phisher who
        // captures the password could try ~hundreds of TOTP codes within that
        // window. 5 min with no sliding extension = strict human-timescale gate.
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;
        // Force IsPersistent so the browser receives an explicit Expires/max-age
        // header instead of a session-scoped cookie. Without this, ExpireTimeSpan
        // is enforced server-side but invisible to the client and to tests.
        options.Events.OnSigningIn = ctx =>
        {
            ctx.Properties.IsPersistent = true;
            ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = SessionConstants.CsrfCookieName;
    options.Cookie.HttpOnly = false;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.HeaderName = SessionConstants.CsrfHeaderName;
});

builder.Services.Configure<MvcOptions>(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                code = "RATE_LIMITED",
                message = "Too many requests. Please retry shortly.",
            }
        }, ct);
    };

    options.AddPolicy(AuthRateLimitPolicies.AuthLoginByIp, httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });

    options.AddPolicy<string, TotpByUserPartitioner>(AuthRateLimitPolicies.AuthTotpByUser);
});

// === End Stage 6a wiring ===

builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ILiabilityPaymentService, LiabilityPaymentService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<ITransactionExportService, TransactionExportService>();
builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<IRecurringTransactionService, RecurringTransactionService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<NetWorthGenerator>();
builder.Services.AddScoped<IncomeExpenseGenerator>();
builder.Services.AddScoped<ExpenseBreakdownGenerator>();
builder.Services.AddScoped<TransactionHistoryGenerator>();
builder.Services.AddScoped<BudgetVsActualReportGenerator>();
builder.Services.AddScoped<LargestExpensesReportGenerator>();
builder.Services.AddScoped<MonthlyCashFlowReportGenerator>();
builder.Services.AddScoped<NetWorthOverTimeReportGenerator>();
builder.Services.AddScoped<ReportGeneratorFactory>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IFileAttachmentService, FileAttachmentService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<ICategoryBudgetService, CategoryBudgetService>();
builder.Services.AddScoped<IMovementService, MovementService>();
builder.Services.AddScoped<IMovementExportService, MovementExportService>();
builder.Services.AddScoped<IImportProfileService, ImportProfileService>();
builder.Services.AddSingleton<CsvImportParser>();
builder.Services.AddSingleton<ExcelImportParser>();
builder.Services.AddSingleton<ImportParserFactory>();
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<ITransferDetectionService, TransferDetectionService>();
builder.Services.AddScoped<ITransferReviewService, TransferReviewService>();
builder.Services.AddScoped<IImportStagedTransactionService, ImportStagedTransactionService>();
builder.Services.AddScoped<IHeaderDetectionService, HeaderDetectionService>();
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

app.UseRateLimiter();

app.UseMiddleware<PersistentCookieRotationMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<UserBlockedIpMiddleware>();

if (app.Environment.IsDevelopment())
    app.UseViteDevelopmentServer(useMiddleware: true);

app.MapControllers();
app.MapControllerRoute(
    name: "app",
    pattern: "app/{*path}",
    defaults: new { controller = "App", action = "Index" });
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Stage 6a: removed startup EnsureExistsAsync hook. With HttpContextCurrentUserAccessor,
// no HttpContext exists at startup so the call would throw. Stage 7's data remap
// creates the per-user Settings row on first registration.

app.Run();

public partial class Program { }

internal sealed class TotpByUserPartitioner : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // Synchronous partition derivation by blocking on the cookie decode.
        // Cookie is Identity.TwoFactorUserId — a scoped cookie issued by SignInManager
        // when RequiresTwoFactor. We resolve it via AuthenticateAsync inline.
        var task = httpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        task.Wait();
        var userId = task.Result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? AuthRateLimitPolicies.AnonymousTotpPartition;

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    }
}
