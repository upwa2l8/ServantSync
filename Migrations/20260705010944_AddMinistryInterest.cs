using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMinistryInterest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MinistryInterests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonUserId = table.Column<string>(type: "TEXT", nullable: false),
                    MinistryId = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinistryInterests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MinistryInterests_Ministries_MinistryId",
                        column: x => x.MinistryId,
                        principalTable: "Ministries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MinistryInterests_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MinistryInterests_MinistryId",
                table: "MinistryInterests",
                column: "MinistryId");

            migrationBuilder.CreateIndex(
                name: "IX_MinistryInterests_PersonUserId",
                table: "MinistryInterests",
                column: "PersonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MinistryInterests_PersonUserId_MinistryId",
                table: "MinistryInterests",
                columns: new[] { "PersonUserId", "MinistryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MinistryInterests");
        }
    }
}
