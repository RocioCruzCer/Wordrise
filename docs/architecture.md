# Wordrise — Arquitectura

## Componentes

Aplicación monolítica ASP.NET Core MVC de tres capas (interfaz, lógica, datos), desplegada como un contenedor Docker en Render.

```mermaid
flowchart LR
    B[Navegador<br/>Razor + CSS + JS<br/>Web Speech API] -- HTML / fetch JSON + CSRF --> W[ASP.NET Core MVC<br/>Controllers]
    W --> S[Services<br/>Game · Stats · Account]
    S --> D[Domain<br/>reglas puras]
    S --> E[EF Core 10<br/>WordriseDbContext]
    E --> P[(PostgreSQL<br/>Render)]
    W -. cookies auth +<br/>data protection keys .-> P
```

- `Domain/Game`: lógica sin dependencias (evaluación de intentos, pistas, puntaje, selección diaria, campamentos, repetición espaciada). Probada con pruebas unitarias.
- `Services`: casos de uso; únicos que usan `WordriseDbContext`.
- `Controllers`: MVC para páginas; `RoundsApiController` (`/api/rounds/{id}`, `/guesses`, `/hints`) para la ronda.

## Flujo de una ronda

```mermaid
sequenceDiagram
    actor U as Usuario
    participant C as Navegador
    participant S as Servidor
    participant DB as PostgreSQL
    U->>C: Play today's word
    C->>S: POST /play/today
    S->>DB: daily_words (fecha, nivel) → crea si no existe
    S->>DB: rounds (usuario, palabra del día) único
    S-->>C: Vista con estado (sin respuesta)
    loop hasta acertar o 6 intentos
        U->>C: escribe intento
        C->>S: POST /api/rounds/{id}/guesses
        S->>S: valida longitud, letras, lista válida
        S->>DB: attempts + rounds (+ pending_reviews si falla) en una transacción
        S-->>C: estados por letra, pistas desbloqueadas
    end
    S-->>C: resultado (palabra, definición, ejemplo, traducción, puntaje)
    C->>U: pronunciación con SpeechSynthesis
```

## Modelo de datos

```mermaid
erDiagram
    users ||--o{ rounds : plays
    users ||--o{ pending_reviews : has
    words ||--o{ daily_words : "is daily word"
    words ||--o{ rounds : target
    words ||--o{ pending_reviews : reviewed
    daily_words ||--o{ rounds : "daily rounds"
    rounds ||--o{ attempts : contains
```

| Tabla | Columnas principales | Restricciones |
|---|---|---|
| `users` | id, name, email, password_hash, level, pronunciation_audio_enabled, created_at | email único y en minúsculas; level ∈ {A2,B1,B2} |
| `words` | id, text, level, part_of_speech, definition_en, example_sentence, translation_es, is_daily_answer | text único, `^[a-z]{4,10}$`; índice (level, is_daily_answer) |
| `daily_words` | id, date, level, word_id | único (date, level); FK word RESTRICT |
| `rounds` | id, user_id, mode, daily_word_id?, word_id, played_on, status, attempts_used, hints_unlocked, manual_hints_used, score, started_at, finished_at | único (user_id, daily_word_id) parcial; checks de rangos y coherencia mode/daily_word_id; FK user CASCADE, word/daily_word RESTRICT |
| `attempts` | id, round_id, number, guess_text, feedback, created_at | único (round_id, number); FK round CASCADE |
| `pending_reviews` | id, user_id, word_id, stage, next_review_on, last_missed_on | único (user_id, word_id); índice (user_id, next_review_on); FK user CASCADE, word RESTRICT |
| `data_protection_keys` | id, friendly_name, xml | Claves de cookies persistentes entre despliegues |

Las estadísticas se calculan desde `rounds` (sin tablas adicionales). Fechas de juego: `date` en zona `America/Mexico_City`; instantes: `timestamptz` en UTC.

## Seguridad

- Contraseñas con `PasswordHasher<T>` (PBKDF2 con sal). Mensaje genérico ante credenciales inválidas.
- Cookie `HttpOnly`, `SameSite=Lax`; antiforgery global (formularios y header `X-CSRF-TOKEN` en `fetch`).
- Cada consulta de ronda filtra por `user_id` del usuario autenticado (sin IDOR).
- Rate limiting: 10/min por IP en login/registro; 60/min por usuario en la API de rondas.
- La respuesta nunca se envía al cliente mientras la ronda está en curso.

## PWA

```mermaid
flowchart LR
    P[Página] -- registra --> SW[sw.js]
    SW -- /css /js /img /icons: cache-first --> C[(Cache Storage<br/>wordrise-static-vN)]
    SW -- navegación: solo red --> S[Servidor]
    SW -. sin red .-> O[/offline.html/]
    P -- /api/*: sin interceptar --> S
```

Detalle y alternativas en `docs/adr/0002-pwa-caching.md`. No se guardan HTML autenticado ni respuestas de la API.
