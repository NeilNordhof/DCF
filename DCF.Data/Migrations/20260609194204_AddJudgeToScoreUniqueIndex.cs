using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DCF.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJudgeToScoreUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_CorpsId_ShowId_Caption",
                table: "Scores");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_CorpsId_ShowId_Caption_Judge",
                table: "Scores",
                columns: new[] { "CorpsId", "ShowId", "Caption", "Judge" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Scores_CorpsId_ShowId_Caption_Judge",
                table: "Scores");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_CorpsId_ShowId_Caption",
                table: "Scores",
                columns: new[] { "CorpsId", "ShowId", "Caption" },
                unique: true);
        }
    }
}
