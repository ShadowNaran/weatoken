using Apiclase19.Data;
using Apiclase19.Dominio;
using Apiclase19.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Apiclase19.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ServiceFilter(typeof(GitHubSessionAuthorizationFilter))]
    public class PersonaController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PersonaController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Persona>>> Get(
            CancellationToken cancellationToken)
        {
            return Ok(await _context.Personas.AsNoTracking().ToListAsync(cancellationToken));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Persona>> GetById(
            int id,
            CancellationToken cancellationToken)
        {
            var persona = await _context.Personas.AsNoTracking()
                .FirstOrDefaultAsync(persona => persona.Id == id, cancellationToken);
            if (persona is null)
            {
                return NotFound();
            }

            return Ok(persona);
        }

        [HttpPost]
        public async Task<ActionResult<Persona>> Create(
            Persona persona,
            CancellationToken cancellationToken)
        {
            _context.Personas.Add(persona);
            await _context.SaveChangesAsync(cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = persona.Id }, persona);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(
            int id,
            Persona persona,
            CancellationToken cancellationToken)
        {
            if (id != persona.Id)
            {
                return BadRequest();
            }

            var personaExistente = await _context.Personas.FindAsync(
                [id],
                cancellationToken);
            if (personaExistente is null)
            {
                return NotFound();
            }

            personaExistente.Nombre = persona.Nombre;
            personaExistente.Apellido = persona.Apellido;
            personaExistente.Edad = persona.Edad;
            await _context.SaveChangesAsync(cancellationToken);

            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            var persona = await _context.Personas.FindAsync(
                [id],
                cancellationToken);
            if (persona is null)
            {
                return NotFound();
            }

            _context.Personas.Remove(persona);
            await _context.SaveChangesAsync(cancellationToken);

            return NoContent();
        }
    }
}
