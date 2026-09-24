using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tools;

/// <summary>
/// Cron entry: poll-drains `Pending` ExportJob rows across all users, builds each
/// ZIP, tokenizes the download link, emails the user, and cleans up expired/consumed
/// jobs' files. Cross-tenant by design (acts for no single user) — same
/// AdminDbContext + IgnoreQueryFilters pattern as SweepSessions. Stage 13.8 Task 6.
/// Invoked via `dotnet run --project ProjectCeres -- --run-export-jobs`.
/// </summary>
[RequiresAdminContext]
public static class ExportJobWorker
{
    /// <summary>Retry budget before a build failure gives up and marks the job Failed.</summary>
    public const int MaxFailures = 3;

    /// <summary>How long a Ready download link stays valid.</summary>
    public static readonly TimeSpan DownloadLifetime = TimeSpan.FromHours(24);

    public static async Task<int> RunAsync(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var logger = sp.GetRequiredService<ILogger<Program>>();

        await ProcessPendingAsync(
            sp.GetRequiredService<AdminDbContext>(),
            sp.GetRequiredService<DataExportBuilder>(),
            sp.GetRequiredService<ExportTokenGenerator>(),
            sp.GetRequiredService<TokenLookupHasher>(),
            sp.GetRequiredService<IEmailComposer>(),
            sp.GetRequiredService<IEmailService>(),
            sp.GetRequiredService<IEmailRecipientResolver>(),
            sp.GetRequiredService<ILanguageResolver>(),
            sp.GetRequiredService<IOptions<EmailOptions>>(),
            sp.GetRequiredService<IOptions<FileAttachmentOptions>>(),
            sp.GetRequiredService<IWebHostEnvironment>(),
            sp.GetRequiredService<TimeProvider>(),
            logger,
            CancellationToken.None);

        return 0;
    }

    /// <summary>
    /// Drains Pending jobs (build+tokenize+email), re-sends Ready-but-unemailed jobs,
    /// retries/fails jobs whose builder throws, and cleans up expired/consumed jobs'
    /// files. Returns the number of jobs touched, for logging.
    /// </summary>
    public static async Task<int> ProcessPendingAsync(
        AdminDbContext db,
        DataExportBuilder builder,
        ExportTokenGenerator tokens,
        TokenLookupHasher lookupHasher,
        IEmailComposer composer,
        IEmailService email,
        IEmailRecipientResolver recipients,
        ILanguageResolver languages,
        IOptions<EmailOptions> emailOptions,
        IOptions<FileAttachmentOptions> attachmentOptions,
        IWebHostEnvironment env,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var touched = 0;
        var exportRoot = ExportDirectory(attachmentOptions, env);

        // Cross-tenant by design: the worker acts for no single user. Allow-listed in
        // ArchitectureTests.IgnoreQueryFilters_only_appears_in_documented_exception_paths.
        var pending = await db.ExportJobs
            .IgnoreQueryFilters()
            .Where(j => j.Status == ExportJobStatus.Pending)
            .ToListAsync(ct);

        foreach (var job in pending)
        {
            job.Status = ExportJobStatus.Processing;
            await db.SaveChangesAsync(ct);

            try
            {
                var zipPath = await builder.BuildAsync(job.UserId, exportRoot, ct);
                var rawToken = tokens.Generate();

                job.StoredPath = zipPath;
                job.TokenLookup = lookupHasher.ComputeLookup(rawToken);
                job.TokenHash = tokens.Hash(rawToken);
                job.Status = ExportJobStatus.Ready;
                job.ReadyAt = now;
                job.ExpiresAt = now + DownloadLifetime;
                await db.SaveChangesAsync(ct);

                await SendReadyEmailAsync(job, rawToken, composer, email, recipients, languages, emailOptions, db, clock, logger, ct);
            }
            catch (Exception ex)
            {
                job.FailureCount++;
                DeletePartialZip(job, logger);
                job.StoredPath = null;

                if (job.FailureCount >= MaxFailures)
                {
                    job.Status = ExportJobStatus.Failed;
                    await db.SaveChangesAsync(ct);
                    await SendFailedEmailAsync(job, composer, email, recipients, languages, logger, ct);
                }
                else
                {
                    job.Status = ExportJobStatus.Pending;
                    await db.SaveChangesAsync(ct);
                }

                logger.LogError(ex, "ExportJob {JobId} build failed (attempt {Count}).", job.Id, job.FailureCount);
            }

            touched++;
        }

        // Idempotent resend: a Ready job whose email never went out (crash after build,
        // before/during send) re-sends without rebuilding the ZIP.
        var unemailedReady = await db.ExportJobs
            .IgnoreQueryFilters()
            .Where(j => j.Status == ExportJobStatus.Ready && j.EmailedAt == null)
            .ToListAsync(ct);

        foreach (var job in unemailedReady)
        {
            // TokenLookup/TokenHash were already stamped when the job first went Ready;
            // there is no raw token to re-derive, so re-issue a fresh one — the old link
            // (if it ever reached the user) is superseded, which is safe for a single-use token.
            var rawToken = tokens.Generate();
            job.TokenLookup = lookupHasher.ComputeLookup(rawToken);
            job.TokenHash = tokens.Hash(rawToken);
            await db.SaveChangesAsync(ct);

            await SendReadyEmailAsync(job, rawToken, composer, email, recipients, languages, emailOptions, db, clock, logger, ct);
            touched++;
        }

        // Cleanup: delete the ZIP for jobs past expiry or already consumed; null StoredPath.
        var toClean = await db.ExportJobs
            .IgnoreQueryFilters()
            .Where(j => j.StoredPath != null
                && ((j.ExpiresAt != null && j.ExpiresAt <= now) || j.ConsumedAt != null))
            .ToListAsync(ct);

        foreach (var job in toClean)
        {
            DeletePartialZip(job, logger);
            job.StoredPath = null;
            await db.SaveChangesAsync(ct);
            touched++;
        }

        logger.LogInformation("Export job sweep touched {Count} rows.", touched);
        return touched;
    }

