using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Migrations
{
    /// <inheritdoc />
    public partial class AddSlotInterests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlotInterests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServiceSlotId = table.Column<int>(type: "int", nullable: false),
                    SubscribedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotInterests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotInterests_People_PersonUserId",
                        column: x => x.PersonUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SlotInterests_ServiceSlots_ServiceSlotId",
                        column: x => x.ServiceSlotId,
                        principalTable: "ServiceSlots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlotInterests_PersonUserId",
                table: "SlotInterests",
                column: "PersonUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SlotInterests_PersonUserId_ServiceSlotId",
                table: "SlotInterests",
                columns: new[] { "PersonUserId", "ServiceSlotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlotInterests_ServiceSlotId",
                table: "SlotInterests",
                column: "ServiceSlotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlotInterests");
        }
    }
}
