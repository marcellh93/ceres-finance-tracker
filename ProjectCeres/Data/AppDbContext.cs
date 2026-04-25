using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;

namespace ProjectCeres.Data;

public class AppDbContext : DbContext
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
    public DbSet<CsvImportProfile> CsvImportProfiles => Set<CsvImportProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureRelationships(modelBuilder);
        SeedData(modelBuilder);
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

        // CsvImportProfile.ColumnMappings stored as jsonb.
        modelBuilder.Entity<CsvImportProfile>()
            .Property(p => p.ColumnMappings)
            .HasColumnType("jsonb");
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
        modelBuilder.Entity<Account>().HasData(
            new Account
            {
                Id            = new Guid("10000000-0000-0000-0000-000000000001"),
                Name          = "Cash",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true
            },
            new Account
            {
                Id            = new Guid("10000000-0000-0000-0000-000000000002"),
                Name          = "Checking Account",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true
            },
            new Account
            {
                Id            = new Guid("10000000-0000-0000-0000-000000000003"),
                Name          = "Savings Account",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true
            },
            new Account
            {
                Id            = new Guid("10000000-0000-0000-0000-000000000004"),
                Name          = "Credit Card",
                AccountTypeId = 2,
                CurrencyId    = 1,
                IsActive      = true
            }
        );
    }

    private static void SeedCategories(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>().HasData(
            // --- System ---
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000001"), Name = "Opening Balance",    CategoryTypeId = 1, IsActive = true, IsSystem = true,  LifestyleTag = null     },

            // --- Income (CategoryTypeId = 1) ---
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000002"), Name = "Salary",             CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null     },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000003"), Name = "Freelance Income",   CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null     },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000004"), Name = "Rental Income",      CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null     },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000005"), Name = "Investment Income",  CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null     },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000006"), Name = "Business Income",    CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null     },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000007"), Name = "Other Income",       CategoryTypeId = 1, IsActive = true, IsSystem = false, LifestyleTag = null     },

            // --- Expense (CategoryTypeId = 2) ---
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000008"), Name = "Housing / Rent",     CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000009"), Name = "Utilities",          CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000010"), Name = "Groceries",          CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000011"), Name = "Transport",          CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000012"), Name = "Fuel",               CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000013"), Name = "Healthcare",         CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000014"), Name = "Insurance",          CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000015"), Name = "Subscriptions",      CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000016"), Name = "Dining Out",         CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000017"), Name = "Entertainment",      CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000018"), Name = "Clothing",           CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000019"), Name = "Personal Care",      CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000020"), Name = "Education",          CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000021"), Name = "Travel",             CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000022"), Name = "Home & Garden",      CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Needs"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000023"), Name = "Gifts & Donations",  CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = "Wants"  },
            new Category { Id = new Guid("20000000-0000-0000-0000-000000000024"), Name = "Other Expenses",     CategoryTypeId = 2, IsActive = true, IsSystem = false, LifestyleTag = null     }
        );
    }

    private static void SeedSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Settings>().HasData(
            new Settings
            {
                Id                = 1,
                NumberFormat      = "comma_decimal",
                DateFormat        = "DD/MM/YYYY",
                DefaultCurrencyId = 1
            }
        );
    }
}
