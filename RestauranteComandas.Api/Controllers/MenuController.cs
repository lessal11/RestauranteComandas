using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestauranteComandas.Api.Data;
using RestauranteComandas.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace RestauranteComandas.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MenuController : ControllerBase
    {
        private readonly RestauranteDbContext _context;

        public MenuController(RestauranteDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [Authorize(Roles = "Administrador,Mesero,Cocina,Caja")]
        public async Task<ActionResult<IEnumerable<MenuItem>>> GetMenu()
        {
            return await _context.MenuItems
                .OrderBy(m => m.Categoria)
                .ThenBy(m => m.Nombre)
                .ToListAsync();
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Administrador,Mesero,Cocina,Caja")]
        public async Task<ActionResult<MenuItem>> GetMenuItem(int id)
        {
            var item = await _context.MenuItems.FindAsync(id);

            if (item == null)
            {
                return NotFound("Plato no encontrado");
            }

            return item;
        }

        [HttpGet("categoria/{categoria}")]
        [Authorize(Roles = "Administrador,Mesero,Cocina,Caja")]
        public async Task<ActionResult<IEnumerable<MenuItem>>> GetPorCategoria(string categoria)
        {
            var items = await _context.MenuItems
                .Where(m => m.Categoria.ToLower() == categoria.ToLower() && m.Disponible)
                .OrderBy(m => m.Nombre)
                .ToListAsync();

            return items;
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        public async Task<ActionResult<MenuItem>> CrearMenuItem(MenuItem menuItem)
        {
            if (string.IsNullOrWhiteSpace(menuItem.Nombre))
            {
                return BadRequest("El nombre del plato es obligatorio");
            }

            if (string.IsNullOrWhiteSpace(menuItem.Categoria))
            {
                return BadRequest("La categoría es obligatoria");
            }

            if (menuItem.Precio <= 0)
            {
                return BadRequest("El precio debe ser mayor a 0");
            }

            menuItem.Disponible = true;

            _context.MenuItems.Add(menuItem);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetMenuItem), new { id = menuItem.Id }, menuItem);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> ActualizarMenuItem(int id, MenuItem menuItem)
        {
            if (id != menuItem.Id)
            {
                return BadRequest("El ID del plato no coincide");
            }

            var itemExistente = await _context.MenuItems.FindAsync(id);

            if (itemExistente == null)
            {
                return NotFound("Plato no encontrado");
            }

            itemExistente.Nombre = menuItem.Nombre;
            itemExistente.Categoria = menuItem.Categoria;
            itemExistente.Precio = menuItem.Precio;
            itemExistente.Disponible = menuItem.Disponible;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPatch("{id}/disponibilidad")]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> CambiarDisponibilidad(int id, [FromBody] bool disponible)
        {
            var item = await _context.MenuItems.FindAsync(id);

            if (item == null)
            {
                return NotFound("Plato no encontrado");
            }

            item.Disponible = disponible;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> EliminarMenuItem(int id)
        {
            var item = await _context.MenuItems.FindAsync(id);

            if (item == null)
            {
                return NotFound("Plato no encontrado");
            }

            _context.MenuItems.Remove(item);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}