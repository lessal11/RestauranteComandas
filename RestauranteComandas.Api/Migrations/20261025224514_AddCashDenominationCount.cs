using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestauranteComandas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCashDenominationCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CajaConteoDenominaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CajaSesionId = table.Column<int>(type: "integer", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CajaConteoDenominaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CajaConteoDenominaciones_CajaSesiones_CajaSesionId",
                        column: x => x.CajaSesionId,
                        principalTable: "CajaSesiones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CajaConteoDenominaciones_CajaSesionId_Valor",
                table: "CajaConteoDenominaciones",
                columns: new[] { "CajaSesionId", "Valor" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CajaConteoDenominaciones");
        }
    }
}
