using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingMaterialConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchedulingMaterialConfigs",
                columns: table => new
                {
                    Material = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    FixedLeadTimeBusinessDays = table.Column<int>(type: "INTEGER", nullable: false),
                    CapacityUnitsPerTooth = table.Column<decimal>(type: "TEXT", nullable: false),
                    TeethPerExtraLeadDay = table.Column<int>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingMaterialConfigs", x => x.Material);
                });

            var seededAt = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                table: "SchedulingMaterialConfigs",
                columns: new[]
                {
                    "Material",
                    "DisplayName",
                    "FixedLeadTimeBusinessDays",
                    "CapacityUnitsPerTooth",
                    "TeethPerExtraLeadDay",
                    "IsActive",
                    "SortOrder",
                    "CreatedAt",
                    "UpdatedAt"
                },
                values: new object[,]
                {
                    { "Pmma", "PMMA", 2, 1.0m, null, true, 10, seededAt, seededAt },
                    { "PmmaTelio", "PMMA Telio", 2, 1.0m, null, true, 20, seededAt, seededAt },
                    { "FullContourZirconia", "Full Contour Zirconia", 3, 1.0m, null, true, 30, seededAt, seededAt },
                    { "GlassCeramics", "Glass Ceramics / LiSi", 4, 1.0m, null, true, 40, seededAt, seededAt },
                    { "Pfm", "PFM", 4, 1.0m, 10, true, 50, seededAt, seededAt },
                    { "PfzLayeredZrCrown", "PFZ Layered Zr Crown", 4, 1.0m, 10, true, 60, seededAt, seededAt }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchedulingMaterialConfigs");
        }
    }
}
