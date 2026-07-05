using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlotDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlotDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceSlotId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotDocuments_People_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "People",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlotDocuments_ServiceSlots_ServiceSlotId",
                        column: x => x.ServiceSlotId,
                        principalTable: "ServiceSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlotDocuments_ServiceSlotId_Category",
                table: "SlotDocuments",
                columns: new[] { "ServiceSlotId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_SlotDocuments_UploadedByUserId",
                table: "SlotDocuments",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlotDocuments");
        }
    }
}
