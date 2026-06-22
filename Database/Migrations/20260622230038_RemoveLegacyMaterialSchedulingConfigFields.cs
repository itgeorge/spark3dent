using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyMaterialSchedulingConfigFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "SchedulingMaterialConfigs");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "SchedulingMaterialConfigs");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "SchedulingMaterialConfigs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "SchedulingMaterialConfigs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "SchedulingMaterialConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "SchedulingMaterialConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
