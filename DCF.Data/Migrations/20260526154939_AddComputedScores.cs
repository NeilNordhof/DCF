using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DCF.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComputedScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComputedScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShowId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorpsId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneralEffectCombined = table.Column<double>(type: "double precision", nullable: false),
                    GeneralEffect1 = table.Column<double>(type: "double precision", nullable: false),
                    GeneralEffect2 = table.Column<double>(type: "double precision", nullable: false),
                    VisualCombined = table.Column<double>(type: "double precision", nullable: false),
                    Visual = table.Column<double>(type: "double precision", nullable: false),
                    Colorguard = table.Column<double>(type: "double precision", nullable: false),
                    VisualProficiency = table.Column<double>(type: "double precision", nullable: false),
                    VisualAnalysis = table.Column<double>(type: "double precision", nullable: false),
                    MusicCombined = table.Column<double>(type: "double precision", nullable: false),
                    Brass = table.Column<double>(type: "double precision", nullable: false),
                    Percussion = table.Column<double>(type: "double precision", nullable: false),
                    MusicAnalysis = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComputedScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComputedScores_Corps_CorpsId",
                        column: x => x.CorpsId,
                        principalTable: "Corps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComputedScores_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComputedScores_Shows_ShowId",
                        column: x => x.ShowId,
                        principalTable: "Shows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComputedScores_CorpsId",
                table: "ComputedScores",
                column: "CorpsId");

            migrationBuilder.CreateIndex(
                name: "IX_ComputedScores_SeasonId",
                table: "ComputedScores",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_ComputedScores_ShowId_CorpsId",
                table: "ComputedScores",
                columns: new[] { "ShowId", "CorpsId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComputedScores");
        }
    }
}
