using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient; // Asegúrate de tener instalado el paquete System.Data.SqlClient o Microsoft.Data.SqlClient
using System.Collections.Generic;

namespace apiAsistenciaColegio.Controllers
{
    [Route("api/dw")]
    [ApiController]
    public class DataWarehouseController : ControllerBase
    {
        // Cadena de conexión a tu Data Warehouse
        private readonly string cadenaConexion = @"Server=GERBER-CANAHUI; Database=DW_Colegio; Integrated Security=True; TrustServerCertificate=True;";

        [HttpGet("alumnos")]
        public IActionResult ObtenerAlumnosDW(string grado = "TODOS", string seccion = "TODAS")
        {
            var listaAlumnos = new List<object>();

            using (SqlConnection con = new SqlConnection(cadenaConexion))
            {
                con.Open();

                // Consulta base
                string query = "SELECT AlumnoKey, Nombre_Completo, Grado, Seccion, Fuente_Origen FROM DW_Dim_Alumnos WHERE 1=1";

                // Filtros dinámicos según lo que el usuario elija en la pantalla
                if (grado != "TODOS") query += " AND Grado = @grado";
                if (seccion != "TODAS") query += " AND Seccion = @seccion";

                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    if (grado != "TODOS") cmd.Parameters.AddWithValue("@grado", grado);
                    if (seccion != "TODAS") cmd.Parameters.AddWithValue("@seccion", seccion);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            listaAlumnos.Add(new
                            {
                                id = reader["AlumnoKey"],
                                nombre = reader["Nombre_Completo"].ToString(),
                                grado = reader["Grado"].ToString(),
                                seccion = reader["Seccion"].ToString(),
                                horario = "Matutina", // Valor estático si no lo tienes en el DW
                                estado = "Activo",    // Valor estático si no lo tienes en el DW
                                origen = reader["Fuente_Origen"].ToString()
                            });
                        }
                    }
                }
            }
            return Ok(listaAlumnos);
        }
    }
}