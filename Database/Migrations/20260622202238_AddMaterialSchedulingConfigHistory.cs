using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialSchedulingConfigHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE "SchedulingMaterialConfigs_new" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_SchedulingMaterialConfigs" PRIMARY KEY AUTOINCREMENT,
                    "Material" TEXT NOT NULL,
                    "ActiveFromDate" TEXT NOT NULL,
                    "DisplayName" TEXT NULL,
                    "FixedLeadTimeBusinessDays" INTEGER NOT NULL,
                    "CapacityUnitsPerTooth" TEXT NOT NULL,
                    "TeethPerExtraLeadDay" INTEGER NULL,
                    "IsActive" INTEGER NOT NULL DEFAULT 1,
                    "SortOrder" INTEGER NOT NULL DEFAULT 0,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "SchedulingMaterialConfigs_new" (
                    "Material",
                    "ActiveFromDate",
                    "DisplayName",
                    "FixedLeadTimeBusinessDays",
                    "CapacityUnitsPerTooth",
                    "TeethPerExtraLeadDay",
                    "IsActive",
                    "SortOrder",
                    "CreatedAt",
                    "UpdatedAt")
                SELECT
                    "Material",
                    '2026-01-01',
                    "DisplayName",
                    "FixedLeadTimeBusinessDays",
                    "CapacityUnitsPerTooth",
                    "TeethPerExtraLeadDay",
                    "IsActive",
                    "SortOrder",
                    "CreatedAt",
                    "UpdatedAt"
                FROM "SchedulingMaterialConfigs";
                """);

            migrationBuilder.DropTable(name: "SchedulingMaterialConfigs");
            migrationBuilder.RenameTable(name: "SchedulingMaterialConfigs_new", newName: "SchedulingMaterialConfigs");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingMaterialConfigs_Material",
                table: "SchedulingMaterialConfigs",
                column: "Material");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingMaterialConfigs_Material_ActiveFromDate",
                table: "SchedulingMaterialConfigs",
                columns: new[] { "Material", "ActiveFromDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE "SchedulingMaterialConfigs_old" (
                    "Material" TEXT NOT NULL CONSTRAINT "PK_SchedulingMaterialConfigs" PRIMARY KEY,
                    "DisplayName" TEXT NULL,
                    "FixedLeadTimeBusinessDays" INTEGER NOT NULL,
                    "CapacityUnitsPerTooth" TEXT NOT NULL,
                    "TeethPerExtraLeadDay" INTEGER NULL,
                    "IsActive" INTEGER NOT NULL DEFAULT 1,
                    "SortOrder" INTEGER NOT NULL DEFAULT 0,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "SchedulingMaterialConfigs_old" (
                    "Material",
                    "DisplayName",
                    "FixedLeadTimeBusinessDays",
                    "CapacityUnitsPerTooth",
                    "TeethPerExtraLeadDay",
                    "IsActive",
                    "SortOrder",
                    "CreatedAt",
                    "UpdatedAt")
                SELECT
                    c."Material",
                    c."DisplayName",
                    c."FixedLeadTimeBusinessDays",
                    c."CapacityUnitsPerTooth",
                    c."TeethPerExtraLeadDay",
                    c."IsActive",
                    c."SortOrder",
                    c."CreatedAt",
                    c."UpdatedAt"
                FROM "SchedulingMaterialConfigs" c
                WHERE c."Id" = (
                    SELECT c2."Id"
                    FROM "SchedulingMaterialConfigs" c2
                    WHERE c2."Material" = c."Material"
                    ORDER BY c2."ActiveFromDate" DESC, c2."Id" DESC
                    LIMIT 1
                );
                """);

            migrationBuilder.DropTable(name: "SchedulingMaterialConfigs");
            migrationBuilder.RenameTable(name: "SchedulingMaterialConfigs_old", newName: "SchedulingMaterialConfigs");
        }
    }
}
