using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Nickname = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    RepresentativeName = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyIdentifier = table.Column<string>(type: "TEXT", nullable: false),
                    VatIdentifier = table.Column<string>(type: "TEXT", nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    PostalCode = table.Column<string>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Nickname);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<string>(type: "TEXT", nullable: false),
                    NumberNumeric = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SellerName = table.Column<string>(type: "TEXT", nullable: false),
                    SellerRepresentativeName = table.Column<string>(type: "TEXT", nullable: false),
                    SellerCompanyIdentifier = table.Column<string>(type: "TEXT", nullable: false),
                    SellerVatIdentifier = table.Column<string>(type: "TEXT", nullable: true),
                    SellerAddress = table.Column<string>(type: "TEXT", nullable: false),
                    SellerCity = table.Column<string>(type: "TEXT", nullable: false),
                    SellerPostalCode = table.Column<string>(type: "TEXT", nullable: false),
                    SellerCountry = table.Column<string>(type: "TEXT", nullable: false),
                    BuyerName = table.Column<string>(type: "TEXT", nullable: false),
                    BuyerRepresentativeName = table.Column<string>(type: "TEXT", nullable: false),
                    BuyerCompanyIdentifier = table.Column<string>(type: "TEXT", nullable: false),
                    BuyerVatIdentifier = table.Column<string>(type: "TEXT", nullable: true),
                    BuyerAddress = table.Column<string>(type: "TEXT", nullable: false),
                    BuyerCity = table.Column<string>(type: "TEXT", nullable: false),
                    BuyerPostalCode = table.Column<string>(type: "TEXT", nullable: false),
                    BuyerCountry = table.Column<string>(type: "TEXT", nullable: false),
                    BankIban = table.Column<string>(type: "TEXT", nullable: false),
                    BankName = table.Column<string>(type: "TEXT", nullable: false),
                    BankBic = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceSequence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    LastNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSequence", x => x.Id);
                    table.CheckConstraint("CK_InvoiceSequence_Id", "Id = 1");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLineItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceEntityId = table.Column<long>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    AmountCents = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLineItems_Invoices_InvoiceEntityId",
                        column: x => x.InvoiceEntityId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLineItems_InvoiceEntityId",
                table: "InvoiceLineItems",
                column: "InvoiceEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Number",
                table: "Invoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_NumberNumeric",
                table: "Invoices",
                column: "NumberNumeric",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "InvoiceLineItems");

            migrationBuilder.DropTable(
                name: "InvoiceSequence");

            migrationBuilder.DropTable(
                name: "Invoices");
        }
    }
}
