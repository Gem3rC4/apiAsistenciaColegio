## **Documentación Técnica: Sistema de Asistencia por Código QR**
### Institución: Colegio Católico Eco-tecnológico Sagrado Corazón de Jesús

### **Estado:** Fase 1 (MVP Local) Completada

### **1. Visión General del Proyecto**
El sistema es una aplicación web de pila completa (Full-Stack) diseñada para automatizar el registro de asistencia de los estudiantes mediante la lectura de códigos QR. Permite registrar entradas y salidas de forma inteligente, evitando registros duplicados accidentales, y ofrece un panel de visualización en tiempo real para los maestros.

### **2. Pila Tecnológica (Tech Stack)**
* **Base de Datos:** Microsoft SQL Server.
* **Backend:** C# .NET Core (Web API RESTful).
* **Frontend:** HTML5, CSS3, JavaScript Vanilla.
* **Librerías de Terceros:** *html5-qrcode* (Manejo de la cámara y lectura óptica).
    * *SweetAlert2* (Interfaz gráfica para ventanas emergentes/modales).
* **Despliegue Local:** Kestrel (Servidor interno de .NET) configurado para red LAN (Wi-Fi)


### **3. Arquitectura de Base de Datos (SQL Server)**
La base de datos centraliza la información de los alumnos y la lógica de negocio para garantizar la integridad de los datos, reduciendo la carga de procesamiento en el servidor web.

#### **Tablas Principales:**

* *tblEstudiantes:* Almacena los datos personales (Id, Nombre, Apellido).

* *tblGrados* y *tblAsignaciones:* Gestionan la relación estructurada de a qué grado (ej. Primero Básico) y sección (A, B) pertenece cada estudiante.

* *tblAsistencia:* Historial de movimientos (Id_Estudiante, Fecha_Hora, Tipo_Movimiento).

#### **Lógica de Negocio (Procedimientos Almacenados):**

1. **usp_RegistrarAsistencia:**

    * Valida la existencia del código QR.

    * Regla de los 5 minutos (Cooldown): Bloquea escaneos repetidos en un lapso menor a 5 minutos para evitar lecturas accidentales.

    * Sistema Cíclico: Lee el último movimiento del día del alumno y registra lo contrario (Si el último fue Entrada, registra Salida; si fue Salida, registra Entrada).

2. **usp_ObtenerReporteAsistenciaHoy:**

    * Utiliza un *LEFT JOIN* y subconsultas para extraer el listado completo de estudiantes del colegio.

    * Filtra la primera entrada (ASC) y la última salida (DESC) del día en curso.

    * Asigna automáticamente los estados: "PRESENTE", "FALTA" o "RETIRADO".



### **4. Desarrollo del Backend (API en C# .NET)**
El controlador *AsistenciaController.cs* actúa como el puente de comunicación mediante ADO.NET (*SqlConnection*, *SqlCommand*).

#### Endpoints (Rutas):

* *POST /api/asistencia/registrar:* Recibe el texto del código QR en formato JSON (EscaneoRequest). Ejecuta el SP de registro y devuelve un código HTTP 200 (Éxito) o HTTP 400 (Advertencia/Error de validación).

* *GET /api/asistencia/hoy:* Consulta el SP de reportes y devuelve un arreglo JSON estructurado mediante la clase ReporteAsistenciaDto, manejando lecturas seguras con *reader.HasRows*.

#### Configuración del Servidor Web (*Program.cs*):

Se habilitaron las instrucciones *app.UseDefaultFiles();* y *app.UseStaticFiles();* para permitir que la API sirva las páginas HTML directamente desde la carpeta wwwroot.

### **5. Desarrollo del Frontend (Interfaz de Usuario)**
Toda la interfaz gráfica reside en la carpeta pública *wwwroot.* Se optó por un diseño *"Eco-tecnológico"* basado en tonos verdes, sombras suaves y un esquema Mobile-First (optimizado para celulares).

#### **Estructura de Archivos:**

1. *index.html* (**Dashboard**): Menú principal (Landing Page) que enruta al usuario hacia el escáner o hacia el reporte a través de botones tipo tarjeta.

2. *escaner.html* (**Módulo de Captura**): Activa la cámara del dispositivo. Al detectar un QR, pausa el escaneo temporalmente, envía la petición POST al backend y lanza una alerta gráfica (SweetAlert2) que obliga al usuario a presionar "Aceptar" antes de leer el siguiente código.

3. *reporte.html* (**Panel de Maestros**): Consume el endpoint GET y renderiza una tabla dinámica. Los estados de asistencia ("PRESENTE", "FALTA") se colorean automáticamente mediante etiquetas (badges) CSS. Los registros se auto-ordenan desde la base de datos por Grado > Sección > Apellido.

### **6. Configuración de Red Local (LAN)**
Para permitir que dispositivos externos (como el celular de un docente) se conecten al servidor de desarrollo (Laptop):

1. **IP y Puertos:** Se configuró Visual Studio (*launchSettings.json*) para escuchar en la dirección IP local de la máquina (*ej. 192.168.0.5*) además de *localhost*.

2. **Firewall:** Se abrió el puerto TCP correspondiente (ej. *5114*) en el Firewall de Windows Defender con reglas de entrada.

3. **Permisos de Hardware:** Se utilizó el flag de desarrollador en Google Chrome móvil (*chrome://flags/#unsafely-treat-insecure-origin-as-secure*) para permitir que la cámara se encienda bajo un entorno HTTP local durante la fase de pruebas.
