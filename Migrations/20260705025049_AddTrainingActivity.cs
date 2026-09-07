using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TotalPageCount",
                table: "TrainingContents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrainingActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonUserId = table.Column<string>(type: "TEXT", nullable: false),
                    TrainingContentId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrainingContentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ViewedPagesCsv = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    HighestWatchedSec = table.Column<int>(type: "INTEGER", nullable: false),
                    ActualDurationSec = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstOpenedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingActivities_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainingActivities_TrainingContents_TrainingContentId",
                        column: x => x.TrainingContentId,
                        principalTable: "TrainingContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingActivities_PersonUserId_TrainingContentId_TrainingContentVersion",
                table: "TrainingActivities",
                columns: new[] { "PersonUserId", "TrainingContentId", "TrainingContentVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingActivities_TrainingContentId",
                table: "TrainingActivities",
                column: "TrainingContentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrainingActivities");

            migrationBuilder.DropColumn(
                name: "TotalPageCount",
                table: "TrainingContents");
        }
    }
}
