using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SubmitterName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubmitterEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    SubmitterUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubmitterIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TriageNotes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LinkedSpec = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Honeypot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TriagedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TriagedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureRequests_CreatedUtc",
                table: "FeatureRequests",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureRequests_Status",
                table: "FeatureRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureRequests_SubmitterIp",
                table: "FeatureRequests",
                column: "SubmitterIp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureRequests");
        }
    }
}
