using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectCeres.Admin;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Services.Reports;
using Resend;
using Vite.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// AddControllersWithViews (not AddControllers) registers the antiforgery filter
// infrastructure AutoValidateAntiforgeryTokenAttribute needs (dotnet/aspnetcore#22189).
// No Razor views remain; this is the service superset only — no view routing.
builder.Services.AddControllersWithViews()
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

            return new UnprocessableEntityObjectResult(new
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

// ADR-0078 — shelve import + review endpoints from the beta (Production).
// Enabled (endpoints live) in Development, E2E, and Testing; fenced (404) everywhere else.
// A middleware fence (added to app below, before UseRouting) short-circuits before the
// auth FallbackPolicy can issue a 401, ensuring a clean 404 for unauthenticated callers.
var importAndReviewEnabled = builder.Environment.IsDevelopment()
    || builder.Environment.IsEnvironment("E2E")
    || builder.Environment.IsEnvironment("Testing");

// Phase 3 Stage 7: background-job scope primitive. Singleton — the AsyncLocal inside
// does the per-flow isolation; the holder is process-wide. IUserJobRunner is scoped
// because it depends on the scoped AppDbContext.
builder.Services.AddSingleton<IUserScope, UserScope>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IBackgroundJobScope, BackgroundJobScope>();
builder.Services.AddScoped<IUserJobRunner, UserJobRunner>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
builder.Services.AddScoped<UserOwnershipInterceptor>();

// Stage 7.5 / ADR-0068 — PostgreSQL Row-Level Security defence in depth.
// Stage 7.6.7 / ADR-0073: IPreAuthCallSiteTagger registry deleted; pre-auth call sites
// are tagged by the [PreAuthCallSite] attribute on the action method itself.
builder.Services.AddScoped<RowLevelSecurityInterceptor>();
// Stage 7.6.2: RlsExceptionTranslator is a static helper invoked from
// AppDbContext.SaveChangesAsync's catch block. No DI registration needed.

// AppDbContext — runtime, bound to ceres_app (NOBYPASSRLS). Every command issued
// against this DbContext is filtered by Postgres RLS using the GUC set by the
// RowLevelSecurityInterceptor.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("ApplicationConnection"));
    options.AddInterceptors(sp.GetRequiredService<UserOwnershipInterceptor>());
    options.AddInterceptors(sp.GetRequiredService<RowLevelSecurityInterceptor>());
});

// AdminDbContext — cross-tenant variant, bound to ceres_admin (BYPASSRLS). Used by
// IUserJobRunner for background-job user enumeration and by future Admin/* services.
// Intentionally NOT wired with RowLevelSecurityInterceptor — ceres_admin bypasses RLS.
builder.Services.AddDbContext<AdminDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("AdminConnection"));
    options.AddInterceptors(sp.GetRequiredService<UserOwnershipInterceptor>());
});

// === Stage 6a: ASP.NET Identity + Argon2id + custom session model ===

builder.Services.Configure<Argon2idOptions>(
    builder.Configuration.GetSection("Authentication:Argon2id"));

// Stage 6.15 — HMAC-SHA256-based token-lookup hasher for /password-reset/confirm,
// /email-change/confirm, /email-change/revoke. Closes the Argon2id-amplification
// DoS on those endpoints (verify cost is O(1) regardless of token table size).
builder.Services.Configure<TokenLookupOptions>(
    builder.Configuration.GetSection("Authentication:TokenLookupSecret"));
builder.Services.AddSingleton<TokenLookupHasher>();

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

// Stage 9.1.5.b §4.6: replace Identity's UpperInvariantLookupNormalizer with a
// lowercase variant so FailedLoginRecorder, LockoutCache, and
// UserManager.NormalizeEmail all produce identical email keys. The lowercase
// choice preserves the prior FailedLoginRecorder.TruncateAndNormalize semantics
// (which existing tests assert against). Registered AFTER AddIdentity so this
// registration overrides Identity's default.
builder.Services.AddSingleton<ILookupNormalizer, LowercaseLookupNormalizer>();

