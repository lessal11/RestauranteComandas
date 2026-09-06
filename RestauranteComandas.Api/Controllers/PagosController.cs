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
                .Include(o => o.Pago)
                .FirstOrDefaultAsync(o => o.Id == dto.OrdenId);

            if (orden == null)
            {
                return NotFound("La orden seleccionada no existe");
            }

            if (orden.Pago != null)
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
<!DOCTYPE html>

<html lang='es'>

<head>

    <meta charset='UTF-8'>

    <meta
        name='viewport'
        content='width=device-width, initial-scale=1.0'
    >

    <title>Pedido #{orden.Id:D3}</title>

    <style>

        * {{
            box-sizing: border-box;
        }}

        /*
         * El alto de 300mm es únicamente inicial.
         * Antes de imprimir, JavaScript lo reemplaza
         * por la altura REAL del ticket.
         */
        @page {{
            size: 58mm 300mm;
            margin: 0;
        }}

        html {{
            width: 58mm;
            margin: 0;
            padding: 0;
            background: #fff;
        }}

        body {{
            width: 58mm;
            margin: 0;
            padding: 0;

            background: #fff;
            color: #000;

            font-family:
                Arial,
                Helvetica,
                sans-serif;

            font-size: 14px;

            line-height: 1.15;
        }}


        /* ==========================================
           TICKET

           Casi TODO el ancho del papel.
           ========================================== */

        .ticket {{
            width: 58mm;

            margin: 0;

            /*
             * Margen interno mínimo.
             * 0.7 mm a cada lado.
             */
            padding:
                1mm
                0.7mm
                1mm
                0.7mm;

            background: white;
        }}


        /* ==========================================
           ENCABEZADO
           SOLO APARECE UNA VEZ
           ========================================== */

        .encabezado {{
            width: 100%;

            text-align: center;

            padding: 0;
            margin: 0;
        }}

        .restaurante {{
            margin: 0;

            font-size: 20px;
            line-height: 1;

            font-weight: 900;
        }}

        .tipo-documento {{
            margin-top: 5px;

            font-size: 14px;
            line-height: 1;

            font-weight: 900;
        }}

        .pedido {{
            margin-top: 4px;

            font-size: 19px;
            line-height: 1;

            font-weight: 900;
        }}


        /* ==========================================
           SEPARADORES
           ========================================== */

        .linea-fuerte {{
            width: 100%;

            border-top:
                2.5px
                solid
                #000;

            margin:
                7px
                0;
        }}

        .linea-producto {{
            width: 100%;

            border-top:
                1px
                dashed
                #000;

            margin:
                2px
                0;
        }}


        /* ==========================================
           DATOS DEL PEDIDO
           ========================================== */

        .datos {{
            width: 100%;

            font-size: 14px;
            line-height: 1.15;
        }}

        .fila-dato {{
            width: 100%;

            margin:
                4px
                0;
        }}

        .fila-dato strong {{
            font-weight: 900;
        }}


        /* ==========================================
           ENCABEZADO PRODUCTOS
           ========================================== */

        .cabecera-productos {{
            display: grid;

            grid-template-columns:
                minmax(0, 1fr)
                16mm;

            gap: 2px;

            width: 100%;

            padding:
                3px
                0
                5px
                0;

            font-size: 14px;
            line-height: 1;

            font-weight: 900;

            border-bottom:
                2.5px
                solid
                #000;
        }}

        .cabecera-precio {{
            text-align: right;
        }}


        /* ==========================================
           PRODUCTOS

           IMPORTANTE:
           NO page-break
           NO break-inside
           NO saltos de hoja
           ========================================== */

        .producto {{
            width: 100%;

            padding:
                5px
                0
                4px
                0;

            border-bottom:
                1px
                dashed
                #000;
        }}

        .producto-principal {{
            display: grid;

            grid-template-columns:
                minmax(0, 1fr)
                16mm;

            gap: 2px;

            width: 100%;

            align-items: start;
        }}

        .producto-nombre {{
            min-width: 0;

            font-size: 16px;
            line-height: 1.08;

            font-weight: 900;

            overflow-wrap: anywhere;
            word-break: break-word;
        }}

        .producto-total {{
            font-size: 16px;
            line-height: 1.08;

            font-weight: 900;

            text-align: right;

            white-space: nowrap;
        }}

        .producto-nota {{
            width: 100%;

            margin-top: 3px;

            font-size: 12px;
            line-height: 1.1;

            font-weight: 700;
            font-style: italic;

            overflow-wrap: anywhere;
            word-break: break-word;
        }}


        /* ==========================================
           TOTAL
           ========================================== */

        .total {{
            display: flex;

            width: 100%;

            justify-content:
                space-between;

            align-items:
                center;

            gap: 4px;

            padding:
                7px
                0;

            font-size: 19px;
            line-height: 1;

            font-weight: 900;
        }}


        /* ==========================================
           DATOS DEL PAGO
           ========================================== */

        .datos-pago {{
            width: 100%;

            font-size: 13px;
            line-height: 1.15;
        }}

        .fila-pago {{
            display: flex;

            width: 100%;

            justify-content:
                space-between;

            align-items:
                flex-start;

            gap: 4px;

            margin:
                3px
                0;
        }}

        .etiqueta {{
            flex-shrink: 0;

            font-weight: 900;
        }}

        .valor {{
            flex: 1;

            text-align: right;

            font-weight: 600;

            overflow-wrap: anywhere;
        }}


        /* ==========================================
           PIE
           ========================================== */

        .pie {{
            width: 100%;

            margin-top: 6px;

            padding:
                6px
                0
                2px
                0;

            border-top:
                1px
                dashed
                #000;

            text-align: center;

            font-size: 12px;
            line-height: 1.15;

            font-weight: 700;
        }}


        /* ==========================================
           BOTÓN
           NO FORMA PARTE DEL TICKET
           ========================================== */

        .botones {{
            width: 58mm;

            margin-top: 10px;

            padding:
                0
                1mm;

            text-align: center;
        }}

        button {{
            width: 100%;

            padding: 10px;

            border: 0;

            background: #111;
            color: white;

            font-size: 13px;
            font-weight: bold;

            cursor: pointer;
        }}


        /* ==========================================
           PANTALLA
           ========================================== */

        @media screen {{

            body {{
                background: #e5e7eb;
            }}

            .ticket {{
                background: #fff;
            }}

        }}


        /* ==========================================
           IMPRESIÓN
           ========================================== */

        @media print {{

            html,
            body {{
                width: 58mm !important;

                margin: 0 !important;
                padding: 0 !important;

                background: white !important;
            }}

            .ticket {{
                width: 58mm !important;

                margin: 0 !important;

                padding:
                    1mm
                    0.7mm
                    1mm
                    0.7mm !important;
            }}

            .botones {{
                display: none !important;
            }}

        }}

    </style>

