# ADR 0001 — Monolito ASP.NET Core MVC + EF Core + PostgreSQL en Render

- Estado: aceptado
- Fecha: 2026-11-14

## Contexto

La propuesta técnica define ASP.NET Core MVC, PostgreSQL administrado por Render, EF Core, la Web Speech API y despliegue con Docker. La sección 3.2 de la propuesta menciona "servidor Django", lo que contradice el stack de la sección 3.1.

## Decisión

- Un solo proyecto web ASP.NET Core MVC sobre .NET 10 (LTS, SDK ya instalado en la máquina de desarrollo) con vistas Razor y JavaScript sin framework.
- Se trata la mención a Django como un error de redacción: el stack vigente es el de la sección 3.1.
- Autenticación con cookies y `PasswordHasher<T>` en lugar de ASP.NET Core Identity, porque el modelo de datos solo pide nombre, correo, contraseña cifrada y nivel.
- Nombres de base de datos en `snake_case` aplicados en `OnModelCreating`, sin dependencias adicionales.
- Lista de intentos válidos en archivo de texto cargado en memoria (`FrozenSet`), separada del banco de respuestas en base de datos.

## Alternativas

| Alternativa | Ventaja | Desventaja |
|---|---|---|
| React + API separada | Interfaz más interactiva | Dos despliegues; la propuesta la descarta explícitamente |
| ASP.NET Core Identity | Recuperación de contraseña, lockout, 2FA listos | 7+ tablas y UI que el alcance no requiere |
| Lista válida en PostgreSQL | Editable sin redeploy | ~100k filas y una consulta por intento sin beneficio actual |

## Consecuencias

- Despliegue simple: una imagen Docker + base de datos.
- Corregir la sección 3.2 de la propuesta ("Django" → "ASP.NET Core").
- Si se requiere recuperación de contraseña, reevaluar Identity.