builder.Services.Configure<SecurityStampValidatorOptions>(o =>
{
    o.ValidationInterval = TimeSpan.FromMinutes(5);
});

// Replace Identity's PBKDF2 hasher with Argon2id (pinned m=19456 t=2 p=1).
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, Argon2idPasswordHasher>();
builder.Services.AddScoped<Argon2idPasswordHasher>();
builder.Services.AddScoped<PersistentTokenService>();
builder.Services.AddScoped<PasswordResetTokenGenerator>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<EmailConfirmationTokenGenerator>();
builder.Services.AddScoped<EmailConfirmationService>();
builder.Services.AddScoped<EmailChangeTokenGenerator>();
builder.Services.AddScoped<EmailChangeService>();
builder.Services.AddScoped<LockoutUnlockTokenGenerator>();
builder.Services.AddScoped<LockoutUnlockService>();
builder.Services.AddMemoryCache();
builder.Services.AddOptions<LockoutCacheOptions>()
    .Validate(o => o.IpPointerTtl > TimeSpan.Zero, "LockoutCacheOptions.IpPointerTtl must be positive.");
builder.Services.AddSingleton<LockoutCache>();

// === Email service registration (Stage 8c) ===
// Production must fail loud if Email:Resend:ApiKey is unbound — silently
// falling back to LogOnlyEmailService would drop every transactional email
// with no signal. Dev/Test without a key keeps using LogOnlyEmailService.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));


var resendApiKey = builder.Configuration["Email:Resend:ApiKey"];
if (builder.Environment.IsEnvironment("E2E"))
{
    // E2E: file-sink so Playwright can read verify/reset/unlock links. Keyed on
    // environment (not key-absence) so a machine-level Email__Resend__ApiKey can
    // never flip E2E to real sends. Singleton matches LogOnlyEmailService.
    var sinkDir = builder.Configuration["Email:FileSink:Directory"]
        ?? throw new InvalidOperationException("Email:FileSink:Directory is required under E2E.");
    builder.Services.AddSingleton<IEmailService>(sp =>
        new FileSinkEmailService(sinkDir,
            sp.GetRequiredService<ILogger<FileSinkEmailService>>()));
}
else if (string.IsNullOrWhiteSpace(resendApiKey))
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "Email:Resend:ApiKey is required in Production. " +
            "Set the Email__Resend__ApiKey environment variable.");
    }
    builder.Services.AddSingleton<IEmailService, LogOnlyEmailService>();
}
else
{
    // Stage 9.6.1 (2026-05-18) — the Resend SDK reads its API token from
    // IOptions<ResendClientOptions>.ApiToken, NOT from the HttpClient's
    // DefaultRequestHeaders.Authorization. Pre-fix we configured the wrong
    // knob so every send hit Resend's API with no usable token and got back
    // 401 "API key is invalid" — even though the key was valid and a raw
    // curl with the same key succeeded. The README's documented pattern is
    // `AddResend(o => o.ApiToken = ...)`. See
    // https://github.com/resend/resend-dotnet for the README reference.
    builder.Services.AddResend(o => o.ApiToken = resendApiKey);
    builder.Services.AddScoped<IEmailService, ResendEmailService>();
}

builder.Services.AddScoped<IEmailRecipientResolver, EmailRecipientResolver>();
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddScoped<IEmailComposer, EmailComposer>();
builder.Services.AddScoped<ILanguageResolver, LanguageResolver>();

// Stage 8e: Svix HMAC verifier for the Resend webhook controller. Stateless — singleton.
builder.Services.AddSingleton<IResendSignatureVerifier, ResendSignatureVerifier>();

builder.Services.AddScoped<MfaBackupCodeService>();
builder.Services.AddScoped<TotpReplayGuard>();
builder.Services.AddScoped<FailedLoginRecorder>();
builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();
builder.Services.AddSingleton<IAuthorizationHandler, RecentAuthRequirementHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AdminLiveRequirementHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, RecentAuthMiddlewareResultHandler>();

