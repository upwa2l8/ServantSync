using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlotCapacityAndOccurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Capacity",
                table: "ServiceSlots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "SlotOccurrences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceSlotId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CapacityOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotOccurrences_ServiceSlots_ServiceSlotId",
                        column: x => x.ServiceSlotId,
                        principalTable: "ServiceSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlotOccurrences_ServiceSlotId_StartUtc",
                table: "SlotOccurrences",
                columns: new[] { "ServiceSlotId", "StartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlotOccurrences_StartUtc",
                table: "SlotOccurrences",
                column: "StartUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlotOccurrences");

            migrationBuilder.DropColumn(
                name: "Capacity",
                table: "ServiceSlots");
        }
    }
}
