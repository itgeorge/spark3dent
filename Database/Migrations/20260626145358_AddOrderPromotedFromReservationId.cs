using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPromotedFromReservationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PromotedFromReservationId",
                table: "SchedulingOrders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingOrders_PromotedFromReservationId",
                table: "SchedulingOrders",
                column: "PromotedFromReservationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SchedulingOrders_PromotedFromReservationId",
                table: "SchedulingOrders");

            migrationBuilder.DropColumn(
                name: "PromotedFromReservationId",
                table: "SchedulingOrders");
        }
    }
}
