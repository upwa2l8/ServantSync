using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Hand-edited from EF Core's default scaffold. The auto-generated
    /// <c>AddColumn(nullable: false, defaultValue: 0)</c> would attempt to
    /// set every existing TrainingContent's OrganizationId to 0, which
    /// then fails the FK constraint because no Organization row has id=0.
    ///
    /// The 3-phase pattern applied here:
    ///   1. AddColumn nullable (no FK yet) — safe on existing rows.
    ///   2. Raw-SQL UPDATE backfill — pick the parent org for each
    ///      existing row from the best schema hint we have, falling back
    ///      to <c>MIN(OrganizationId) FROM Organizations</c> for any
    ///      orphan content with no requirements referencing it. This
    ///      backfill is deterministic: in the seeded dev DB the safe
    ///      path picks Demo Church for both seeded rows (Safe Spaces
    ///      has an org-scoped requirement pointing at Demo Church;
    ///      Concussion is only slot-scoped under Demo Church's Game-Day
    ///      Referee slot).
    ///   3. AlterColumn to NOT NULL — only after every row has a real
    ///      OrganizationId. Then the indexes + FK are added.
    ///
    /// Production deployments with multiple orgs and no requirement
    /// referencing some content will land on the <c>MIN(id)</c> fallback
    /// org — an admin must reassign that content manually via the
    /// Manage catalog. We surface that requirement in STATUS.md.
    /// </remarks>
    public partial class AddTrainingContentOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 1: add the column as nullable so the ALTER succeeds
            // against existing data (the schema accepts NULL because no
            // NOT NULL or FK is in effect yet).
            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "TrainingContents",
                type: "INTEGER",
                nullable: true);

            // Phase 2: backfill. Each TrainingContent picks up the org
            // from its best-available hint, in this order:
            //   (a) any org-scoped TrainingRequirement referencing the
            //       content (most specific signal).
            //   (b) any slot-scoped TrainingRequirement referencing the
            //       content — resolved through ServiceSlot → Ministry
            //       → Organization.
            //   (c) MIN(OrganizationId) from the Organizations table —
            //       guarantees a valid FK if real orgs exist.
            //
            // SQLite does not understand EF Core navigation properties as
            // column references — `s.Ministry.OrganizationId` reads as a
            // flat column lookup, not a join through the `Ministry`
            // navigation. We must join the `Ministries` table explicitly,
            // via the `ServiceSlot.MinistryId` foreign-key column, so the
            // resolver can find `m.OrganizationId`. Skipping this join
            // surfaces as `SQLite Error 1: no such column:
            // s.Ministry.OrganizationId` at migration time.
            //
            // CTE-style: one UPDATE per row, picking the first non-NULL
            // hint. If multiple hints exist we deterministically pick
            // MIN(OrganizationId) so reassignment is reproducible.
            //
            // NOTE (perf): the inner scalar subquery runs once per
            // `TrainingContents` row, doing a full
            // `TrainingRequirements LEFT JOIN ServiceSlots LEFT JOIN
            // Ministries` scan each iteration. Acceptable for the
            // seeded dev DB (2 rows) and for the one-time migration;
            // production-scale data already in the thousands of rows
            // should consider a temp-table approach to avoid N
            // round-trips. Not blocking today; revisit if a real-org
            // migration starts to time out.
            migrationBuilder.Sql(@"
                WITH hints AS (
                    SELECT
                        tc.Id AS ContentId,
                        (
                            SELECT MIN(COALESCE(tr.OrganizationId, m.OrganizationId))
                            FROM TrainingRequirements tr
                            LEFT JOIN ServiceSlots s ON s.Id = tr.ServiceSlotId
                            LEFT JOIN Ministries  m ON m.Id = s.MinistryId
                            WHERE tr.TrainingContentId = tc.Id
                        ) AS BestOrgHint
                    FROM TrainingContents tc
                )
                UPDATE TrainingContents
                SET OrganizationId = COALESCE(
                    hints.BestOrgHint,
                    (SELECT MIN(Id) FROM Organizations)
                )
                FROM hints
                WHERE TrainingContents.Id = hints.ContentId
                  AND TrainingContents.OrganizationId IS NULL;
            ");

            // Phase 3: enforce NOT NULL on the column now that every row
            // has a real OrganizationId. EF Core's AlterColumn with
            // nullable: false on SQLite triggers a table rebuild that
            // sets the column's nullability.
            migrationBuilder.AlterColumn<int>(
                name: "OrganizationId",
                table: "TrainingContents",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_TrainingContents_Title_Version",
                table: "TrainingContents");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContents_OrganizationId_Title",
                table: "TrainingContents",
                columns: new[] { "OrganizationId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContents_OrganizationId_Title_Version",
                table: "TrainingContents",
                columns: new[] { "OrganizationId", "Title", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingContents_Organizations_OrganizationId",
                table: "TrainingContents",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrainingContents_Organizations_OrganizationId",
                table: "TrainingContents");

            migrationBuilder.DropIndex(
                name: "IX_TrainingContents_OrganizationId_Title",
                table: "TrainingContents");

            migrationBuilder.DropIndex(
                name: "IX_TrainingContents_OrganizationId_Title_Version",
                table: "TrainingContents");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "TrainingContents");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingContents_Title_Version",
                table: "TrainingContents",
                columns: new[] { "Title", "Version" },
                unique: true);
        }
    }
}