if (builder.Environment.IsEnvironment("E2E"))
{
    builder.Services.AddSingleton<IBreachedPasswordChecker, AlwaysAllowBreachedPasswordChecker>();
}
else
{
    builder.Services.AddHttpClient<IBreachedPasswordChecker, HaveIBeenPwnedPasswordChecker>();
}

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

    options.AddPolicy(RequireRecentAuthAttribute.PolicyName, p =>
        p.AddRequirements(new RecentAuthRequirement()));

    options.AddPolicy(RequireAdminAttribute.PolicyName, p =>
        p.RequireAuthenticatedUser().AddRequirements(new AdminLiveRequirement()));
});

var rateLimitOptions = new ProjectCeres.Common.RateLimiting.RateLimitOptions();
builder.Configuration.GetSection("RateLimits").Bind(rateLimitOptions);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        // SlidingWindowRateLimiter does not populate RetryAfter metadata on its denied
        // lease. Fall back to the segment duration (Window / SegmentsPerWindow = 15 s)
        // so the header is always present, as required by the API contract.
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)retryAfter.TotalSeconds
            : 15;
        context.HttpContext.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        // Stage 9.1.5.b: when rejecting POST /api/auth/login (the password step — NOT
        // /api/auth/login/totp, which uses a per-user AuthTotpByUser limiter unrelated
        // to account lockout), consult LockoutCache. If the requesting IP's
        // last-attempted email is known to be locked, surface the ACCOUNT_LOCKED_OUT
        // envelope (401) instead of RATE_LIMITED (429). Memory-only reads — no DB
        // query — so the DoS-amplification concern in security-model.md § lockout is
        // preserved.
        //
        // Exact-match on PathString (case-insensitive + trailing-slash-normalized per
        // ASP.NET conventions) excludes /api/auth/login/totp without a separate
        // exclusion clause.
        if (context.HttpContext.Request.Path == "/api/auth/login")
        {
            var lockoutCache = context.HttpContext.RequestServices
                .GetRequiredService<LockoutCache>();
            // Match the AuthLoginByIp rate-limiter partition fallback so the cache key
            // aligns when Connection.RemoteIpAddress is null (e.g. TestServer requests).
            var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (lockoutCache.TryGetLastLockedEmailForIp(ip, out var lockedEmail, out _)
                && lockoutCache.TryGetLockoutEnd(lockedEmail, out _))
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = new
                    {
                        code = "ACCOUNT_LOCKED_OUT",
                        message = ProjectCeres.Common.Authentication.AuthMessages.AccountTemporarilyLockedFifteenMinutes,
                    }
                }, ct);
                return;
            }
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
            PermitLimit = rateLimitOptions.LoginByIpPermitLimit,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });

    options.AddPolicy(AuthRateLimitPolicies.AuthCsrfByIp, httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.CsrfByIpPermitLimit,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });

    options.AddPolicy<string, TotpByUserPartitioner>(AuthRateLimitPolicies.AuthTotpByUser);

    options.AddPolicy(AuthRateLimitPolicies.AuthMfaByUser, httpContext =>
    {
        // Rate limiter runs BEFORE UseAuthentication, so httpContext.User is empty here.
        // Explicitly authenticate against the application cookie scheme to resolve the
        // current user id for per-user partitioning. Mirrors AuthReauthByUser above and
        // TotpByUserPartitioner (which authenticates against TwoFactorUserIdScheme).
        // Stage 6c.2 follow-up: prior to this fix the partition key always fell back to
        // "anonymous-mfa" because User claims hadn't been populated yet, collapsing every
        // authenticated MFA request into a single shared bucket.
        var task = httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        task.Wait();
        var userId = task.Result.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? "anonymous-mfa";
        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });

    options.AddPolicy(AuthRateLimitPolicies.AuthReauthByUser, httpContext =>
    {
        // Rate limiter runs BEFORE UseAuthentication, so httpContext.User is empty here.
        // Explicitly authenticate against the application cookie scheme to resolve the
        // current user id for per-user partitioning. Mirrors the pattern in
        // TotpByUserPartitioner (which authenticates against TwoFactorUserIdScheme).
        var task = httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        task.Wait();
        var userId = task.Result.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? "anonymous-reauth";
        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        });
    });

    options.AddPolicy(AuthRateLimitPolicies.EmailByUser, httpContext =>
    {
        // Stage 8d. Partition by the NORMALIZED email from the JSON body when present,
        // so unknown and known emails go through the same limiter path on
        // /password-reset/request — preserving the Stage 6.16 timing-channel fix.
        // Fallbacks: authenticated NameIdentifier (for /email-change/request whose body
        // carries "newEmail", not "email"), then client IP, then a fixed constant.
        var key = EmailPartitionHelpers.TryReadEmailFromBody(httpContext)
                  ?? AuthenticateAndGetUserId(httpContext)
                  ?? httpContext.Connection.RemoteIpAddress?.ToString()
                  ?? "anonymous-email";
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(60),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        });

        static string? AuthenticateAndGetUserId(HttpContext ctx)
        {
            // Mirrors AuthReauthByUser / AuthMfaByUser. Rate limiter middleware runs
            // before UseAuthentication, so ctx.User is empty here — explicitly decode
            // the application cookie to pick up the NameIdentifier claim.
            var task = ctx.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            task.Wait();
            return task.Result.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    });

    // Stage 8d. Per-IP backstop on email-triggering endpoints — 10/hr/IP. Catches an
    // attacker that rotates the "email" payload across many addresses from a single
    // source to dodge the per-email EmailByUser bucket above. Registered as the
    // GlobalLimiter (gated by [ApplyEmailIpRateLimit] endpoint metadata) because
    // EnableRateLimitingAttribute is declared AllowMultiple=false, so we cannot
    // stack a second [EnableRateLimiting] on the same action to run the IP bucket
    // alongside EmailByUser. Endpoints without the marker return GetNoLimiter and
    // skip the bucket entirely. The named policy AuthRateLimitPolicies.EmailByIp is
    // also registered below so test infrastructure can reference it by name.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var marker = httpContext.GetEndpoint()?.Metadata.GetMetadata<ApplyEmailIpRateLimitAttribute>();
        if (marker is null)
            return RateLimitPartition.GetNoLimiter("no-email-ip-limit");

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter($"email-by-ip:{ip}",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.EmailByIpPermitLimit,
                Window = TimeSpan.FromMinutes(60),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
    });

    // EmailByIp is also addressable by name (e.g. for tests that want to query the
    // policy registry by AuthRateLimitPolicies.EmailByIp) although in production it
    // is enforced via GlobalLimiter above — endpoints do NOT attach this via
    // [EnableRateLimiting]. The named policy uses the same parameters so semantics
    // line up exactly with the global limiter.
    options.AddPolicy(AuthRateLimitPolicies.EmailByIp, httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(60),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        });
    });
});

