using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingOrderCreatedAtSort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SchedulingOrders_CreatedAt",
                table: "SchedulingOrders");

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUnixTimeMilliseconds",
                table: "SchedulingOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("UPDATE SchedulingOrders SET CreatedAtUnixTimeMilliseconds = CAST(strftime('%s', CreatedAt) AS INTEGER) * 1000 WHERE CreatedAtUnixTimeMilliseconds = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingOrders_CreatedAtUnixTimeMilliseconds",
                table: "SchedulingOrders",
                column: "CreatedAtUnixTimeMilliseconds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SchedulingOrders_CreatedAtUnixTimeMilliseconds",
                table: "SchedulingOrders");

            migrationBuilder.DropColumn(
                name: "CreatedAtUnixTimeMilliseconds",
                table: "SchedulingOrders");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingOrders_CreatedAt",
                table: "SchedulingOrders",
                column: "CreatedAt");
        }
    }
}
