using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Brings the EF model into agreement with the constraints ScopeOlderAttachmentFksToOwner
    /// installed in raw SQL.
    ///
    /// That migration declared the alternate keys and composite FKs directly in SQL because
    /// EF rejects HasAlternateKey on a TPC derived type. The security review pointed out
    /// that only the DERIVED placement is rejected — the abstract Movement ROOT accepts it,
    /// and TPC propagates a UNIQUE (Id, UserId) into each concrete table. Verified: EF
    /// generates exactly the constraint names the raw SQL had already created.
    ///
    /// That matters because the model was the source of truth describing the VULNERABLE
    /// single-column shape while the database enforced the safe one. MigrationDriftTests
    /// compares model to snapshot and could not see the difference, so a routine future
    /// migration would have regenerated the single-column FK from the model. This project
    /// has already been burned once by exactly that (AlignAttachmentIndexes).
    ///
    /// Every statement is guarded: the constraints and indexes already exist on any database
    /// that ran the earlier migration, so this is a model-alignment pass, not a schema change.
    /// It is written to be safe on a database that has NOT run it too.
    /// </summary>
    public partial class AlignAttachmentFksWithModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            
            migrationBuilder.Sql(@"
                -- Alternate keys: created by the earlier migration on Transactions and
                -- Transfers; LiabilityPayments is new (TPC propagates the root key to all
                -- three concrete tables).
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'AK_Transactions_Id_UserId') THEN
                        ALTER TABLE ""Transactions"" ADD CONSTRAINT ""AK_Transactions_Id_UserId"" UNIQUE (""Id"", ""UserId"");
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'AK_Transfers_Id_UserId') THEN
                        ALTER TABLE ""Transfers"" ADD CONSTRAINT ""AK_Transfers_Id_UserId"" UNIQUE (""Id"", ""UserId"");
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'AK_LiabilityPayments_Id_UserId') THEN
                        ALTER TABLE ""LiabilityPayments"" ADD CONSTRAINT ""AK_LiabilityPayments_Id_UserId"" UNIQUE (""Id"", ""UserId"");
                    END IF;

                    -- Composite FKs. Drop whichever single-column form is still present,
                    -- then add the composite one if it is not already there.
                    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_TransactionAttachments_Transactions_TransactionId') THEN
                        ALTER TABLE ""TransactionAttachments"" DROP CONSTRAINT ""FK_TransactionAttachments_Transactions_TransactionId"";
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_TransactionAttachments_Transactions_TransactionId_UserId') THEN
                        ALTER TABLE ""TransactionAttachments"" ADD CONSTRAINT ""FK_TransactionAttachments_Transactions_TransactionId_UserId""
                          FOREIGN KEY (""TransactionId"", ""UserId"") REFERENCES ""Transactions"" (""Id"", ""UserId"") ON DELETE CASCADE;
                    END IF;

                    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_TransferAttachments_Transfers_TransferId') THEN
                        ALTER TABLE ""TransferAttachments"" DROP CONSTRAINT ""FK_TransferAttachments_Transfers_TransferId"";
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_TransferAttachments_Transfers_TransferId_UserId') THEN
                        ALTER TABLE ""TransferAttachments"" ADD CONSTRAINT ""FK_TransferAttachments_Transfers_TransferId_UserId""
                          FOREIGN KEY (""TransferId"", ""UserId"") REFERENCES ""Transfers"" (""Id"", ""UserId"") ON DELETE CASCADE;
                    END IF;
                END $$;

                -- Composite indexes backing the new FKs (review finding I-2: the
                -- SupportTicketAttachment precedent created one and this pair did not).
                CREATE INDEX IF NOT EXISTS ""IX_TransactionAttachments_TransactionId_UserId""
                    ON ""TransactionAttachments"" (""TransactionId"", ""UserId"");
                CREATE INDEX IF NOT EXISTS ""IX_TransferAttachments_TransferId_UserId""
                    ON ""TransferAttachments"" (""TransferId"", ""UserId"");

                DROP INDEX IF EXISTS ""IX_TransactionAttachments_TransactionId"";
                DROP INDEX IF EXISTS ""IX_TransferAttachments_TransferId"";
            ");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            
            // Deliberately minimal. Reverting the composite FKs restores the cross-tenant
            // destructive write this migration exists to prevent, so Down() only removes
            // the objects that are safe to remove and leaves the security constraints in
            // place — the same stance AddRowLevelSecurityPolicies took on the attachment
            // UserId columns.
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_TransactionAttachments_TransactionId_UserId"";
                DROP INDEX IF EXISTS ""IX_TransferAttachments_TransferId_UserId"";
                CREATE INDEX IF NOT EXISTS ""IX_TransactionAttachments_TransactionId"" ON ""TransactionAttachments"" (""TransactionId"");
                CREATE INDEX IF NOT EXISTS ""IX_TransferAttachments_TransferId"" ON ""TransferAttachments"" (""TransferId"");
            ");

        }
    }
}
