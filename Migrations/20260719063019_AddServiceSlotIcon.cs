using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceSlotIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "ServiceSlots",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Icon",
                table: "ServiceSlots");
        }
    }
}
