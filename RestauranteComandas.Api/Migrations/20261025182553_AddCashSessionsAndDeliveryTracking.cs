using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestauranteComandas.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCashSessionsAndDeliveryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CajaSesionId",
                table: "Pagos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UsuarioRegistroId",
                table: "Pagos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CantidadEntregada",
                table: "OrdenDetalles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CajaSesiones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UsuarioAperturaId = table.Column<int>(type: "integer", nullable: false),
                    FechaApertura = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FondoInicial = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    UsuarioCierreId = table.Column<int>(type: "integer", nullable: true),
                    FechaCierre = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EfectivoContado = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TotalEfectivo = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TotalTransferencia = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TotalD1 = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TotalCobrado = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    EfectivoEsperado = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    Diferencia = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    ObservacionCierre = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CajaSesiones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CajaSesiones_Usuarios_UsuarioAperturaId",
                        column: x => x.UsuarioAperturaId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CajaSesiones_Usuarios_UsuarioCierreId",
                        column: x => x.UsuarioCierreId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pagos_CajaSesionId",
                table: "Pagos",
                column: "CajaSesionId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagos_UsuarioRegistroId",
                table: "Pagos",
                column: "UsuarioRegistroId");

            migrationBuilder.CreateIndex(
                name: "IX_CajaSesiones_Estado",
                table: "CajaSesiones",
                column: "Estado",
                unique: true,
                filter: "\"Estado\" = 'Abierta'");

            migrationBuilder.CreateIndex(
                name: "IX_CajaSesiones_UsuarioAperturaId",
                table: "CajaSesiones",
                column: "UsuarioAperturaId");

            migrationBuilder.CreateIndex(
                name: "IX_CajaSesiones_UsuarioCierreId",
                table: "CajaSesiones",
                column: "UsuarioCierreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pagos_CajaSesiones_CajaSesionId",
                table: "Pagos",
                column: "CajaSesionId",
                principalTable: "CajaSesiones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pagos_Usuarios_UsuarioRegistroId",
                table: "Pagos",
                column: "UsuarioRegistroId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pagos_CajaSesiones_CajaSesionId",
                table: "Pagos");

            migrationBuilder.DropForeignKey(
                name: "FK_Pagos_Usuarios_UsuarioRegistroId",
                table: "Pagos");

            migrationBuilder.DropTable(
                name: "CajaSesiones");

            migrationBuilder.DropIndex(
                name: "IX_Pagos_CajaSesionId",
                table: "Pagos");

            migrationBuilder.DropIndex(
                name: "IX_Pagos_UsuarioRegistroId",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "CajaSesionId",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "UsuarioRegistroId",
                table: "Pagos");

            migrationBuilder.DropColumn(
                name: "CantidadEntregada",
                table: "OrdenDetalles");
        }
    }
}
