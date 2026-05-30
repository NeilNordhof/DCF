using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DCF.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShowTimezone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "Shows",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "Shows");
        }
    }
}
