using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DCF.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShowLocationAndSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Shows",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ScoresAnnouncedTime",
                table: "Shows",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<bool>(
                name: "IsExhibition",
                table: "Shows",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Shows",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Shows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Shows",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShowScheduleEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShowId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    CorpsId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShowScheduleEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShowScheduleEntries_Corps_CorpsId",
                        column: x => x.CorpsId,
                        principalTable: "Corps",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShowScheduleEntries_Shows_ShowId",
                        column: x => x.ShowId,
                        principalTable: "Shows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShowScheduleEntries_CorpsId",
                table: "ShowScheduleEntries",
                column: "CorpsId");

            migrationBuilder.CreateIndex(
                name: "IX_ShowScheduleEntries_ShowId",
                table: "ShowScheduleEntries",
                column: "ShowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShowScheduleEntries");

            migrationBuilder.DropColumn(
                name: "IsExhibition",
                table: "Shows");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Shows");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Shows");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Shows");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Shows",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ScoresAnnouncedTime",
                table: "Shows",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