// === End Stage 6a wiring ===

builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<CategorySeedService>();
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
builder.Services.Configure<FileAttachmentOptions>(
    builder.Configuration.GetSection("FileAttachments"));
builder.Services.AddScoped<IFileAttachmentService, FileAttachmentService>();
builder.Services.AddScoped<ISupportTicketService, SupportTicketService>();
builder.Services.AddScoped<ISupportRecipientResolver, SupportRecipientResolver>();

// Stage 12.5 — support-ticket notifications have nowhere to go without this.
// Production fails to boot rather than accept tickets nobody will read; other
// environments skip the notification so tests and fresh clones need no mailbox.
//
// Deliberately AFTER the Email:Resend:ApiKey guard above: that one is the more
// fundamental failure (no mail at all, not just no support mail), and
// ResendEmailServiceTests.Production_without_api_key_throws_at_startup pins its
// message. Checking SupportAddress first would mask it.
if (builder.Environment.IsProduction()
    && string.IsNullOrWhiteSpace(builder.Configuration["Email:SupportAddress"]))
{
    throw new InvalidOperationException(
        "Email:SupportAddress is required in Production. " +
        "Set the Email__SupportAddress environment variable to the mailbox that " +
        "should receive support-ticket notifications.");
}
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<ISessionService, SessionService>();
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
builder.Services.AddScoped<AdminRoleService>();
builder.Services.AddViteServices();

