using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace apiAsistenciaColegio.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AlumnosController : ControllerBase
    {
        private readonly string _connectionString;

        public AlumnosController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("ConexionColegio");
        }

        // GET: /api/alumnos -> Llama a SP existente 'sp_ObtenerEstudiantesParaQR'
        [HttpGet]
        public IActionResult ObtenerAlumnos()
        {
            try
            {
                var listaAlumnos = new List<AlumnoDto>();

                using (var conn = new SqlConnection(_connectionString))
                {
                    using (var cmd = new SqlCommand("sp_ObtenerEstudiantesParaQR", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        conn.Open();

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string nombres = reader["Nombres"].ToString().Trim();
                                string apellidos = reader["Apellidos"].ToString().Trim();

                                listaAlumnos.Add(new AlumnoDto
                                {
                                    grado = reader["Grado"].ToString().Trim(),
                                    seccion = reader["Seccion"].ToString().Trim(),
                                    estudiante = $"{nombres} {apellidos}",
                                    estadoMatricula = "Activo"
                                });
                            }
                        }
                    }
                }

                return Ok(listaAlumnos);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { mensaje = $"Error al ejecutar el SP en la Base de Datos: {ex.Message}" });
            }
        }

        public class AlumnoDto
        {
            public string grado { get; set; }
            public string seccion { get; set; }
            public string estudiante { get; set; }
            public string estadoMatricula { get; set; }
        }
    }
}