</head>


<body>


<div
    class='ticket'
    id='ticket'
>

    <!-- ENCABEZADO: SOLO UNA VEZ -->

    <div class='encabezado'>

        <div class='restaurante'>
            LA SUPER CORVINA
        </div>

        <div class='tipo-documento'>
            COMPROBANTE DE PAGO
        </div>

        <div class='pedido'>
            PEDIDO #{orden.Id:D3}
        </div>

    </div>


    <div class='linea-fuerte'></div>


    <!-- DATOS -->

    <div class='datos'>

        <div class='fila-dato'>
            <strong>Mesa:</strong>
            {(orden.Mesa != null ? orden.Mesa.Numero : 0)}
        </div>

        <div class='fila-dato'>
            <strong>Mesero:</strong>
            {mesero}
        </div>

        <div class='fila-dato'>
            <strong>Fecha:</strong>
            {fechaOrdenEcuador:dd/MM/yyyy HH:mm}
        </div>

    </div>


    <div class='linea-fuerte'></div>


    <!-- PRODUCTOS -->

    <div class='cabecera-productos'>

        <div>
            Detalle del Producto
        </div>

        <div class='cabecera-precio'>
            Precio<br>Total
        </div>

    </div>
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

    <div class='producto'>

        <div class='producto-principal'>

            <div class='producto-nombre'>

                {detalle.Cantidad}× {nombreProducto}

            </div>

            <div class='producto-total'>

                ${detalle.Subtotal:F2}

            </div>

        </div>


        {(string.IsNullOrWhiteSpace(nota)
                    ? ""
                    : $@"
                <div class='producto-nota'>
                    {nota}
                </div>
            ")}

    </div>
");
            }


            html.Append($@"

    <!-- TOTAL INMEDIATAMENTE DESPUÉS DEL ÚLTIMO PLATO -->

    <div class='linea-fuerte'></div>


    <div class='total'>

        <span>
            TOTAL:
        </span>

        <span>
            ${pago.Monto:F2}
        </span>

    </div>


    <div class='linea-fuerte'></div>


    <!-- DATOS DEL PAGO -->

    <div class='datos-pago'>


        <div class='fila-pago'>

            <span class='etiqueta'>
                Forma de pago:
            </span>

            <span class='valor'>
                {metodoPago}
            </span>

        </div>


        <div class='fila-pago'>

            <span class='etiqueta'>
                Referencia:
            </span>

            <span class='valor'>
                {referencia}
            </span>

        </div>


        <div class='fila-pago'>

            <span class='etiqueta'>
                Fecha pago:
            </span>

            <span class='valor'>
                {fechaPagoEcuador:dd/MM/yyyy HH:mm}
            </span>

        </div>


        <div class='fila-pago'>

            <span class='etiqueta'>
                Estado:
            </span>

            <span class='valor'>
                {estadoPago}
            </span>

        </div>

    </div>


    <div class='pie'>

        ¡Gracias por su preferencia!
        ·
        LA SUPER CORVINA

    </div>


</div>


<!-- BOTÓN FUERA DEL PAPEL -->

<div class='botones'>

    <button
        type='button'
        onclick='imprimirTicket()'
    >
        Imprimir comprobante
    </button>

</div>


<script>

    (function () {{

        function configurarAlturaTicket() {{

            const ticket =
                document.getElementById('ticket');

            if (!ticket) {{
                return;
            }}

            const altoPx =
                Math.ceil(
                    ticket.getBoundingClientRect().height
                );

            const altoMm =
                (altoPx * 25.4 / 96) + 0.5;

            let style =
                document.getElementById(
                    'ticket-page-size'
                );

            if (!style) {{

                style =
                    document.createElement('style');

                style.id =
                    'ticket-page-size';

                document.head.appendChild(style);
            }}

            style.textContent = `
                @page {{
                    size: 58mm ${{altoMm.toFixed(2)}}mm;
                    margin: 0;
                }}
            `;
        }}


        window.imprimirTicket = function () {{

            configurarAlturaTicket();

            requestAnimationFrame(function () {{

                setTimeout(function () {{

                    window.print();

                }}, 150);

            }});

        }};


        window.addEventListener(
            'load',
            function () {{

                setTimeout(
                    configurarAlturaTicket,
                    100
                );

            }}
        );


        window.addEventListener(
            'beforeprint',
            function () {{

                configurarAlturaTicket();

            }}
        );


        window.addEventListener(
            'resize',
            function () {{

                configurarAlturaTicket();

            }}
        );


        if (document.fonts) {{

            document.fonts.ready.then(
                function () {{

                    configurarAlturaTicket();

                }}
            );

        }}

    }})();

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