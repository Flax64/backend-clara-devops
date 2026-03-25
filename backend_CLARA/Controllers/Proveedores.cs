using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;

[Route("api/[controller]")]
[ApiController]
public class ProveedoresController : ControllerBase
{
    private readonly string _connectionString = "Server=localhost; Database=farmacia; Uid=root; Pwd=KameHameH4!";

    [HttpGet("lista")]
    public IActionResult Get()
    {
        var lista = new List<object>();
        using (MySqlConnection conn = new MySqlConnection(_connectionString))
        {
            conn.Open();
            string sql = "SELECT id_Proveedor, nombre_Proveedor FROM PROVEEDORES WHERE id_Estatus = 1";
            using (var cmd = new MySqlCommand(sql, conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read()) lista.Add(new { id_Proveedor = r[0], nombre_Proveedor = r[1] });
            }
        }
        return Ok(lista);
    }
}