using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestauranteComandas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSplitPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pagos_OrdenId",
                table: "Pagos");

            migrationBuilder.CreateTable(
                name: "PagoDetalles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PagoId = table.Column<int>(type: "integer", nullable: false),
                    OrdenDetalleId = table.Column<int>(type: "integer", nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    PrecioUnitario = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagoDetalles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PagoDetalles_OrdenDetalles_OrdenDetalleId",
                        column: x => x.OrdenDetalleId,
                        principalTable: "OrdenDetalles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagoDetalles_Pagos_PagoId",
                        column: x => x.PagoId,
                        principalTable: "Pagos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pagos_OrdenId",
                table: "Pagos",
                column: "OrdenId");

            migrationBuilder.CreateIndex(
                name: "IX_PagoDetalles_OrdenDetalleId",
                table: "PagoDetalles",
                column: "OrdenDetalleId");

            migrationBuilder.CreateIndex(
                name: "IX_PagoDetalles_PagoId",
                table: "PagoDetalles",
                column: "PagoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PagoDetalles");

            migrationBuilder.DropIndex(
                name: "IX_Pagos_OrdenId",
                table: "Pagos");

            migrationBuilder.CreateIndex(
                name: "IX_Pagos_OrdenId",
                table: "Pagos",
                column: "OrdenId",
                unique: true);
        }
    }
}
