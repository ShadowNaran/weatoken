# weatoken

API ASP.NET Core con autenticación OAuth de GitHub, operaciones CRUD de personas y documentación Scalar.

## Configuración local

1. Copia `appsettings.Development.example.json` como `appsettings.Development.json`.
2. Completa la conexión a PostgreSQL y las credenciales de tu propia aplicación OAuth de GitHub.
3. Configura en GitHub este callback: `http://localhost:5077/auth/callback`.
4. Ejecuta `dotnet run` y abre `http://localhost:5077/scalar/v1`.

No publiques credenciales reales. `appsettings.json` y los artefactos generados están excluidos por `.gitignore`.