    private static async Task SendReadyEmailAsync(
        ExportJob job, string rawToken, IEmailComposer composer, IEmailService email,
        IEmailRecipientResolver recipients, ILanguageResolver languages, IOptions<EmailOptions> emailOptions,
        AdminDbContext db, TimeProvider clock, ILogger logger, CancellationToken ct)
    {
        try
        {
            var baseUrl = emailOptions.Value.PublicBaseUrl?.TrimEnd('/') ?? "";
            var downloadUrl = $"{baseUrl}/api/profile/export/download?token={Uri.EscapeDataString(rawToken)}";

            var recipient = await recipients.ResolveAsync(job.UserId, ct);
            var culture = await languages.ResolveForUserAsync(job.UserId, ct);
            var msg = composer.Compose(EmailTemplateKey.GdprExportReady, culture, downloadUrl)
                with { To = recipient };
            await email.SendAsync(msg, ct);

            job.EmailedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Per sibling pattern (LockoutUnlockService): a failed send is logged but
            // never rolls back the job state — the next sweep retries the send.
            logger.LogError(ex, "Failed to send GdprExportReady email for ExportJob {JobId}.", job.Id);
        }
    }

    private static async Task SendFailedEmailAsync(
        ExportJob job, IEmailComposer composer, IEmailService email,
        IEmailRecipientResolver recipients, ILanguageResolver languages, ILogger logger, CancellationToken ct)
    {
        try
        {
            var recipient = await recipients.ResolveAsync(job.UserId, ct);
            var culture = await languages.ResolveForUserAsync(job.UserId, ct);
            var msg = composer.Compose(EmailTemplateKey.GdprExportFailed, culture) with { To = recipient };
            await email.SendAsync(msg, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to notify user of ExportJob {JobId} failure.", job.Id);
        }
    }

    private static void DeletePartialZip(ExportJob job, ILogger logger)
    {
        if (string.IsNullOrEmpty(job.StoredPath)) return;
        try
        {
            if (File.Exists(job.StoredPath)) File.Delete(job.StoredPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to delete export ZIP at {Path} for ExportJob {JobId}.", job.StoredPath, job.Id);
        }
    }

    private static string ExportDirectory(IOptions<FileAttachmentOptions> options, IWebHostEnvironment env)
    {
        var root = options.Value.RootPath ?? env.ContentRootPath;
        return Path.Combine(root, "exports");
    }
}
