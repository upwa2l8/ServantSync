using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegistrationToken",
                table: "Organizations",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_RegistrationToken",
                table: "Organizations",
                column: "RegistrationToken",
                unique: true);

            // Backfill: every existing Organization row gets a fresh
            // 32-char hex token so admins of pre-existing orgs don't
            // have to click "Generate registration link" on each org to
            // enable self-signup. randomblob(16) -> 16 raw bytes ->
            // lower(hex(...)) -> 32 hex chars, matching the
            // Guid.NewGuid().ToString("N") shape that OrganizationService
            // and DatabaseSeeder emit. Unique collision probability is
            // 2^-128; the unique index above catches any absurd race.
            migrationBuilder.Sql(
                "UPDATE Organizations SET RegistrationToken = lower(hex(randomblob(16))) WHERE RegistrationToken IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_RegistrationToken",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "RegistrationToken",
                table: "Organizations");
        }
    }
}
