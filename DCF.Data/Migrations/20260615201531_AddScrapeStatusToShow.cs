using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DCF.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScrapeStatusToShow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastScrapeAttemptAt",
                table: "Shows",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScrapeError",
                table: "Shows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScrapeStatus",
                table: "Shows",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastScrapeAttemptAt",
                table: "Shows");

            migrationBuilder.DropColumn(
                name: "ScrapeError",
                table: "Shows");

            migrationBuilder.DropColumn(
                name: "ScrapeStatus",
                table: "Shows");
        }
    }
}
