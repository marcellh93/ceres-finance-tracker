using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Scopes the TransactionAttachment and TransferAttachment foreign keys to their
    /// owner, closing the same cross-tenant destructive write that Stage 12.5 fixed on
    /// SupportTicketAttachment.
    ///
    /// Postgres runs FK checks and ON DELETE CASCADE through an internal
    /// referential-integrity trigger that row-level security is NOT applied to; FORCE
    /// ROW LEVEL SECURITY does not change that. With a single-column FK, user B can
    /// attach a row to user A's movement — the RLS WITH CHECK pins UserId to the WRITER
    /// and says nothing about the parent's owner — and A deleting their own movement
    /// then destroys B's row via the cascade, silently. Reproduced on the support tables
    /// on 2026-08-23; these two carry the identical shape.
    ///
    /// WHY RAW SQL: Transaction and Transfer are TPC subtypes of the abstract Movement
    /// root, and EF refuses HasAlternateKey on a derived type ("The key must be
    /// configured on the root type"). The root is abstract with no table, so there is
    /// nowhere to put it in the model. Postgres has no such restriction, so the
    /// constraint is declared directly — the same approach this project already uses for
    /// RLS policies. The EF model therefore still describes single-column FKs; the
    /// DATABASE is the authority here, and ParityTests'
    /// Attachment_fks_are_scoped_to_the_parent_owner_in_the_database asserts against
    /// pg_constraint rather than the model for exactly that reason.
    ///
    /// Pre-flight: aborts if any attachment already points at a movement owned by
    /// someone else, rather than letting AddForeignKey fail with a bare 23503.
    /// </summary>
    public partial class ScopeOlderAttachmentFksToOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql(@"
                DO $$
                DECLARE divergent int;
                BEGIN
                    SELECT COUNT(*) INTO divergent
                      FROM ""TransactionAttachments"" a
                      JOIN ""Transactions"" t ON t.""Id"" = a.""TransactionId""
                     WHERE a.""UserId"" <> t.""UserId"";
                    IF divergent > 0 THEN
                        RAISE EXCEPTION '% TransactionAttachments row(s) belong to a different user than their transaction; resolve them before scoping the FK', divergent;
                    END IF;

                    SELECT COUNT(*) INTO divergent
                      FROM ""TransferAttachments"" a
                      JOIN ""Transfers"" t ON t.""Id"" = a.""TransferId""
                     WHERE a.""UserId"" <> t.""UserId"";
                    IF divergent > 0 THEN
                        RAISE EXCEPTION '% TransferAttachments row(s) belong to a different user than their transfer', divergent;
                    END IF;
                END $$;

                ALTER TABLE ""Transactions"" ADD CONSTRAINT ""AK_Transactions_Id_UserId"" UNIQUE (""Id"", ""UserId"");
                ALTER TABLE ""Transfers""    ADD CONSTRAINT ""AK_Transfers_Id_UserId""    UNIQUE (""Id"", ""UserId"");

                ALTER TABLE ""TransactionAttachments""
                  DROP CONSTRAINT ""FK_TransactionAttachments_Transactions_TransactionId"";
                ALTER TABLE ""TransactionAttachments""
                  ADD CONSTRAINT ""FK_TransactionAttachments_Transactions_TransactionId_UserId""
                  FOREIGN KEY (""TransactionId"", ""UserId"")
                  REFERENCES ""Transactions"" (""Id"", ""UserId"") ON DELETE CASCADE;

                ALTER TABLE ""TransferAttachments""
                  DROP CONSTRAINT ""FK_TransferAttachments_Transfers_TransferId"";
                ALTER TABLE ""TransferAttachments""
                  ADD CONSTRAINT ""FK_TransferAttachments_Transfers_TransferId_UserId""
                  FOREIGN KEY (""TransferId"", ""UserId"")
                  REFERENCES ""Transfers"" (""Id"", ""UserId"") ON DELETE CASCADE;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql(@"
                ALTER TABLE ""TransactionAttachments""
                  DROP CONSTRAINT ""FK_TransactionAttachments_Transactions_TransactionId_UserId"";
                ALTER TABLE ""TransactionAttachments""
                  ADD CONSTRAINT ""FK_TransactionAttachments_Transactions_TransactionId""
                  FOREIGN KEY (""TransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE CASCADE;

                ALTER TABLE ""TransferAttachments""
                  DROP CONSTRAINT ""FK_TransferAttachments_Transfers_TransferId_UserId"";
                ALTER TABLE ""TransferAttachments""
                  ADD CONSTRAINT ""FK_TransferAttachments_Transfers_TransferId""
                  FOREIGN KEY (""TransferId"") REFERENCES ""Transfers"" (""Id"") ON DELETE CASCADE;

                ALTER TABLE ""Transactions"" DROP CONSTRAINT ""AK_Transactions_Id_UserId"";
                ALTER TABLE ""Transfers""    DROP CONSTRAINT ""AK_Transfers_Id_UserId"";
            ");
        }
    }
}
