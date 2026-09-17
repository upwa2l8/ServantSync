using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlotTradeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlotTradeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequesterUserId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetAssignmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetOwnerUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestedForUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ApproverRole = table.Column<int>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotTradeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotTradeRequests_Assignments_TargetAssignmentId",
                        column: x => x.TargetAssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SlotTradeRequests_People_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlotTradeRequests_RequesterUserId",
                table: "SlotTradeRequests",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SlotTradeRequests_TargetAssignmentId_Status",
                table: "SlotTradeRequests",
                columns: new[] { "TargetAssignmentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlotTradeRequests");
        }
    }
}
