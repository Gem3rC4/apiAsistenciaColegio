using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient; 
using System.Collections.Generic;

namespace apiAsistenciaColegio.Controllers
{
    [Route("api/dw")]
    [ApiController]
    public class DataWarehouseController : ControllerBase
    {
        private readonly string cadenaConexion = @"Server=AUGUSTOPC\SQLEXPRESS; Database=DW_Colegio; Integrated Security=True; TrustServerCertificate=True;";

        [HttpGet("consulta")]
        public IActionResult ConsultarTabla(string tabla, string seccion)
        {
            var tablasPermitidas = new List<string> {
        "ex_PrimeroBasico", "ex_SegundoBasico", "ex_TerceroBasico",
        "ex_CuartoBachillerato", "ex_QuintoBachillerato"
    };

            if (!tablasPermitidas.Contains(tabla))
                return BadRequest("Tabla no permitida.");

            var lista = new List<object>();

            using (SqlConnection con = new SqlConnection(cadenaConexion))
            {
                string query = $"SELECT ApellidosNombres, Grado, Seccion, Estado FROM {tabla} WHERE 1=1";

                if (seccion != "TODAS") query += " AND Seccion = @seccion";

                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    if (seccion != "TODAS") cmd.Parameters.AddWithValue("@seccion", seccion);

                    con.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new
                            {
                                nombreCompleto = reader["ApellidosNombres"].ToString(),
                                grado = reader["Grado"].ToString(),
                                seccion = reader["Seccion"].ToString(),
                                estado = reader["Estado"].ToString()
                            });
                        }
                    }
                }
            }
            return Ok(lista);
        }
    }
}