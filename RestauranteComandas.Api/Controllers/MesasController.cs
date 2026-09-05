using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestauranteComandas.Api.Data;
using RestauranteComandas.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace RestauranteComandas.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MesasController : ControllerBase
    {
        private readonly RestauranteDbContext _context;

        public MesasController(RestauranteDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [Authorize(Roles = "Administrador,Mesero,Caja")]
        public async Task<ActionResult<IEnumerable<Mesa>>> GetMesas()
        {
            return await _context.Mesas
                .OrderBy(m => m.Numero)
                .ToListAsync();
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Administrador,Mesero,Caja")]
        public async Task<ActionResult<Mesa>> GetMesa(int id)
        {
            var mesa = await _context.Mesas.FindAsync(id);

            if (mesa == null)
            {
                return NotFound("Mesa no encontrada");
            }

            return mesa;
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        public async Task<ActionResult<Mesa>> CrearMesa(Mesa mesa)
        {
            if (mesa.Numero <= 0)
            {
                return BadRequest("El número de mesa debe ser mayor a 0");
            }

            var existeMesa = await _context.Mesas
                .AnyAsync(m => m.Numero == mesa.Numero);

            if (existeMesa)
            {
                return BadRequest("Ya existe una mesa con ese número");
            }

            mesa.Estado = string.IsNullOrWhiteSpace(mesa.Estado)
                ? "Disponible"
                : mesa.Estado;

            _context.Mesas.Add(mesa);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetMesa), new { id = mesa.Id }, mesa);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> ActualizarMesa(int id, Mesa mesa)
        {
            if (id != mesa.Id)
            {
                return BadRequest("El ID de la mesa no coincide");
            }

            var mesaExistente = await _context.Mesas.FindAsync(id);

            if (mesaExistente == null)
            {
                return NotFound("Mesa no encontrada");
            }

            var existeOtraMesaConMismoNumero = await _context.Mesas
                .AnyAsync(m => m.Numero == mesa.Numero && m.Id != id);

            if (existeOtraMesaConMismoNumero)
            {
                return BadRequest("Ya existe otra mesa con ese número");
            }

            mesaExistente.Numero = mesa.Numero;
            mesaExistente.Estado = mesa.Estado;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPatch("{id}/estado")]
        [Authorize(Roles = "Administrador,Mesero,Caja")]
        public async Task<IActionResult> CambiarEstadoMesa(int id, [FromBody] string estado)
        {
            var estadosPermitidos = new List<string>
            {
                "Disponible",
                "Ocupada",
                "En pago",
                "Cerrada"
            };

            if (!estadosPermitidos.Contains(estado))
            {
                return BadRequest("Estado de mesa no permitido");
            }

            var mesa = await _context.Mesas.FindAsync(id);

            if (mesa == null)
            {
                return NotFound("Mesa no encontrada");
            }

            mesa.Estado = estado;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Estado de mesa actualizado correctamente",
                mesa.Id,
                mesa.Numero,
                mesa.Estado
            });
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> EliminarMesa(int id)
        {
            var mesa = await _context.Mesas
                .Include(m => m.Ordenes)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (mesa == null)
            {
                return NotFound("Mesa no encontrada");
            }

            if (mesa.Ordenes.Any())
            {
                return BadRequest("No se puede eliminar la mesa porque tiene órdenes registradas");
            }

            _context.Mesas.Remove(mesa);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}