// Development-only bootstrap tool: creates the first user + remaps sentinel-tagged data.
// Invocation: dotnet run --project ProjectCeres --launch-profile https -- --seed-dev-user --email <addr> --generate-password
//         or: dotnet run --project ProjectCeres --launch-profile https -- --seed-dev-user --email <addr> --password <pw>
// Placed here (after all service registrations, before builder.Build()) so RunAsync
// can call builder.Build() and resolve the fully-configured DI container.
if (args.Length > 0 && args[0] == "--seed-dev-user")
{
    Environment.Exit(await ProjectCeres.Tools.SeedDevUser.RunAsync(builder, args[1..]));
}

var app = builder.Build();

// Stage 9.11 — under E2E, refuse to start unless pointed at a recognized e2e DB.
// The wrapper script never sets E2E:SkipDatabaseGuard, so production E2E runs are
// always guarded; only the DI test sets it (it tests wiring, not the guard).
if (app.Environment.IsEnvironment("E2E")
    && !app.Configuration.GetValue<bool>("E2E:SkipDatabaseGuard"))
{
    var e2eConnection = app.Configuration.GetConnectionString("ApplicationConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:ApplicationConnection is not configured.");
    await E2eDatabaseGuardStartupCheck.EnsureConnectedToE2eDatabaseAsync(e2eConnection);
}

// Stage 7.5 / ADR-0068 — refuse to start if the runtime role can issue DDL. Skipped
// when the test harness explicitly opts out via `Stage75:SkipPrivilegeLeakCheck=true`
// because the WAF spins up the same Program.cs many times per suite and the probe
// round-trip is wasteful per test; production + dev start the app once and the
// check is cheap there.
if (!app.Configuration.GetValue<bool>("Stage75:SkipPrivilegeLeakCheck"))
{
    var applicationConnection = app.Configuration.GetConnectionString("ApplicationConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:ApplicationConnection is not configured.");

    await PrivilegeLeakStartupCheck.EnsureApplicationConnectionLacksDdlAsync(applicationConnection);

    // Stage 9.5b / D3 — refuse to start if any applied user-owned table is missing forced
    // RLS. Pending-migration tables are skipped, so a rolling deploy does not crash.
    using var rlsParityScope = app.Services.CreateScope();
    var rlsParityDb = rlsParityScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await RlsParityStartupCheck.EnsureAppliedUserOwnedTablesAreRlsProtectedAsync(
        rlsParityDb.Model, applicationConnection);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(exApp => exApp.Run(async ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                code = "INTERNAL_ERROR",
                message = "An unexpected error occurred."
            }
        });
    }));
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Vite dev middleware runs BEFORE the auth pipeline. The global authorization
// fallback policy at line 252 requires authentication on every non-[AllowAnonymous]
// endpoint, and Vite-proxied requests (fonts in node_modules, HMR, modules) aren't
// controllers — they have no [AllowAnonymous] to opt out, so without this ordering
// they get 401'd by UseAuthorization. Placing Vite alongside UseStaticFiles makes
// the proxied assets behave like static files: bypass auth entirely.
if (app.Environment.IsDevelopment())
{
    // Vite dev middleware must NOT see general page requests — it would serve
    // its own root index.html for any unmatched path. Scope it to asset-shaped
    // requests + the HMR WebSocket upgrade. Everything else falls through to
    // UseRouting + MapFallbackToFile("dist/app.html"), which serves the SPA
    // shell for all page-level paths (/, /movements, /login, etc.).
    //
    // Vite asset path prefixes (Vite-internal conventions):
    //   /@vite/*        — Vite runtime + HMR client bundle
    //   /@react-refresh — React refresh runtime
    //   /@id/*          — virtual module IDs
    //   /src/*          — application source modules
    //   /node_modules/* — third-party packages (fonts, etc.)
    //   /dist/*         — asset URLs injected by Vite when base='/dist/' is set
    //
    // HMR WebSocket: Vite's HMR client connects to wss://host/?token=...
    // (root path with a token query). Detect via the WebSocket upgrade header.
    //
    // UseWebSockets() MUST stay BEFORE MapWhen because the predicate reads
    // ctx.WebSockets.IsWebSocketRequest, which UseWebSockets() populates.
    static bool IsViteRequest(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value;
        if (path is not null && (
            path.StartsWith("/@vite/", StringComparison.Ordinal) ||
            path.StartsWith("/@react-refresh", StringComparison.Ordinal) ||
            path.StartsWith("/@id/", StringComparison.Ordinal) ||
            path.StartsWith("/src/", StringComparison.Ordinal) ||
            path.StartsWith("/node_modules/", StringComparison.Ordinal) ||
            path.StartsWith("/dist/", StringComparison.Ordinal)))
        {
            return true;
        }
        // HMR WebSocket upgrade at the root path.
        if (ctx.WebSockets.IsWebSocketRequest && ctx.Request.Path == "/")
        {
            return true;
        }
        return false;
    }

    app.UseWebSockets();
    app.MapWhen(IsViteRequest, branch =>
    {
        branch.UseViteDevelopmentServer(useMiddleware: true);
    });
}

