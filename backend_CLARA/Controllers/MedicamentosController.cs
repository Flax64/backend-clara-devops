using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MedicamentosController : ControllerBase
    {
        // 🔥 LA MAGIA: Esta lista "static" vive en la memoria RAM de tu compu.
        // Va a guardar los cambios (agregar, editar, borrar) mientras el servidor siga encendido.
        private static List<Medicamento> _baseDeDatosFalsa = new List<Medicamento>
        {
            new Medicamento { Id = 1, Nombre = "Paracetamol", Descripcion = "Caja con 10 tabletas 500mg", Precio = 25.50m, Stock = 50 },
            // Le puse 8 de stock a este para probar tu código visual que pinta de rojo los números menores a 10 😎
            new Medicamento { Id = 2, Nombre = "Ibuprofeno", Descripcion = "Caja con 20 cápsulas 400mg", Precio = 45.00m, Stock = 8 },
            new Medicamento { Id = 3, Nombre = "Amoxicilina", Descripcion = "Suspensión 500mg/5ml", Precio = 120.00m, Stock = 15 }
        };

        // 1. LEER (El que usa tu tabla ahorita)
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(_baseDeDatosFalsa);
        }

        // 2. AGREGAR (Para cuando hagamos tu pantalla de Agregar Medicamento)
        [HttpPost]
        public IActionResult Post([FromBody] Medicamento nuevoMedicamento)
        {
            // Le inventamos un ID automático (el número más alto que exista + 1)
            int nuevoId = _baseDeDatosFalsa.Any() ? _baseDeDatosFalsa.Max(m => m.Id) + 1 : 1;
            nuevoMedicamento.Id = nuevoId;

            _baseDeDatosFalsa.Add(nuevoMedicamento); // Lo guardamos en la RAM
            return Ok(nuevoMedicamento);
        }

        // 3. EDITAR
        [HttpPut("{id}")]
        public IActionResult Put(int id, [FromBody] Medicamento medicamentoActualizado)
        {
            var existente = _baseDeDatosFalsa.FirstOrDefault(m => m.Id == id);
            if (existente == null) return NotFound("Medicamento no encontrado");

            // Actualizamos los datos
            existente.Nombre = medicamentoActualizado.Nombre;
            existente.Descripcion = medicamentoActualizado.Descripcion;
            existente.Precio = medicamentoActualizado.Precio;
            existente.Stock = medicamentoActualizado.Stock;

            return Ok(existente);
        }

        // 4. BORRAR
        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var existente = _baseDeDatosFalsa.FirstOrDefault(m => m.Id == id);
            if (existente == null) return NotFound("Medicamento no encontrado");

            _baseDeDatosFalsa.Remove(existente); // Lo borramos de la RAM
            return Ok("Eliminado correctamente");
        }
    }
}



