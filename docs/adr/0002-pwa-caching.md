# ADR 0002 — PWA instalable con caché solo de recursos estáticos

- Estado: aceptado
- Fecha: 2026-11-14

## Contexto

Se pidió que Wordrise se pueda instalar como PWA. El juego depende del servidor: cada intento se valida en el backend y la respuesta nunca se envía al cliente mientras la ronda está en curso. Las páginas autenticadas incluyen nombre, correo, estadísticas y palabras de repaso del usuario.

## Decisión

- `manifest.webmanifest` con íconos 192/512, ícono *maskable*, `display: standalone` y atajos a Play y Progress.
- Service worker propio (`/sw.js`, sin dependencias):
  - Recursos estáticos (`/css`, `/js`, `/img`, `/icons`, manifest): *cache-first*. Las URLs llevan `?v=<hash>` (`asp-append-version`), así que un despliegue nuevo nunca sirve código viejo.
  - Navegación: solo red; sin conexión se muestra `/offline.html` (pre-cacheada).
  - `/api/*`, métodos distintos de GET y otros orígenes: no se interceptan.
  - **No se guarda HTML autenticado ni respuestas de la API** en caché.
- Actualizaciones: el nuevo service worker queda en espera y la app muestra "A new version is ready · Update"; no se activa sin que el usuario lo acepte, para no recargar a mitad de una ronda.
- `sw.js`, el manifest y `offline.html` se sirven con `Cache-Control: no-cache`; los archivos con huella con `max-age=31536000, immutable`.

## Alternativas

| Alternativa | Ventaja | Desventaja |
|---|---|---|
| Workbox | Estrategias listas, menos código | Dependencia y paso de build adicionales para 4 reglas simples |
| Cachear páginas autenticadas (*stale-while-revalidate*) | Navegación offline | Datos personales en caché del dispositivo; estado del juego desactualizado; riesgo en equipos compartidos |
| Juego offline con respuesta en el cliente | Jugar sin red | Rompe la regla de no exponer la respuesta (proposal §3.5) |

## Consecuencias

- La app se instala y abre en su propia ventana; sin conexión muestra una pantalla clara en lugar del error del navegador.
- Jugar sigue requiriendo conexión.
- Cambiar la lista de pre-caché o la política exige subir `CACHE_VERSION` en `sw.js`.
- La instalación requiere HTTPS (Render lo da) o `localhost`.