app.UseMiddleware<ProjectCeres.Common.Localization.LanguagePreferenceMiddleware>();

// ADR-0078 — shelve import + review endpoints in the beta (Production).
// Placed BEFORE UseRouting so the 404 fires before the auth FallbackPolicy
// can issue a 401. In Development / E2E / Testing importAndReviewEnabled is
// true and this block is skipped, keeping endpoints live for tests and dev.
if (!importAndReviewEnabled)
{
    app.Use(static async (context, next) =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/import", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/reconciliation-review", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/transfer-review", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next(context);
    });
}

// Stage 11 Task 7: one-shot 301 for legacy /app/* bookmarks → /*. Must run BEFORE
// UseRouting — a rewrite is a pre-routing URL transform; placing it after MapControllers
// breaks endpoint dispatch and lets /api/* fall through to the SPA fallback. Query string
// is preserved automatically by RewriteMiddleware. The "^app/" pattern never matches /api.
app.UseRewriter(new RewriteOptions()
    .AddRedirect("^app/(.*)", "$1", statusCode: StatusCodes.Status301MovedPermanently));

app.UseRouting();

app.UseRateLimiter();

// Persistent-cookie rotation runs AFTER UseAuthentication so it can read
// context.User to detect "request carries cookies but didn't authenticate"
// (e.g. session-cookie ticket expired but browser still sending it). Must
// stay BEFORE UseAuthorization so the rotation 401 doesn't get pre-empted
// by an Authorize challenge. See PersistentCookieRotationMiddleware XML
// docs for the 2026-05-22 reordering rationale.
app.UseAuthentication();
app.UseMiddleware<PersistentCookieRotationMiddleware>();
app.UseAuthorization();
app.UseMiddleware<UserBlockedIpMiddleware>();

app.MapControllers();

