using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Common.Email;
using ProjectCeres.Models;

namespace ProjectCeres.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    private readonly ICurrentUserAccessor _currentUser;

    // Stage 9.5b: the user-owned table-name set used by RlsExceptionTranslator, derived
    // from the EF model. Lazily computed once per context — Model is finalized by the time
    // a SaveChanges 42501 catch filter runs.
    private IReadOnlySet<string>? _userOwnedTableNames;
    private IReadOnlySet<string> UserOwnedTableNames =>
        _userOwnedTableNames ??= UserOwnedModel.RlsTables(Model)
            .Select(t => t.PostgresTableName)
            .ToHashSet(StringComparer.Ordinal);

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserAccessor currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    // Stage 7.6.2: catch and translate the DbUpdateException wrapping Postgres 42501
    // (RLS WITH CHECK policy rejection) on a user-owned table into the typed
    // RlsPolicyViolationException. EF Core's ISaveChangesInterceptor hooks are
    // observational only — they don't let you replace the exception that bubbles out
    // of SaveChanges. Overriding SaveChangesAsync here is the documented mechanism
    // for transforming exceptions at the EF boundary.
    //
    // Stage 7.6.5: chain DbExceptionTranslator after RLS to map 23505 / 23503 / 23502
    // into typed exceptions. RLS runs first because 42501 must always route through
    // the RLS path; the second catch handles the constraint-violation codes.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException ex) when (RlsExceptionTranslator.TryTranslate(ex, _currentUser, UserOwnedTableNames, out var rls))
        {
            throw rls!;
        }
        catch (DbUpdateException ex) when (DbExceptionTranslator.TryTranslate(ex, out var typed))
        {
            throw typed!;
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (RlsExceptionTranslator.TryTranslate(ex, _currentUser, UserOwnedTableNames, out var rls))
        {
            throw rls!;
        }
        catch (DbUpdateException ex) when (DbExceptionTranslator.TryTranslate(ex, out var typed))
        {
            throw typed!;
        }
    }

    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<AccountType> AccountTypes => Set<AccountType>();
    public DbSet<CategoryType> CategoryTypes => Set<CategoryType>();
    public DbSet<ReportType> ReportTypes => Set<ReportType>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionAttachment> TransactionAttachments => Set<TransactionAttachment>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<LiabilityPayment> LiabilityPayments => Set<LiabilityPayment>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
    public DbSet<CategoryBudget> CategoryBudgets => Set<CategoryBudget>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();
    public DbSet<Settings> Settings => Set<Settings>();
    public DbSet<TransferAttachment> TransferAttachments => Set<TransferAttachment>();
    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();
    public DbSet<ImportStagedTransfer> ImportStagedTransfers => Set<ImportStagedTransfer>();
    public DbSet<ImportTransferExclusion> ImportTransferExclusions => Set<ImportTransferExclusion>();
    public DbSet<ImportStagedTransaction> ImportStagedTransactions => Set<ImportStagedTransaction>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserBlockedIp> UserBlockedIps => Set<UserBlockedIp>();
    public DbSet<UserMfaBackupCode> UserMfaBackupCodes => Set<UserMfaBackupCode>();
    public DbSet<TotpReplayEntry> TotpReplayEntries => Set<TotpReplayEntry>();
    public DbSet<FailedLoginAttempt> FailedLoginAttempts => Set<FailedLoginAttempt>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailConfirmationToken> EmailConfirmationTokens => Set<EmailConfirmationToken>();
    public DbSet<EmailChangeToken> EmailChangeTokens => Set<EmailChangeToken>();
    public DbSet<LockoutUnlockToken> LockoutUnlockTokens => Set<LockoutUnlockToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<EmailDeliveryEvent> EmailDeliveryEvents => Set<EmailDeliveryEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureRelationships(modelBuilder);
        ConfigureUserOwnership(modelBuilder);
        ConfigureGlobalQueryFilters(modelBuilder);
        ConfigureSessionEntities(modelBuilder);
        ConfigureMfaEntities(modelBuilder);
        ConfigurePasswordResetEntities(modelBuilder);
        ConfigureEmailConfirmationEntities(modelBuilder);
        ConfigureEmailChangeEntities(modelBuilder);
        ConfigureLockoutUnlockEntities(modelBuilder);
        ConfigureAuditLogEntities(modelBuilder);
        ConfigureEmailDeliveryEventEntities(modelBuilder);
        SeedData(modelBuilder);
    }

    private static void ConfigureMfaEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserMfaBackupCode>(b =>
        {
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.UserId);
            // Postgres partial index: only the unused codes (the hot path in verify).
            b.HasIndex(c => new { c.UserId, c.UsedAt })
                .HasFilter(@"""UsedAt"" IS NULL")
                .HasDatabaseName("IX_UserMfaBackupCodes_UserId_Unused");
            b.Property(c => c.CodeHash).HasMaxLength(512);
            b.Property(c => c.UsedFromIp).HasMaxLength(45);
        });

        modelBuilder.Entity<TotpReplayEntry>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.UserId);
            b.HasIndex(e => e.AcceptedAt);
            b.Property(e => e.CodeHash).HasMaxLength(512);
        });
    }

    private static void ConfigureSessionEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserSession>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => new { s.UserId, s.RevokedAt });
            b.HasIndex(s => s.LastUsedAt);
            b.Property(s => s.IpCreatedAt).HasMaxLength(45);
            b.Property(s => s.UserAgent).HasMaxLength(512);
            b.Property(s => s.PersistentTokenHash).HasMaxLength(512);
        });

        modelBuilder.Entity<UserBlockedIp>(b =>
        {
            b.HasKey(i => i.Id);
            b.HasIndex(i => new { i.UserId, i.IpAddress }).IsUnique();
            b.Property(i => i.IpAddress).HasMaxLength(45);
            b.Property(i => i.Reason).HasMaxLength(256);
        });

        modelBuilder.Entity<FailedLoginAttempt>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.IpAddress, e.OccurredAt });
            b.HasIndex(e => new { e.EmailAttempted, e.OccurredAt });
            b.HasIndex(e => e.OccurredAt);
            b.Property(e => e.EmailAttempted).HasMaxLength(256);
            b.Property(e => e.IpAddress).HasMaxLength(45);
            b.Property(e => e.UserAgent).HasMaxLength(512);
            b.Property(e => e.Reason).HasConversion<string>();
        });
    }

    private static void ConfigurePasswordResetEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PasswordResetToken>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.UserId, e.ConsumedAt });
            b.HasIndex(e => e.ExpiresAt);
            b.HasIndex(e => e.TokenLookup).IsUnique();
            b.Property(e => e.TokenLookup).HasMaxLength(32);
            b.Property(e => e.TokenHash).HasMaxLength(512);
        });
    }

    private static void ConfigureEmailConfirmationEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailConfirmationToken>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.UserId, e.ConsumedAt });
            b.HasIndex(e => e.ExpiresAt);
            b.HasIndex(e => e.TokenLookup).IsUnique();
            b.Property(e => e.TokenLookup).HasMaxLength(32);
            b.Property(e => e.TokenHash).HasMaxLength(512);
        });
    }

    private static void ConfigureEmailChangeEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailChangeToken>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.UserId, e.ConsumedAt });
            b.HasIndex(e => e.ExpiresAt);
            b.HasIndex(e => e.TokenLookup).IsUnique();
            b.Property(e => e.TokenLookup).HasMaxLength(32);
            b.Property(e => e.NewEmail).HasMaxLength(256);
            b.Property(e => e.TokenHash).HasMaxLength(512);
            b.Property(e => e.Purpose).HasConversion<int>();
        });
    }

    private static void ConfigureLockoutUnlockEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LockoutUnlockToken>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.UserId, e.ConsumedAt });
            b.HasIndex(e => e.ExpiresAt);
            b.HasIndex(e => e.TokenLookup).IsUnique();
            b.Property(e => e.TokenLookup).HasMaxLength(32);
            b.Property(e => e.TokenHash).HasMaxLength(512);
        });
    }

    private static void ConfigureAuditLogEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.UserId, e.OccurredAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_AuditLog_UserId_OccurredAt");
            b.HasIndex(e => e.OccurredAt)
                .HasDatabaseName("IX_AuditLog_OccurredAt");
            b.Property(e => e.Action).HasConversion<string>();
            b.Property(e => e.EntityType).HasMaxLength(64);
            b.Property(e => e.IpAddress).HasMaxLength(45).HasDefaultValue("unknown");
            b.ToTable(t => t.HasCheckConstraint(
                "CK_AuditLog_EntityPair",
                "(\"EntityType\" IS NULL AND \"EntityId\" IS NULL) OR (\"EntityType\" IS NOT NULL AND \"EntityId\" IS NOT NULL)"));
        });
    }

    private static void ConfigureEmailDeliveryEventEntities(ModelBuilder modelBuilder)
    {
        // Stage 8e: Resend webhook events. Cross-tenant by design (the webhook is pre-auth;
        // events may also arrive for addresses without a matching user). Does NOT implement
        // IUserOwned and is intentionally NOT in UserOwnedModel.RlsTables — same precedent as
        // FailedLoginAttempt (ADR-0067). Payload is jsonb for future ad-hoc inspection;
        // the (EmailAddress, OccurredAt DESC) index supports the most-recent-event-by-address
        // lookup pattern that the operational tooling will use.
        modelBuilder.Entity<EmailDeliveryEvent>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.MessageId).HasMaxLength(128);
            b.Property(e => e.Type).HasMaxLength(64);
            b.Property(e => e.EmailAddress).HasMaxLength(256);
            b.Property(e => e.Payload).HasColumnType("jsonb");
            b.HasIndex(e => new { e.EmailAddress, e.OccurredAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_EmailDeliveryEvents_EmailAddress_OccurredAt");
        });
    }

    /// <summary>
    /// Stage 7 multi-tenancy. Every user-owned entity has a UserId column and an index on it.
    /// Per-user category seeding is handled at registration time by CategorySeedService — no
    /// user-owned data is seeded in-DbContext. Indexes on UserId ensure multi-user query plans
    /// perform equivalently for every tenant.
    /// </summary>
    private static void ConfigureUserOwnership(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>().HasIndex(e => e.UserId);
        modelBuilder.Entity<Budget>().HasIndex(e => e.UserId);
        modelBuilder.Entity<CategoryBudget>().HasIndex(e => e.UserId);
        modelBuilder.Entity<RecurringTransaction>().HasIndex(e => e.UserId);
        modelBuilder.Entity<SavedReport>().HasIndex(e => e.UserId);
        modelBuilder.Entity<Transaction>().HasIndex(e => e.UserId);
        modelBuilder.Entity<Transfer>().HasIndex(e => e.UserId);
        modelBuilder.Entity<LiabilityPayment>().HasIndex(e => e.UserId);

        modelBuilder.Entity<Category>().HasIndex(e => e.UserId);

        // Settings is one row per user. The unique constraint is what makes that true.
        modelBuilder.Entity<Settings>().HasIndex(e => e.UserId).IsUnique();

        // Import-pipeline tables.
        modelBuilder.Entity<ImportProfile>().HasIndex(e => e.UserId);
        modelBuilder.Entity<ImportStagedTransaction>().HasIndex(e => e.UserId);
        modelBuilder.Entity<ImportStagedTransfer>().HasIndex(e => e.UserId);
        modelBuilder.Entity<ImportTransferExclusion>().HasIndex(e => e.UserId);
    }

    /// <summary>
    /// Stage 7 multi-tenancy: every IUserOwned entity carries an EF global query filter so an
    /// accidentally-omitted <c>.Where(t => t.UserId == currentUser.UserId)</c> returns zero rows
    /// instead of leaking. Service code still writes the explicit Where as belt-and-suspenders
    /// (ADR-0065 explicit redundancy). Movement is abstract under TPC — filters apply to each
    /// concrete subtype, not the abstract root.
    ///
    /// NOT filtered (and why):
    /// - TransactionAttachment, TransferAttachment: no UserId column; scoped via parent in service code.
    /// - FailedLoginAttempt: cross-tenant by design (ADR-0067); retention sweep iterates all rows.
    /// - AccountType, CategoryType, Currency, ReportType: system reference tables.
    /// - AspNet* Identity tables: cross-tenant by definition.
    /// </summary>
    private void ConfigureGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        // Stage 9.5b: iterate UserOwnedModel.RlsTables (model-derived) so adding a new user-owned
        // entity needs no list edit. The Movement TPC abstract root is registered explicitly first
        // — EF rejects HasQueryFilter on TPC subtypes (Transaction / Transfer / LiabilityPayment)
        // because the filter must live on the root and EF propagates it to each concrete table.
        //
        // Implementation note: we invoke a generic helper via reflection rather than building
        // the filter as a raw Expression tree. Both work, but the generic-method approach lets
        // the C# compiler emit the lambda with the correct `this`-closure semantics so EF
        // parameterizes the filter (`WHERE "UserId" = @__currentUser_UserId_0`) and re-evaluates
        // `_currentUser.UserId` per query. Building the expression directly with
        // `Expression.Constant(_currentUser, ...)` would bake the specific accessor instance
        // into the cached model — every subsequent DbContext would query against the wrong
        // user. See dotnet/efcore#14740 for the documented closure-evaluation pitfall.
        modelBuilder.Entity<Movement>().HasQueryFilter(e => e.UserId == _currentUser.UserId);

        var registerMethod = typeof(AppDbContext).GetMethod(
            nameof(RegisterUserOwnedFilter),
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Query-filter set = model-derived RLS set MINUS the attachment tables. Attachments
        // are RLS-protected at the DB layer (Stage 9.5b) but carry no EF query filter — they
        // are scoped via their parent in service code. See UserOwnedModel + spec §4.1.
        foreach (var table in UserOwnedModel.RlsTables(modelBuilder.Model))
        {
            if (typeof(Movement).IsAssignableFrom(table.EntityType))
                continue; // TPC subtype — covered by Movement above.
            if (table.PostgresTableName is "TransactionAttachments" or "TransferAttachments")
                continue; // RLS-protected; scoped via parent at the EF layer (no query filter).

            registerMethod
                .MakeGenericMethod(table.EntityType)
                .Invoke(this, new object[] { modelBuilder });
        }
    }

    private void RegisterUserOwnedFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IUserOwned
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
    }

    // -------------------------------------------------------------------------
    // Relationships
    // -------------------------------------------------------------------------

    private static void ConfigureRelationships(ModelBuilder modelBuilder)
    {
        // TPC: each concrete Movement subtype maps to its own existing table — no schema change.
        modelBuilder.Entity<Movement>().UseTpcMappingStrategy();

        // LiabilityPayment references Account twice — explicit config required.
        modelBuilder.Entity<LiabilityPayment>()
            .HasOne(p => p.AssetAccount)
            .WithMany()
            .HasForeignKey(p => p.AssetAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LiabilityPayment>()
            .HasOne(p => p.LiabilityAccount)
            .WithMany()
            .HasForeignKey(p => p.LiabilityAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Transfer references Account twice — must be explicit so EF Core knows
        // which FK maps to which navigation property.
        modelBuilder.Entity<Transfer>()
            .HasOne(t => t.SourceAccount)
            .WithMany()
            .HasForeignKey(t => t.SourceAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Transfer>()
            .HasOne(t => t.DestAccount)
            .WithMany()
            .HasForeignKey(t => t.DestAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Transaction → Account: no cascade — Account uses soft delete (IsActive).
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Account)
            .WithMany(a => a.Transactions)
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Transaction → Category: no cascade — Category uses soft delete (IsActive).
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Category)
            .WithMany(c => c.Transactions)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Transaction → Budget: nullable FK, no cascade — Budget uses soft delete.
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Budget)
            .WithMany(b => b.Transactions)
            .HasForeignKey(t => t.BudgetId)
            .OnDelete(DeleteBehavior.Restrict);

        // RecurringTransaction.Frequency stored as string (varchar) for readability.
        modelBuilder.Entity<RecurringTransaction>()
            .Property(r => r.Frequency)
            .HasConversion<string>();

        // RecurringTransaction.ReminderBehaviour stored as string (varchar) — no schema change.
        modelBuilder.Entity<RecurringTransaction>()
            .Property(r => r.ReminderBehaviour)
            .HasConversion<string>();

        // RecurringTransaction → Account: no cascade.
        modelBuilder.Entity<RecurringTransaction>()
            .HasOne(r => r.Account)
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // RecurringTransaction → Category: no cascade.
        modelBuilder.Entity<RecurringTransaction>()
            .HasOne(r => r.Category)
            .WithMany()
            .HasForeignKey(r => r.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // CategoryBudget → Category: no cascade — Category uses soft delete.
        modelBuilder.Entity<CategoryBudget>()
            .HasOne(cb => cb.Category)
            .WithMany()
            .HasForeignKey(cb => cb.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // SavedReport optional FK to Category: set null if category is deactivated
        // (SavedReport references categories for filter presets only).
        modelBuilder.Entity<SavedReport>()
            .HasOne(sr => sr.Category)
            .WithMany()
            .HasForeignKey(sr => sr.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // SavedReport optional FK to Account: same rationale.
        modelBuilder.Entity<SavedReport>()
            .HasOne(sr => sr.Account)
            .WithMany()
            .HasForeignKey(sr => sr.AccountId)
            .OnDelete(DeleteBehavior.SetNull);

        // TransferAttachment → Transfer: cascade delete (attachment has no life outside transfer).
        modelBuilder.Entity<TransferAttachment>()
            .HasOne(a => a.Transfer)
            .WithMany(t => t.Attachments)
            .HasForeignKey(a => a.TransferId)
            .OnDelete(DeleteBehavior.Cascade);

        // Budget.LinkedAccount: optional FK for Savings goal type — no cascade (Account soft-deletes).
        modelBuilder.Entity<Budget>()
            .HasOne(b => b.LinkedAccount)
            .WithMany()
            .HasForeignKey(b => b.LinkedAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ImportProfile>(entity =>
        {
            entity.ToTable("ImportProfiles");
            entity.Property(p => p.ColumnMappings).HasColumnType("jsonb");
            entity.Property(p => p.Format)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(ImportFormat.Csv);
        });

        modelBuilder.Entity<ImportStagedTransfer>(entity =>
        {
            entity.ToTable("ImportStagedTransfers");
            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(30);

            entity.HasOne(e => e.Account)
                  .WithMany()
                  .HasForeignKey(e => e.AccountId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CandidateTransaction)
                  .WithMany()
                  .HasForeignKey(e => e.CandidateTransactionId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ImportStagedTransaction>(entity =>
        {
            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(30);

            entity.HasOne(e => e.Account)
                  .WithMany()
                  .HasForeignKey(e => e.AccountId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.MatchedTransaction)
                  .WithMany()
                  .HasForeignKey(e => e.MatchedTransactionId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ImportTransferExclusion>(entity =>
        {
            entity.ToTable("ImportTransferExclusions");
            // Uniqueness scoped per user — different users can independently exclude
            // the same description pattern.
            entity.HasIndex(e => new { e.UserId, e.DescriptionPattern }).IsUnique();
        });
    }

    // -------------------------------------------------------------------------
    // Seed data
    // -------------------------------------------------------------------------

    private static void SeedData(ModelBuilder modelBuilder)
    {
        SeedLookups(modelBuilder);
    }

    private static void SeedLookups(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Currency>().HasData(
            new Currency { Id = 1, Code = "EUR", Name = "Euro",                      Symbol = "€"    },
            new Currency { Id = 2, Code = "USD", Name = "US Dollar",                 Symbol = "$"    },
            new Currency { Id = 3, Code = "GBP", Name = "British Pound",             Symbol = "£"    },
            new Currency { Id = 4, Code = "COP", Name = "Colombian Peso",            Symbol = "$"    },
            new Currency { Id = 5, Code = "ARS", Name = "Argentine Peso",            Symbol = "$"    },
            new Currency { Id = 6, Code = "VED", Name = "Venezuelan Bolívar Digital", Symbol = "Bs.D" }
        );

        modelBuilder.Entity<AccountType>().HasData(
            new AccountType { Id = 1, Name = "Asset"     },
            new AccountType { Id = 2, Name = "Liability" }
        );

        modelBuilder.Entity<CategoryType>().HasData(
            new CategoryType { Id = 1, Name = "Income"  },
            new CategoryType { Id = 2, Name = "Expense" }
        );

        modelBuilder.Entity<ReportType>().HasData(
            new ReportType { Id = 1, Name = "Net Worth Statement"              },
            new ReportType { Id = 2, Name = "Income & Expense Summary"         },
            new ReportType { Id = 3, Name = "Expense Breakdown by Category"    },
            new ReportType { Id = 4, Name = "Transaction History"              },
            new ReportType { Id = 5, Name = "Budget vs. Actual"               },
            new ReportType { Id = 6, Name = "Largest Expenses"                },
            new ReportType { Id = 7, Name = "Monthly Cash Flow Trend"         },
            new ReportType { Id = 8, Name = "Net Worth Over Time"             }
        );
    }

}
