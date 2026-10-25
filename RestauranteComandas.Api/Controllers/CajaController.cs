using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestauranteComandas.Api.Data;
using RestauranteComandas.Api.DTOs;
using RestauranteComandas.Api.Models;
using System.Security.Claims;

namespace RestauranteComandas.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Administrador,Caja")]
    public class CajaController : ControllerBase
    {
        private readonly RestauranteDbContext _context;

        private static readonly decimal[] DenominacionesPermitidas =
        {
            0.01m,
            0.05m,
            0.10m,
            0.25m,
            0.50m,
            1.00m,
            2.00m,
            5.00m,
            10.00m,
            20.00m,
            50.00m,
            100.00m
        };

        public CajaController(
            RestauranteDbContext context)
        {
            _context = context;
        }


        // =============================================
        // OBTENER CAJA ACTIVA
        // =============================================

        [HttpGet("activa")]
        public async Task<IActionResult> GetCajaActiva()
        {
            var caja =
                await _context.CajaSesiones
                    .Include(c => c.UsuarioApertura)
                    .Where(c =>
                        c.Estado == "Abierta"
                    )
                    .OrderByDescending(
                        c => c.FechaApertura
                    )
                    .Select(c => new
                    {
                        c.Id,

                        c.FechaApertura,

                        c.FondoInicial,

                        c.Estado,

                        usuarioAperturaId =
                            c.UsuarioAperturaId,

                        usuarioApertura =
                            c.UsuarioApertura != null
                                ? c.UsuarioApertura.Nombre
                                : ""
                    })
                    .FirstOrDefaultAsync();

            if (caja == null)
            {
                return NoContent();
            }

            return Ok(caja);
        }

        // =============================================
        // RESUMEN DE CAJA ACTIVA
        // =============================================

        [HttpGet("activa/resumen")]
        public async Task<IActionResult> GetResumenCajaActiva()
        {
            var caja =
                await _context.CajaSesiones
                    .Include(c => c.UsuarioApertura)
                    .FirstOrDefaultAsync(
                        c => c.Estado == "Abierta"
                    );

            if (caja == null)
            {
                return NoContent();
            }


            var pagos =
                await _context.Pagos
                    .Where(p =>
                        p.CajaSesionId == caja.Id &&
                        p.EstadoPago == "Confirmado"
                    )
                    .ToListAsync();


            var totalEfectivo =
                pagos
                    .Where(p =>
                        p.MetodoPago == "Efectivo"
                    )
                    .Sum(p => p.Monto);


            var totalTransferencia =
                pagos
                    .Where(p =>
                        p.MetodoPago == "Transferencia"
                    )
                    .Sum(p => p.Monto);


            var totalD1 =
                pagos
                    .Where(p =>
                        p.MetodoPago == "D1"
                    )
                    .Sum(p => p.Monto);


            var totalCobrado =
                pagos.Sum(
                    p => p.Monto
                );


            var efectivoEsperado =
                caja.FondoInicial +
                totalEfectivo;


            return Ok(new
            {
                cajaId =
                    caja.Id,

                caja.Estado,

                caja.FechaApertura,

                caja.FondoInicial,

                usuarioApertura =
                    caja.UsuarioApertura != null
                        ? caja.UsuarioApertura.Nombre
                        : "",

                cantidadPagos =
                    pagos.Count,

                totalEfectivo,

                totalTransferencia,

                totalD1,

                totalCobrado,

                efectivoEsperado
            });
        }

        // =============================================
        // ABRIR CAJA
        // =============================================

        [HttpPost("abrir")]
        public async Task<IActionResult> AbrirCaja(
            AbrirCajaDto dto)
        {
            if (dto.FondoInicial < 0)
            {
                return BadRequest(
                    "El fondo inicial no puede ser negativo"
                );
            }


            // -----------------------------------------
            // Identificar usuario autenticado
            // -----------------------------------------

            var usuarioIdClaim =
                User.FindFirst(
                    ClaimTypes.NameIdentifier
                )?.Value;

            if (
                !int.TryParse(
                    usuarioIdClaim,
                    out var usuarioId
                )
            )
            {
                return Unauthorized(
                    "No se pudo identificar al usuario autenticado"
                );
            }


            var usuario =
                await _context.Usuarios
                    .FirstOrDefaultAsync(
                        u =>
                            u.Id == usuarioId &&
                            u.Activo
                    );

            if (usuario == null)
            {
                return Unauthorized(
                    "El usuario autenticado no existe o está inactivo"
                );
            }


            // -----------------------------------------
            // Solo puede existir una caja abierta
            // -----------------------------------------

            var cajaAbierta =
                await _context.CajaSesiones
                    .Include(c => c.UsuarioApertura)
                    .FirstOrDefaultAsync(
                        c =>
                            c.Estado == "Abierta"
                    );

            if (cajaAbierta != null)
            {
                return Conflict(new
                {
                    mensaje =
                        "Ya existe una caja abierta.",

                    cajaId =
                        cajaAbierta.Id,

                    fechaApertura =
                        cajaAbierta.FechaApertura,

                    fondoInicial =
                        cajaAbierta.FondoInicial,

                    usuarioApertura =
                        cajaAbierta.UsuarioApertura != null
                            ? cajaAbierta.UsuarioApertura.Nombre
                            : ""
                });
            }


            // -----------------------------------------
            // Crear sesión
            // -----------------------------------------

            var caja =
                new CajaSesion
                {
                    UsuarioAperturaId =
                        usuario.Id,

                    FechaApertura =
                        DateTime.UtcNow,

                    FondoInicial =
                        dto.FondoInicial,

                    Estado =
                        "Abierta"
                };


            _context.CajaSesiones.Add(
                caja
            );


            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                /*
                 * Protección adicional.
                 *
                 * Si dos usuarios intentaron abrir
                 * caja prácticamente al mismo tiempo,
                 * el índice único de PostgreSQL evita
                 * crear dos sesiones abiertas.
                 */

                return Conflict(
                    "Ya existe una caja abierta."
                );
            }


            return Ok(new
            {
                mensaje =
                    "Caja abierta correctamente",

                caja = new
                {
                    caja.Id,

                    caja.FechaApertura,

                    caja.FondoInicial,

                    caja.Estado,

                    usuarioAperturaId =
                        usuario.Id,

                    usuarioApertura =
                        usuario.Nombre
                }
            });
        }

        // =============================================
        // CERRAR CAJA
        // =============================================

        [HttpPost("cerrar")]
        public async Task<IActionResult> CerrarCaja(
            CerrarCajaDto dto)
        {
            // =========================================
            // USUARIO AUTENTICADO
            // =========================================

            var usuarioIdClaim =
                User.FindFirst(
                    ClaimTypes.NameIdentifier
                )?.Value;

            if (
                !int.TryParse(
                    usuarioIdClaim,
                    out var usuarioId
                )
            )
            {
                return Unauthorized(
                    "No se pudo identificar al usuario autenticado"
                );
            }


            var usuario =
                await _context.Usuarios
                    .FirstOrDefaultAsync(
                        u =>
                            u.Id == usuarioId &&
                            u.Activo
                    );

            if (usuario == null)
            {
                return Unauthorized(
                    "El usuario autenticado no existe o está inactivo"
                );
            }


            // =========================================
            // BUSCAR CAJA ABIERTA
            // =========================================

            var caja =
                await _context.CajaSesiones
                    .Include(c => c.UsuarioApertura)
                    .FirstOrDefaultAsync(
                        c =>
                            c.Estado == "Abierta"
                    );

            if (caja == null)
            {
                return BadRequest(
                    "No existe una caja abierta para cerrar."
                );
            }


            // =========================================
            // VALIDAR DENOMINACIONES
            // =========================================

            if (
                dto.Denominaciones == null ||
                !dto.Denominaciones.Any()
            )
            {
                return BadRequest(
                    "Debe registrar el conteo de efectivo por denominaciones."
                );
            }


            if (
                dto.Denominaciones.Any(
                    d => d.Cantidad < 0
                )
            )
            {
                return BadRequest(
                    "La cantidad de una denominación no puede ser negativa."
                );
            }


            if (
                dto.Denominaciones.Any(
                    d =>
                        !DenominacionesPermitidas.Contains(
                            d.Valor
                        )
                )
            )
            {
                return BadRequest(
                    "Se recibió una denominación de efectivo no permitida."
                );
            }


            var denominacionesDuplicadas =
                dto.Denominaciones
                    .GroupBy(
                        d => d.Valor
                    )
                    .Any(
                        g => g.Count() > 1
                    );


            if (denominacionesDuplicadas)
            {
                return BadRequest(
                    "No se puede registrar dos veces la misma denominación."
                );
            }


            // =========================================
            // PAGOS CONFIRMADOS
            // =========================================

            var pagos =
                await _context.Pagos
                    .Where(p =>
                        p.CajaSesionId == caja.Id &&
                        p.EstadoPago == "Confirmado"
                    )
                    .ToListAsync();


            var totalEfectivo =
                pagos
                    .Where(p =>
                        p.MetodoPago == "Efectivo"
                    )
                    .Sum(p => p.Monto);


            var totalTransferencia =
                pagos
                    .Where(p =>
                        p.MetodoPago == "Transferencia"
                    )
                    .Sum(p => p.Monto);


            var totalD1 =
                pagos
                    .Where(p =>
                        p.MetodoPago == "D1"
                    )
                    .Sum(p => p.Monto);


            var totalCobrado =
                pagos.Sum(
                    p => p.Monto
                );


            // =========================================
            // CUADRE DE EFECTIVO
            // =========================================

            var efectivoEsperado =
                caja.FondoInicial +
                totalEfectivo;


            var efectivoContado =
                dto.Denominaciones
                    .Sum(
                        d =>
                            d.Valor *
                            d.Cantidad
                    );


            var diferencia =
                efectivoContado -
                efectivoEsperado;


            if (
                diferencia != 0m &&
                string.IsNullOrWhiteSpace(
                    dto.ObservacionCierre
                )
            )
            {
                return BadRequest(
                    $"Existe una diferencia de ${diferencia:F2}. " +
                    "Debe ingresar una observación antes de cerrar la caja."
                );
            }


            // =========================================
            // SNAPSHOT DE CIERRE
            // =========================================

            caja.UsuarioCierreId =
                usuario.Id;

            caja.FechaCierre =
                DateTime.UtcNow;

            caja.EfectivoContado =
                efectivoContado;

            caja.TotalEfectivo =
                totalEfectivo;

            caja.TotalTransferencia =
                totalTransferencia;

            caja.TotalD1 =
                totalD1;

            caja.TotalCobrado =
                totalCobrado;

            caja.EfectivoEsperado =
                efectivoEsperado;

            caja.Diferencia =
                diferencia;

            caja.ObservacionCierre =
                string.IsNullOrWhiteSpace(
                    dto.ObservacionCierre
                )
                    ? null
                    : dto.ObservacionCierre.Trim();

            caja.Estado =
                "Cerrada";


            // =========================================
            // GUARDAR DENOMINACIONES
            // =========================================

            foreach (
                var denominacion in
                    dto.Denominaciones
                        .Where(
                            d => d.Cantidad > 0
                        )
            )
            {
                var subtotal =
                    denominacion.Valor *
                    denominacion.Cantidad;


                var conteo =
                    new CajaConteoDenominacion
                    {
                        CajaSesionId =
                            caja.Id,

                        Valor =
                            denominacion.Valor,

                        Cantidad =
                            denominacion.Cantidad,

                        Subtotal =
                            subtotal
                    };


                _context.CajaConteoDenominaciones
                    .Add(
                        conteo
                    );
            }


            await _context.SaveChangesAsync();


            string estadoCuadre;

            if (diferencia == 0m)
            {
                estadoCuadre =
                    "Cuadrada";
            }
            else if (diferencia < 0m)
            {
                estadoCuadre =
                    "Faltante";
            }
            else
            {
                estadoCuadre =
                    "Sobrante";
            }


            return Ok(new
            {
                mensaje =
                    "Caja cerrada correctamente",

                cajaId =
                    caja.Id,

                caja.FechaApertura,

                caja.FechaCierre,

                usuarioApertura =
                    caja.UsuarioApertura != null
                        ? caja.UsuarioApertura.Nombre
                        : "",

                usuarioCierre =
                    usuario.Nombre,

                caja.FondoInicial,

                cantidadPagos =
                    pagos.Count,

                totalEfectivo,

                totalTransferencia,

                totalD1,

                totalCobrado,

                efectivoEsperado,

                efectivoContado =
                    caja.EfectivoContado,

                diferencia,

                estadoCuadre,

                observacion =
                    caja.ObservacionCierre,

                denominaciones =
                    dto.Denominaciones
                        .Where(
                            d => d.Cantidad > 0
                        )
                        .OrderBy(
                            d => d.Valor
                        )
                        .Select(
                            d => new
                            {
                                valor =
                                    d.Valor,

                                cantidad =
                                    d.Cantidad,

                                subtotal =
                                    d.Valor *
                                    d.Cantidad
                            }
                        )
                        .ToList()
            });
        }

        // =============================================
        // HISTORIAL DE CIERRES DE CAJA
        // =============================================

        [HttpGet("historial")]
        public async Task<IActionResult> GetHistorialCaja(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            var query =
                _context.CajaSesiones
                    .Include(c => c.UsuarioApertura)
                    .Include(c => c.UsuarioCierre)
                    .Where(c =>
                        c.Estado == "Cerrada" &&
                        c.FechaCierre != null
                    )
                    .AsQueryable();


            // Ecuador continental UTC-5
            var ecuadorOffset =
                TimeSpan.FromHours(-5);


            // =========================================
            // FILTRO DESDE
            // =========================================

            if (desde.HasValue)
            {
                var inicioDesdeUtc =
                    new DateTimeOffset(
                        desde.Value.Year,
                        desde.Value.Month,
                        desde.Value.Day,
                        0,
                        0,
                        0,
                        ecuadorOffset
                    )
                    .UtcDateTime;

                query =
                    query.Where(c =>
                        c.FechaCierre >=
                        inicioDesdeUtc
                    );
            }


            // =========================================
            // FILTRO HASTA
            // =========================================

            if (hasta.HasValue)
            {
                var finHastaUtc =
                    new DateTimeOffset(
                        hasta.Value.Year,
                        hasta.Value.Month,
                        hasta.Value.Day,
                        0,
                        0,
                        0,
                        ecuadorOffset
                    )
                    .AddDays(1)
                    .UtcDateTime;

                query =
                    query.Where(c =>
                        c.FechaCierre <
                        finHastaUtc
                    );
            }


            var cierres =
                await query
                    .OrderByDescending(
                        c => c.FechaCierre
                    )
                    .Select(c => new
                    {
                        c.Id,

                        c.FechaApertura,

                        c.FechaCierre,

                        c.FondoInicial,

                        c.Estado,

                        usuarioApertura =
                            c.UsuarioApertura != null
                                ? c.UsuarioApertura.Nombre
                                : "",

                        usuarioCierre =
                            c.UsuarioCierre != null
                                ? c.UsuarioCierre.Nombre
                                : "",

                        totalEfectivo =
                            c.TotalEfectivo ?? 0,

                        totalTransferencia =
                            c.TotalTransferencia ?? 0,

                        totalD1 =
                            c.TotalD1 ?? 0,

                        totalCobrado =
                            c.TotalCobrado ?? 0,

                        efectivoEsperado =
                            c.EfectivoEsperado ?? 0,

                        efectivoContado =
                            c.EfectivoContado ?? 0,

                        diferencia =
                            c.Diferencia ?? 0,

                        c.ObservacionCierre,

                        estadoCuadre =
                            (c.Diferencia ?? 0) == 0
                                ? "Cuadrada"
                                : (c.Diferencia ?? 0) < 0
                                    ? "Faltante"
                                    : "Sobrante"
                    })
                    .ToListAsync();


            return Ok(cierres);
        }

        // =============================================
        // DETALLE DE UNA SESIÓN DE CAJA
        // =============================================

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCajaPorId(
            int id)
        {
            var caja =
                await _context.CajaSesiones
                    .Include(c => c.UsuarioApertura)
                    .Include(c => c.UsuarioCierre)
                    .Where(c =>
                        c.Id == id
                    )
                    .Select(c => new
                    {
                        c.Id,

                        c.Estado,

                        c.FechaApertura,

                        c.FechaCierre,

                        c.FondoInicial,

                        usuarioAperturaId =
                            c.UsuarioAperturaId,

                        usuarioApertura =
                            c.UsuarioApertura != null
                                ? c.UsuarioApertura.Nombre
                                : "",

                        usuarioCierreId =
                            c.UsuarioCierreId,

                        usuarioCierre =
                            c.UsuarioCierre != null
                                ? c.UsuarioCierre.Nombre
                                : "",

                        totalEfectivo =
                            c.TotalEfectivo ?? 0,

                        totalTransferencia =
                            c.TotalTransferencia ?? 0,

                        totalD1 =
                            c.TotalD1 ?? 0,

                        totalCobrado =
                            c.TotalCobrado ?? 0,

                        efectivoEsperado =
                            c.EfectivoEsperado ?? 0,

                        efectivoContado =
                            c.EfectivoContado ?? 0,

                        diferencia =
                            c.Diferencia ?? 0,

                        c.ObservacionCierre,

                        cantidadPagos =
                            c.Pagos.Count(p =>
                                p.EstadoPago ==
                                "Confirmado"
                            ),

                        estadoCuadre =
                            c.Estado == "Abierta"
                                ? "Pendiente"
                                : (c.Diferencia ?? 0) == 0
                                    ? "Cuadrada"
                                    : (c.Diferencia ?? 0) < 0
                                        ? "Faltante"
                                        : "Sobrante"
                    })
                    .FirstOrDefaultAsync();


            if (caja == null)
            {
                return NotFound(
                    "Sesión de caja no encontrada"
                );
            }


            return Ok(caja);
        }
    }
}