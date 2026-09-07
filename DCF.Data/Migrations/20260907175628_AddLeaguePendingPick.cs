using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DCF.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaguePendingPick : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PendingPickCaption",
                table: "Leagues",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingPickCorpsId",
                table: "Leagues",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingPickCaption",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "PendingPickCorpsId",
                table: "Leagues");
        }
    }
}