// API fallthrough (MUST precede the SPA fallback). A /api/* path that matched
// no controller returns JSON — never the SPA HTML — and stops MapFallbackToFile
// from shadowing the Web API (dotnet/aspnetcore#41060). NOT AllowAnonymous: the
// global FallbackPolicy applies, so an anonymous /api/* request is challenged
// (401) and an authenticated-but-unmatched one falls to the 404 handler. This
// preserves the pre-Stage-11 behavior where unmatched authed routes 401'd via
// the fallback policy.
app.MapFallback("api/{*path}", () => Results.NotFound());

// Convenience redirect: the design-system showcase is a separate Vite entry
// point served as a static file, so its real URL is /dist/design-system.html.
// Without this, /design-system falls through to the SPA shell below, React
// Router finds no matching route, and the user sees "Page not found" on a page
// that exists. Anonymous — it is a token reference, not user data.
app.MapGet("/design-system", () => Results.Redirect("/dist/design-system.html"))
   .AllowAnonymous()
   .ExcludeFromDescription();

// Stage 11 Task 5: serve the built SPA for any other unmatched path.
// In manifest/E2E mode this resolves to wwwroot/dist/app.html (the Vite-built
// host carrying hashed asset links). AllowAnonymous is required because the
// global FallbackPolicy requires authentication on every endpoint — the SPA
// shell must be publicly reachable so React Router can render the login page.
app.MapFallbackToFile("dist/app.html").AllowAnonymous();

// Stage 6a: removed startup EnsureExistsAsync hook. With HttpContextCurrentUserAccessor,
// no HttpContext exists at startup so the call would throw. Stage 7's data remap
// creates the per-user Settings row on first registration.

app.Run();

public partial class Program { }

internal static class EmailPartitionHelpers
{
    /// <summary>
    /// Reads the lowercase, trimmed "email" property from a JSON request body for
    /// EmailByUser limiter partitioning. Returns null if the body has no top-level
    /// "email" string property (e.g. /email-change/request whose body carries
    /// "newEmail" — the EmailByUser policy then falls through to the UserId claim).
    ///
    /// Body buffering is required: the rate limiter middleware runs before model
    /// binding, so the body stream has not yet been buffered. We call
    /// <see cref="HttpRequestRewindExtensions.EnableBuffering(HttpRequest)"/>
    /// to allow the position to be reset; the body is rewound before returning so
    /// the model binder downstream sees the full payload.
    /// </summary>
    public static string? TryReadEmailFromBody(HttpContext ctx)
    {
        if (ctx.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) != true)
            return null;

        ctx.Request.EnableBuffering();
        ctx.Request.Body.Position = 0;

        // Read the body buffer asynchronously — Kestrel disallows sync I/O by default
        // and StreamReader.ReadToEnd() trips InvalidOperationException. We block on
        // the async read because the limiter-policy callback itself is synchronous.
        var body = ReadBodyAsync(ctx.Request.Body, ctx.RequestAborted).GetAwaiter().GetResult();
        ctx.Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("email", out var emailEl) &&
                emailEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return emailEl.GetString()?.Trim().ToLowerInvariant();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed body — let the model binder produce the 400. We deliberately
            // do not fall back to IP/user here: the limiter would then partition
            // differently for malformed-vs-well-formed requests, which an attacker
            // could exploit to dodge the per-email bucket.
        }

        return null;
    }

    private static async Task<string> ReadBodyAsync(Stream body, CancellationToken ct)
    {
        using var reader = new StreamReader(body, leaveOpen: true);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }
}

internal sealed class TotpByUserPartitioner : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // Synchronous partition derivation by blocking on the cookie decode.
        // Cookie is Identity.TwoFactorUserId — a scoped cookie issued by SignInManager
        // when RequiresTwoFactor. We resolve it via AuthenticateAsync inline.
        // Identity stores the user-id under ClaimTypes.Name on this scoped principal,
        // not ClaimTypes.NameIdentifier — the full Identity scheme adds NameIdentifier
        // separately. So we read Identity.Name (which Identity maps to ClaimTypes.Name).
        var task = httpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        task.Wait();
        var userId = task.Result.Principal?.Identity?.Name
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
