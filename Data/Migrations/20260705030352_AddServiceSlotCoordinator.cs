using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceSlotCoordinator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoordinatorEmail",
                table: "ServiceSlots",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoordinatorPersonUserId",
                table: "ServiceSlots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoordinatorPhone",
                table: "ServiceSlots",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceSlots_CoordinatorPersonUserId",
                table: "ServiceSlots",
                column: "CoordinatorPersonUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceSlots_People_CoordinatorPersonUserId",
                table: "ServiceSlots",
                column: "CoordinatorPersonUserId",
                principalTable: "People",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceSlots_People_CoordinatorPersonUserId",
                table: "ServiceSlots");

            migrationBuilder.DropIndex(
                name: "IX_ServiceSlots_CoordinatorPersonUserId",
                table: "ServiceSlots");

            migrationBuilder.DropColumn(
                name: "CoordinatorEmail",
                table: "ServiceSlots");

            migrationBuilder.DropColumn(
                name: "CoordinatorPersonUserId",
                table: "ServiceSlots");

            migrationBuilder.DropColumn(
                name: "CoordinatorPhone",
                table: "ServiceSlots");
        }
    }
}
