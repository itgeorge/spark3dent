using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class ConvertSchedulingOrderShadeToEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Shade",
                table: "SchedulingOrders");

            migrationBuilder.AddColumn<int>(
                name: "Shade",
                table: "SchedulingOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Shade",
                table: "SchedulingOrders");

            migrationBuilder.AddColumn<string>(
                name: "Shade",
                table: "SchedulingOrders",
                type: "TEXT",
                nullable: true);
        }
    }
}
