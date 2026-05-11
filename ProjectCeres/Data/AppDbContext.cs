using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

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
    public DbSet<EmailChangeToken> EmailChangeTokens => Set<EmailChangeToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureRelationships(modelBuilder);
        ConfigureUserOwnership(modelBuilder);
        ConfigureSessionEntities(modelBuilder);
        ConfigureMfaEntities(modelBuilder);
        ConfigurePasswordResetEntities(modelBuilder);
        ConfigureEmailChangeEntities(modelBuilder);
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
            b.Property(e => e.NewEmail).HasMaxLength(256);
            b.Property(e => e.TokenHash).HasMaxLength(512);
            b.Property(e => e.Purpose).HasConversion<int>();
        });
    }

    /// <summary>
    /// Phase 3 multi-tenancy scaffolding. Every user-owned entity has a UserId column.
    /// Pre-auth, all rows are stamped with <see cref="SingleUserAccessor.SentinelUserId"/>.
    /// At auth time, an FK to AspNetUsers is added and the sentinel is migrated to a real
    /// user id. Indexes on UserId are added now so multi-user query plans don't regress later.
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

        // Category.UserId is nullable: NULL = system category shared across all users.
        modelBuilder.Entity<Category>().HasIndex(e => e.UserId);

        // Settings is one row per user. The unique constraint is what makes that true.
        modelBuilder.Entity<Settings>().HasIndex(e => e.UserId).IsUnique();

        // Import-pipeline tables.
        modelBuilder.Entity<ImportProfile>().HasIndex(e => e.UserId);
        modelBuilder.Entity<ImportStagedTransaction>().HasIndex(e => e.UserId);
        modelBuilder.Entity<ImportStagedTransfer>().HasIndex(e => e.UserId);
        modelBuilder.Entity<ImportTransferExclusion>().HasIndex(e => e.UserId);
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
        SeedAccounts(modelBuilder);
        SeedCategories(modelBuilder);
        SeedSettings(modelBuilder);
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

    private static void SeedAccounts(ModelBuilder modelBuilder)
    {
        var owner = SingleUserAccessor.SentinelUserId;
        modelBuilder.Entity<Account>().HasData(
            new Account
            {
                Id            = new Guid("10000000-0000-0000-0000-000000000001"),
                Name          = "Cash",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true,
                UserId        = owner
            },
            new Account
            {
                Id            = new Guid("10000000-0000-0000-0000-000000000002"),
                Name          = "Checking Account",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true,
                UserId        = owner
            },
            new Account
            {
                Id            = new Guid("10000000-0000-0000-0000-000000000003"),
                Name          = "Savings Account",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true,
                UserId        = owner
            },
            new Account
            {
                Id            = new Guid("10000000-0000-0000-0000-000000000004"),
                Name          = "Credit Card",
                AccountTypeId = 2,
                CurrencyId    = 1,
                IsActive      = true,
                UserId        = owner
            }
        );
    }

    private static void SeedCategories(ModelBuilder modelBuilder)
    {
        // System categories have UserId = null (shared across all users).
        // User-default categories are stamped with the pre-auth sentinel so they belong
        // to the single bootstrap user; they will be remapped at auth time.
        var owner = (Guid?)SingleUserAccessor.SentinelUserId;
        modelBuilder.Entity<Category>().HasData(
            // --- System ---
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000001"), Name = "Opening Balance",    CategoryTypeId = 1, IsActive = true, IsSystem = true,  LifestyleTag = null,    UserId = null  },

            // --- Income (CategoryTypeId = 1) ---
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000002"), Name = "Salary",             CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null,    UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000003"), Name = "Freelance Income",   CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null,    UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000004"), Name = "Rental Income",      CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null,    UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000005"), Name = "Investment Income",  CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null,    UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000006"), Name = "Business Income",    CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null,    UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000007"), Name = "Other Income",       CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null,    UserId = owner },

            // --- Expense (CategoryTypeId = 2) ---
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000008"), Name = "Housing / Rent",     CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000009"), Name = "Utilities",          CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000010"), Name = "Groceries",          CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000011"), Name = "Transport",          CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000012"), Name = "Fuel",               CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000013"), Name = "Healthcare",         CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000014"), Name = "Insurance",          CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000015"), Name = "Subscriptions",      CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000016"), Name = "Dining Out",         CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000017"), Name = "Entertainment",      CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000018"), Name = "Clothing",           CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000019"), Name = "Personal Care",      CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000020"), Name = "Education",          CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000021"), Name = "Travel",             CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000022"), Name = "Home & Garden",      CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000023"), Name = "Gifts & Donations",  CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants", UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000024"), Name = "Other Expenses",     CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = null,    UserId = owner },

            // --- Uncategorized fallbacks (IsSystem = false so they appear in reports and transaction lists) ---
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000025"), Name = "Uncategorized Income",  CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null, UserId = owner },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000026"), Name = "Uncategorized Expense", CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = null, UserId = owner }
        );
    }

    private static void SeedSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Settings>().HasData(
            new Settings
            {
                Id                = 1,
                UserId            = SingleUserAccessor.SentinelUserId,
                NumberFormat      = "comma_decimal",
                DateFormat        = "DD/MM/YYYY",
                DefaultCurrencyId = 1
            }
        );
    }
}
