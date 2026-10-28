# Routify

Routify es una aplicación web de gestión operativa para control de pedidos, asignación de rutas, seguimiento de entregas y monitoreo del desempeño del equipo de reparto.

El proyecto está desarrollado en ASP.NET Core y está orientado a negocios con logística de última milla, pedidos en línea y coordinación de repartidores en tiempo real.

## Características principales

- Dashboard de despacho con KPIs diarios.
- Gestión de pedidos y estados de entrega.
- Asignación de rutas y reparto por operador.
- Autenticación de usuarios con sesiones persistentes.
- Seguimiento en tiempo real con SignalR.
- Integración con geocodificación para direcciones y mapas.
- Notificaciones por correo y WhatsApp.
- Soporte para SQL Server con tipos geográficos de NetTopologySuite.

## Stack tecnológico

- ASP.NET Core 10
- MVC + Razor Views
- Entity Framework Core
- SQL Server
- SignalR
- JavaScript / HTML / CSS
- OpenStreetMap Nominatim para geocodificación

## Estructura del proyecto

- `Controllers/` — controladores HTTP
- `Models/` — entidades y modelos de dominio
- `Views/` — vistas Razor
- `Data/` — contexto de base de datos y acceso a datos
- `Services/` — lógica de negocio y servicios transversales
- `Hubs/` — SignalR hubs para tiempo real
- `ViewModels/` — modelos para las vistas
- `Helpers/` — utilidades internas
- `wwwroot/` — archivos estáticos del frontend
- `Program.cs` — configuración y pipeline de la aplicación
- `appsettings.json` — configuración principal de la aplicación

## Requisitos

- .NET SDK 10.0
- SQL Server
- Visual Studio 2022 o VS Code con C#

## Configuración

1. Clona el repositorio.
2. Ajusta la cadena de conexión en `appsettings.json` o en un archivo de configuración local.
3. Verifica que la base de datos exista y que el usuario tenga permisos para conectarse.
4. Configura los datos del negocio y de notificaciones si se van a usar correo o WhatsApp.

Ejemplo de conexión:

```json
"ConnectionStrings": {
  "RoutifyDB": "Server=TU_SERVIDOR;Database=RoutifyDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
}
```

## Ejecutar la aplicación

Desde la raíz del proyecto:

```bash
dotnet restore
dotnet build
dotnet run
```

La aplicación estará disponible normalmente en:

- https://localhost:7080
- http://localhost:5080

## Variables y configuración relevantes

En `appsettings.json` se encuentran opciones para:

- conexión a base de datos
- URL base de la aplicación
- nombre del comercio
- tiempo de inactividad
- rutas de archivos
- geocodificación
- configuración de correo
- configuración de WhatsApp

## Notas importantes

- La aplicación usa cookies para autenticación.
- El proyecto está preparado para trabajar con geografía de SQL Server (`NetTopologySuite`).
- La configuración actual incluye un punto de conexión externo para SQL Server y valores por defecto para mensajes y geocodificación.

## Estado del proyecto

Este repositorio parece ser un proyecto funcional de gestión logística con enfoque web y panel operativo. La estructura indica que está en fase de desarrollo o implementación de negocio con varias áreas internas ya definidas.

## Licencia

No se especifica una licencia en el repositorio. Si necesitas usarlo en producción, revisa primero la política del autor antes de distribuirlo o reutilizarlo.

## Contacto / Autor

Repositorio: `antonyaponte25/Routify`
