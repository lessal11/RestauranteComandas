using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestauranteComandas.Api.Data;
using RestauranteComandas.Api.DTOs;
using RestauranteComandas.Api.Hubs;
using RestauranteComandas.Api.Models;
using System.Text;
using Microsoft.AspNetCore.Authorization;

namespace RestauranteComandas.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PagosController : ControllerBase
    {
        private readonly RestauranteDbContext _context;
        private readonly IHubContext<CocinaHub> _hubContext;

        public PagosController(RestauranteDbContext context, IHubContext<CocinaHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GetPagos()
        {
            var pagos = await _context.Pagos
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Mesa)
                .OrderByDescending(p => p.FechaPago)
                .Select(p => new
                {
                    p.Id,
                    p.OrdenId,
                    Mesa = p.Orden != null && p.Orden.Mesa != null ? p.Orden.Mesa.Numero : 0,
                    p.MetodoPago,
                    p.Monto,
                    p.Referencia,
                    p.FechaPago,
                    p.EstadoPago
                })
                .ToListAsync();

            return Ok(pagos);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GetPago(int id)
        {
            var pago = await _context.Pagos
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Mesa)
                .Where(p => p.Id == id)
                .Select(p => new
                {
                    p.Id,
                    p.OrdenId,
                    Mesa = p.Orden != null && p.Orden.Mesa != null ? p.Orden.Mesa.Numero : 0,
                    p.MetodoPago,
                    p.Monto,
                    p.Referencia,
                    p.FechaPago,
                    p.EstadoPago
                })
                .FirstOrDefaultAsync();

            if (pago == null)
            {
                return NotFound("Pago no encontrado");
            }

            return Ok(pago);
        }

        [HttpGet("orden/{ordenId}")]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GetPagoPorOrden(int ordenId)
        {
            var pago = await _context.Pagos
                .Where(p => p.OrdenId == ordenId)
                .Select(p => new
                {
                    p.Id,
                    p.OrdenId,
                    p.MetodoPago,
                    p.Monto,
                    p.Referencia,
                    p.FechaPago,
                    p.EstadoPago
                })
                .FirstOrDefaultAsync();

            if (pago == null)
            {
                return NotFound("No existe pago registrado para esta orden");
            }

            return Ok(pago);
        }

        // =========================================================
        // RESUMEN DE PAGO DE UNA ORDEN
        // =========================================================

        [HttpGet("orden/{ordenId}/resumen")]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GetResumenPagoOrden(int ordenId)
        {
            var orden = await _context.Ordenes
                .Include(o => o.Mesa)
                .Include(o => o.Detalles)
                    .ThenInclude(d => d.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == ordenId);

            if (orden == null)
            {
                return NotFound("Orden no encontrada");
            }

            var cantidadesPagadas = await _context.PagoDetalles
                .Where(pd =>
                    pd.OrdenDetalle != null &&
                    pd.OrdenDetalle.OrdenId == ordenId &&
                    pd.Pago != null &&
                    pd.Pago.EstadoPago == "Confirmado")
                .GroupBy(pd => pd.OrdenDetalleId)
                .Select(g => new
                {
                    OrdenDetalleId = g.Key,
                    CantidadPagada = g.Sum(x => x.Cantidad)
                })
                .ToDictionaryAsync(
                    x => x.OrdenDetalleId,
                    x => x.CantidadPagada
                );

            var totalPagado = await _context.Pagos
                .Where(p =>
                    p.OrdenId == ordenId &&
                    p.EstadoPago == "Confirmado")
                .SumAsync(p => (decimal?)p.Monto) ?? 0;

            var pendiente = Math.Max(
                0,
                orden.Total - totalPagado
            );

            string estadoPago;

            if (totalPagado <= 0)
            {
                estadoPago = "Sin pago";
            }
            else if (pendiente > 0)
            {
                estadoPago = "Pago parcial";
            }
            else
            {
                estadoPago = "Pagado";
            }

            var detalles = orden.Detalles
                .OrderBy(d => d.Id)
                .Select(d =>
                {
                    var cantidadPagada =
                        cantidadesPagadas.TryGetValue(
                            d.Id,
                            out var pagada)
                            ? pagada
                            : 0;

                    var cantidadPendiente =
                        Math.Max(
                            0,
                            d.Cantidad - cantidadPagada
                        );

                    return new
                    {
                        d.Id,

                        d.MenuItemId,

                        Plato =
                            d.MenuItem != null
                                ? d.MenuItem.Nombre
                                : "Producto",

                        d.Cantidad,

                        cantidadPagada,

                        cantidadPendiente,

                        d.PrecioUnitario,

                        d.Subtotal,

                        subtotalPagado =
                            cantidadPagada *
                            d.PrecioUnitario,

                        subtotalPendiente =
                            cantidadPendiente *
                            d.PrecioUnitario,

                        d.DetallePersonalizado
                    };
                })
                .ToList();

            return Ok(new
            {
                ordenId = orden.Id,

                mesa =
                    orden.Mesa != null
                        ? orden.Mesa.Numero
                        : 0,

                estadoOrden = orden.Estado,

                estadoPago,

                total = orden.Total,

                totalPagado,

                pendiente,

                detalles
            });
        }

        // =========================================================
        // REGISTRAR PAGO PARCIAL / CUENTA SEPARADA
        // =========================================================

        [HttpPost("parcial")]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> RegistrarPagoParcial(
            RegistrarPagoParcialDto dto)
        {
            if (dto.OrdenId <= 0)
            {
                return BadRequest(
                    "Debe seleccionar una orden"
                );
            }

            if (
                dto.Detalles == null ||
                !dto.Detalles.Any()
            )
            {
                return BadRequest(
                    "Debe seleccionar al menos un producto para pagar"
                );
            }

            var metodosPermitidos =
                new[]
                {
            "Efectivo",
            "Transferencia",
            "D1"
                };

            if (
                string.IsNullOrWhiteSpace(dto.MetodoPago) ||
                !metodosPermitidos.Contains(dto.MetodoPago)
            )
            {
                return BadRequest(
                    "Método de pago no permitido"
                );
            }

            if (
                dto.Detalles.Any(
                    d => d.Cantidad <= 0
                )
            )
            {
                return BadRequest(
                    "Las cantidades a pagar deben ser mayores a cero"
                );
            }

            /*
             * Evitamos que el frontend mande dos veces
             * el mismo OrdenDetalle dentro del pago.
             */
            var idsDuplicados =
                dto.Detalles
                    .GroupBy(d => d.OrdenDetalleId)
                    .Any(g => g.Count() > 1);

            if (idsDuplicados)
            {
                return BadRequest(
                    "Un mismo producto no puede aparecer dos veces en el pago"
                );
            }

            await using var transaccion =
                await _context.Database
                    .BeginTransactionAsync();

            try
            {
                var orden =
                    await _context.Ordenes
                        .Include(o => o.Mesa)
                        .Include(o => o.Detalles)
                            .ThenInclude(d => d.MenuItem)
                        .FirstOrDefaultAsync(
                            o => o.Id == dto.OrdenId
                        );

                if (orden == null)
                {
                    return NotFound(
                        "Orden no encontrada"
                    );
                }

                if (orden.Estado == "Cancelado")
                {
                    return BadRequest(
                        "No se puede pagar una orden cancelada"
                    );
                }

                if (orden.Estado == "Pagado")
                {
                    return BadRequest(
                        "Esta orden ya está completamente pagada"
                    );
                }

                var detalleIds =
                    dto.Detalles
                        .Select(d => d.OrdenDetalleId)
                        .ToList();

                var detallesOrden =
                    orden.Detalles
                        .Where(d =>
                            detalleIds.Contains(d.Id)
                        )
                        .ToDictionary(d => d.Id);

                if (
                    detallesOrden.Count !=
                    detalleIds.Count
                )
                {
                    return BadRequest(
                        "Uno o más productos no pertenecen a esta orden"
                    );
                }

                var pagadoAnterior =
                    await _context.PagoDetalles
                        .Where(pd =>
                            detalleIds.Contains(
                                pd.OrdenDetalleId
                            ) &&
                            pd.Pago != null &&
                            pd.Pago.EstadoPago ==
                                "Confirmado"
                        )
                        .GroupBy(
                            pd => pd.OrdenDetalleId
                        )
                        .Select(g => new
                        {
                            OrdenDetalleId = g.Key,

                            Cantidad =
                                g.Sum(x => x.Cantidad)
                        })
                        .ToDictionaryAsync(
                            x => x.OrdenDetalleId,
                            x => x.Cantidad
                        );

                decimal montoPago = 0;

                var pagoDetalles =
                    new List<PagoDetalle>();

                foreach (
                    var detalleDto in dto.Detalles
                )
                {
                    var detalle =
                        detallesOrden[
                            detalleDto.OrdenDetalleId
                        ];

                    var cantidadYaPagada =
                        pagadoAnterior.TryGetValue(
                            detalle.Id,
                            out var pagada
                        )
                            ? pagada
                            : 0;

                    var cantidadDisponible =
                        detalle.Cantidad -
                        cantidadYaPagada;

                    if (
                        detalleDto.Cantidad >
                        cantidadDisponible
                    )
                    {
                        return BadRequest(
                            $"No se pueden pagar {detalleDto.Cantidad} unidades de " +
                            $"{detalle.MenuItem?.Nombre ?? "Producto"}. " +
                            $"Solo quedan {cantidadDisponible} pendientes."
                        );
                    }

                    var subtotal =
                        detalle.PrecioUnitario *
                        detalleDto.Cantidad;

                    montoPago += subtotal;

                    pagoDetalles.Add(
                        new PagoDetalle
                        {
                            OrdenDetalleId =
                                detalle.Id,

                            Cantidad =
                                detalleDto.Cantidad,

                            PrecioUnitario =
                                detalle.PrecioUnitario,

                            Subtotal =
                                subtotal
                        }
                    );
                }

                if (montoPago <= 0)
                {
                    return BadRequest(
                        "El pago no tiene un monto válido"
                    );
                }

                var pago =
                    new Pago
                    {
                        OrdenId =
                            orden.Id,

                        MetodoPago =
                            dto.MetodoPago,

                        Monto =
                            montoPago,

                        Referencia =
                            string.IsNullOrWhiteSpace(
                                dto.Referencia
                            )
                                ? "Sin referencia"
                                : dto.Referencia.Trim(),

                        FechaPago =
                            DateTime.UtcNow,

                        EstadoPago =
                            "Confirmado",

                        Detalles =
                            pagoDetalles
                    };

                _context.Pagos.Add(
                    pago
                );

                await _context.SaveChangesAsync();

                var totalPagado =
                    await _context.Pagos
                        .Where(p =>
                            p.OrdenId ==
                                orden.Id &&
                            p.EstadoPago ==
                                "Confirmado"
                        )
                        .SumAsync(
                            p => (decimal?)p.Monto
                        ) ?? 0;

                var pendiente =
                    Math.Max(
                        0,
                        orden.Total - totalPagado
                    );

                var pagoCompleto =
                    pendiente <= 0.001m;

                /*
                 * IMPORTANTE:
                 *
                 * Si es un pago parcial NO cambiamos
                 * Pendiente / En preparación / Listo.
                 *
                 * Solo cuando se termina de pagar
                 * toda la orden pasa a Pagado.
                 */
                if (pagoCompleto)
                {
                    orden.Estado =
                        "Pagado";

                    if (orden.Mesa != null)
                    {
                        orden.Mesa.Estado =
                            "Disponible";
                    }

                    await _context.SaveChangesAsync();
                }

                await transaccion
                    .CommitAsync();

                if (pagoCompleto)
                {
                    await _hubContext
                        .Clients.All
                        .SendAsync(
                            "EstadoOrdenActualizado",
                            new
                            {
                                id =
                                    orden.Id,

                                estado =
                                    orden.Estado
                            }
                        );
                }

                return Ok(new
                {
                    mensaje =
                        pagoCompleto
                            ? "Pago registrado. La orden quedó completamente pagada."
                            : "Pago parcial registrado correctamente.",

                    pagoId =
                        pago.Id,

                    ordenId =
                        orden.Id,

                    montoPago =
                        pago.Monto,

                    totalOrden =
                        orden.Total,

                    totalPagado,

                    pendiente,

                    estadoPago =
                        pagoCompleto
                            ? "Pagado"
                            : "Pago parcial",

                    estadoOrden =
                        orden.Estado
                });
            }
            catch
            {
                await transaccion
                    .RollbackAsync();

                throw;
            }
        }

        [HttpPost]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> RegistrarPago(RegistrarPagoDto dto)
        {
            if (dto.OrdenId <= 0)
            {
                return BadRequest("Debe seleccionar una orden");
            }

            if (string.IsNullOrWhiteSpace(dto.MetodoPago))
            {
                return BadRequest("Debe seleccionar un método de pago");
            }

            if (dto.Monto <= 0)
            {
                return BadRequest("El monto debe ser mayor a 0");
            }

            var metodosPermitidos = new List<string>
            {
                "Efectivo",
                "Transferencia",
                "D1"
            };

            if (!metodosPermitidos.Contains(dto.MetodoPago))
            {
                return BadRequest("Método de pago no permitido");
            }

            var orden = await _context.Ordenes
                .Include(o => o.Mesa)
                .Include(o => o.Pagos)
                .FirstOrDefaultAsync(o => o.Id == dto.OrdenId);

            if (orden == null)
            {
                return NotFound("La orden seleccionada no existe");
            }

            if (orden.Pagos.Any())
            {
                return BadRequest("Esta orden ya tiene un pago registrado");
            }

            if (orden.Estado == "Cancelado")
            {
                return BadRequest("No se puede pagar una orden cancelada");
            }

            if (dto.Monto < orden.Total)
            {
                return BadRequest($"El monto pagado no puede ser menor al total de la orden. Total: {orden.Total}");
            }

            var pago = new Pago
            {
                OrdenId = orden.Id,
                MetodoPago = dto.MetodoPago,
                Monto = dto.Monto,
                Referencia = dto.Referencia,
                FechaPago = DateTime.UtcNow,
                EstadoPago = "Confirmado"
            };

            _context.Pagos.Add(pago);

            orden.Estado = "Pagado";

            if (orden.Mesa != null)
            {
                orden.Mesa.Estado = "Disponible";
            }

            await _context.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("EstadoOrdenActualizado", new
            {
                id = orden.Id,
                estado = orden.Estado
            });

            var comprobanteUrl = $"{Request.Scheme}://{Request.Host}/api/Pagos/orden/{orden.Id}/comprobante";

            return Ok(new
            {
                mensaje = "Pago registrado correctamente",
                pagoId = pago.Id,
                ordenId = orden.Id,
                metodoPago = pago.MetodoPago,
                monto = pago.Monto,
                referencia = pago.Referencia,
                estadoOrden = orden.Estado,
                estadoPago = pago.EstadoPago,
                comprobanteUrl
            });
        }

        [HttpGet("orden/{ordenId}/comprobante")]
        [Authorize(Roles = "Administrador,Caja")]
        public async Task<IActionResult> GenerarComprobantePorOrden(int ordenId)
        {
            var cantidadPagosConfirmados =
                await _context.Pagos
                    .CountAsync(p =>
                        p.OrdenId == ordenId &&
                        p.EstadoPago == "Confirmado"
                    );

                        if (cantidadPagosConfirmados > 1)
                        {
                            return Conflict(
                                "Esta orden tiene cuentas separadas. " +
                                "La generación de comprobantes para pagos múltiples " +
                                "se habilitará en la siguiente fase."
                            );
                        }

            var pago = await _context.Pagos
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Mesa)
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Usuario)
                .Include(p => p.Orden)
                    .ThenInclude(o => o!.Detalles)
                        .ThenInclude(d => d.MenuItem)
                .FirstOrDefaultAsync(p => p.OrdenId == ordenId);

            if (pago == null)
            {
                return NotFound("No existe pago registrado para esta orden");
            }

            if (pago.Orden == null)
            {
                return NotFound("No se encontró la orden asociada al pago");
            }

            var orden = pago.Orden;

            // Ecuador continental UTC-5
            var ecuadorOffset = TimeSpan.FromHours(-5);

            var fechaOrdenEcuador = new DateTimeOffset(
                DateTime.SpecifyKind(orden.Fecha, DateTimeKind.Utc)
            ).ToOffset(ecuadorOffset);

            var fechaPagoEcuador = new DateTimeOffset(
                DateTime.SpecifyKind(pago.FechaPago, DateTimeKind.Utc)
            ).ToOffset(ecuadorOffset);

            var mesero = System.Net.WebUtility.HtmlEncode(
                orden.Usuario != null
                    ? orden.Usuario.Nombre
                    : "No registrado"
            );

            var metodoPago = System.Net.WebUtility.HtmlEncode(
                pago.MetodoPago ?? ""
            );

            var referencia = System.Net.WebUtility.HtmlEncode(
                string.IsNullOrWhiteSpace(pago.Referencia)
                    ? "Sin referencia"
                    : pago.Referencia
            );

            var estadoPago = System.Net.WebUtility.HtmlEncode(
                pago.EstadoPago ?? ""
            );

            var html = new StringBuilder();

            html.Append($@"
<!doctype html>
<html lang='es'>

<head>

    <meta charset='utf-8'>

    <meta
        name='viewport'
        content='width=device-width, initial-scale=1'
    >

    <title>Pedido #{orden.Id:D3}</title>

    <style>

        /*
         * ==========================================================
         * PAPEL
         * ==========================================================
         *
         * MISMA ESTRATEGIA DEL EJEMPLO.
         *
         * Ancho: 80 mm
         * Alto: automático
         * Márgenes: prácticamente nulos
         */

        @page {{
            size: 80mm auto;
            margin: 1mm 1mm;
        }}


        * {{
            color: #000000 !important;
            border-color: #000000 !important;
            box-sizing: border-box;
        }}


        html {{
            margin: 0;
            padding: 0;
            background: #ffffff;
        }}


        body {{
            font-family:
                Arial,
                ""Helvetica Neue"",
                sans-serif;

            color: #000000;

            margin: 0;
            padding: 0;

            width: 100%;

            font-size: 34px;
            line-height: 1.12;

            background: #ffffff;

            font-weight: 900 !important;
        }}


        /*
         * ==========================================================
         * ENCABEZADO
         *
         * SOLO APARECE UNA VEZ AL INICIO.
         * ==========================================================
         */

        .header {{
            width: 100%;

            text-align: center;

            border-bottom:
                5px
                solid
                #000000;

            padding-bottom: 10px;

            margin-bottom: 12px;
        }}


        .header h1 {{
            font-size: 34px;

            margin:
                0
                0
                6px
                0;

            color: #000000;

            text-transform: uppercase;

            font-weight: 900 !important;

            letter-spacing: -0.5px;
        }}


        .header h2 {{
            font-size: 34px;

            margin: 0;

            color: #000000;

            font-weight: 900 !important;
        }}


        /*
         * ==========================================================
         * DATOS GENERALES
         * ==========================================================
         */

        .info-grid {{
            width: 100%;

            margin-bottom: 12px;

            border-bottom:
                5px
                solid
                #000000;

            padding-bottom: 10px;
        }}


        .info-cell {{
            width: 100%;

            font-size: 34px;

            color: #000000;

            margin-bottom: 8px;

            font-weight: 900 !important;

            overflow-wrap: anywhere;
        }}


        /*
         * ==========================================================
         * TABLA DE PRODUCTOS
         *
         * LOS PRODUCTOS VAN UNO DEBAJO DEL OTRO.
         * ==========================================================
         */

        table.items-table {{
            width: 100%;

            border-collapse: collapse;

            margin-top: 6px;

            margin-bottom: 12px;
        }}


        table.items-table th {{
            border-bottom:
                5px
                solid
                #000000;

            font-size: 34px;

            text-align: left;

            padding:
                10px
                0;

            font-weight: 900 !important;
        }}


        table.items-table td {{
            padding:
                10px
                0;

            border-bottom:
                3px
                dashed
                #000000;

            font-size: 34px;

            color: #000000;

            vertical-align: top;

            font-weight: 900 !important;
        }}


        .producto {{
            font-size: 34px;

            font-weight: 900 !important;

            line-height: 1.08;

            overflow-wrap: anywhere;
        }}


        .precio {{
            width: 180px;

            text-align: right;

            white-space: nowrap;

            font-size: 34px;

            font-weight: 900 !important;
        }}


        /*
         * ==========================================================
         * NOTA POR PRODUCTO
         * ==========================================================
         */

        .nota-producto {{
            display: block;

            margin-top: 5px;

            font-size: 27px;

            line-height: 1.1;

            font-weight: 900 !important;

            font-style: italic;
        }}


        /*
         * ==========================================================
         * TOTAL
         * ==========================================================
         */

        .total-box {{
            width: 100%;

            text-align: right;

            font-size: 34px;

            font-weight: 900 !important;

            color: #000000;

            padding:
                14px
                0;

            border-top:
                5px
                solid
                #000000;

            border-bottom:
                5px
                solid
                #000000;

            margin-bottom: 16px;
        }}


        /*
         * ==========================================================
         * INFORMACIÓN DEL PAGO
         * ==========================================================
         */

        .payment-box {{
            width: 100%;

            border:
                5px
                solid
                #000000;

            background: #ffffff;

            padding: 12px;

            margin-bottom: 16px;

            color: #000000;

            font-weight: 900 !important;
        }}


        .payment-title {{
            font-size: 30px;

            font-weight: 900 !important;

            text-transform: uppercase;

            margin-bottom: 8px;
        }}


        .payment-row {{
            width: 100%;

            font-size: 30px;

            line-height: 1.12;

            margin-bottom: 7px;

            font-weight: 900 !important;

            overflow-wrap: anywhere;
        }}


        /*
         * ==========================================================
         * PIE
         * ==========================================================
         */

        .foot {{
            width: 100%;

            text-align: center;

            font-size: 28px;

            color: #000000;

            margin-top: 12px;

            border-top:
                4px
                dashed
                #000000;

            padding-top: 10px;

            font-weight: 900 !important;
        }}


        /*
         * ==========================================================
         * BOTÓN
         *
         * VISIBLE EN PANTALLA.
         * NO SE IMPRIME.
         * ==========================================================
         */

        .acciones {{
            width: 100%;

            margin-top: 20px;

            text-align: center;
        }}


        .acciones button {{
            width: 100%;

            border: none;

            padding: 15px;

            background: #000000;

            color: #ffffff !important;

            font-size: 24px;

            font-weight: 900;

            cursor: pointer;
        }}


        /*
         * ==========================================================
         * IMPRESIÓN
         * ==========================================================
         */

        @media print {{

            html,
            body {{
                margin: 0 !important;
                padding: 0 !important;

                width: 100% !important;

                background:
                    #ffffff !important;
            }}


            .acciones {{
                display: none !important;
            }}

        }}

    </style>

</head>


<body>


    <!-- ======================================================
         ENCABEZADO
         SOLO UNA VEZ
         ====================================================== -->

    <div class='header'>

        <h1>
            LA SUPER CORVINA
        </h1>

        <h2>
            COMPROBANTE DE PAGO #{orden.Id:D3}
        </h2>

    </div>


    <!-- ======================================================
         INFORMACIÓN GENERAL
         ====================================================== -->

    <div class='info-grid'>

        <div class='info-cell'>

            <strong>Mesa:</strong>

            {(orden.Mesa != null
                        ? orden.Mesa.Numero
                        : 0)}

        </div>


        <div class='info-cell'>

            <strong>Mesero:</strong>

            {mesero}

        </div>


        <div class='info-cell'>

            <strong>Fecha:</strong>

            {fechaOrdenEcuador:dd/MM/yyyy HH:mm}

        </div>

    </div>


    <!-- ======================================================
         PRODUCTOS
         ====================================================== -->

    <table class='items-table'>

        <thead>

            <tr>

                <th>
                    Detalle del Producto
                </th>

                <th
                    style='
                        text-align:right;
                        width:180px;
                    '
                >
                    Precio Total
                </th>

            </tr>

        </thead>

        <tbody>
");

            foreach (var detalle in orden.Detalles)
            {
                var nombreProducto =
                    System.Net.WebUtility.HtmlEncode(
                        detalle.MenuItem != null
                            ? detalle.MenuItem.Nombre
                            : "Producto"
                    );

                var nota =
                    System.Net.WebUtility.HtmlEncode(
                        detalle.DetallePersonalizado ?? ""
                    );

                html.Append($@"

            <tr>

                <td class='producto'>

                    <strong>
                        {detalle.Cantidad}×
                    </strong>

                    {nombreProducto}

                    {(string.IsNullOrWhiteSpace(nota)
                                ? ""
                                : $@"
                            <span class='nota-producto'>
                                {nota}
                            </span>
                        ")}

                </td>


                <td class='precio'>

                    ${detalle.Subtotal:F2}

                </td>

            </tr>
");
            }

            html.Append($@"

        </tbody>

    </table>


    <!-- ======================================================
         TOTAL
         ====================================================== -->

    <div class='total-box'>

        TOTAL DEL PEDIDO:
        ${pago.Monto:F2}

    </div>


    <!-- ======================================================
         INFORMACIÓN DEL PAGO
         ====================================================== -->

    <div class='payment-box'>

        <div class='payment-title'>
            INFORMACIÓN DEL PAGO
        </div>


        <div class='payment-row'>

            Forma de pago:
            {metodoPago}

        </div>


        <div class='payment-row'>

            Referencia:
            {referencia}

        </div>


        <div class='payment-row'>

            Fecha de pago:
            {fechaPagoEcuador:dd/MM/yyyy HH:mm}

        </div>


        <div class='payment-row'>

            Estado:
            {estadoPago}

        </div>

    </div>


    <!-- ======================================================
         PIE
         ====================================================== -->

    <div class='foot'>

        ¡Gracias por su preferencia!
        ·
        La Super Corvina

    </div>


    <!-- ======================================================
         BOTÓN MANUAL
         ====================================================== -->

    <div class='acciones'>

        <button
            type='button'
            onclick='window.print()'
        >
            Imprimir comprobante
        </button>

    </div>


    <!-- ======================================================
         MISMO COMPORTAMIENTO DEL EJEMPLO:
         ABRIR IMPRESIÓN AUTOMÁTICAMENTE
         ====================================================== -->

    <script>

        window.onload = function() {{

            setTimeout(
                function() {{

                    window.print();

                }},
                300
            );

        }};

    </script>


</body>

</html>
");

            return Content(
                html.ToString(),
                "text/html; charset=utf-8"
            );
        }
    }
}