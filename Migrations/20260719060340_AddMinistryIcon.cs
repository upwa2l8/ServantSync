using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServantSync.Migrations
{
    /// <inheritdoc />
    public partial class AddMinistryIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "Ministries",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Icon",
                table: "Ministries");
        }
    }
}
