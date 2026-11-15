# Wordrise — Especificación funcional (v1)

Fuente: `docs/source/` (Propuesta técnica, User Stories y wireframes). Este documento resume
los requisitos implementados y registra los supuestos que tomó la implementación inicial.

## Alcance v1

| Incluido | Fuera de alcance (v1) |
|---|---|
| Registro/login con nivel CEFR (A2, B1, B2) | Niveles C1/C2 |
| Palabra del día determinista por fecha y nivel | ~~"Practice past words"~~ (implementado en el incremento 3) |
| 6 intentos, 4 pistas progresivas, "Use a hint (−10 pts)" | ~~"Listening challenge"~~ (implementado en el incremento 3) |
| Resultado con definición, ejemplo, traducción y audio (Web Speech API) | Recordatorio diario (Daily reminder) |
| Repetición espaciada de palabras falladas | Selector de idioma de interfaz (solo inglés) |
| Progreso: aprendidas, precisión, intentos promedio, por repasar, gráfica 7 días, campamentos | Efectos de sonido |
| Cuaderno de cumbres con pronunciación | |
| Modo claro/oscuro, tamaño de texto, estados con ícono además de color | |

## Incremento 2 (2026-11-14): PWA y mejoras

Solicitado por el usuario: "hazla PWA para descargar y agrégale las mejoras que veas necesarias".

| Mejora | Descripción | Implementación | Pruebas |
|---|---|---|---|
| PWA instalable | Manifest, íconos, service worker, página offline, botón "Install app" (Profile, barra lateral, login), instrucciones para iOS, aviso de nueva versión | `wwwroot/manifest.webmanifest`, `sw.js`, `offline.html`, `js/pwa.js`; ADR 0002 | Manual (Lighthouse / DevTools → Application) |
| Racha diaria | Racha actual y mejor racha en Home y Progress | `StreakCalculator`, `StatsService` | `StreakCalculatorTests` |
| Compartir resultado | Cuadrícula de colores sin letras (sin spoilers) vía Web Share API o portapapeles | `round.js` | Manual |
| Cuenta regresiva | Tiempo restante para la siguiente palabra (medianoche de Ciudad de México) | `AppClock.UntilNextDay`, `round.js` | `AppClockTests` |
| Vibración | Vibración breve ante un intento inválido (si el dispositivo la soporta) | `round.js` | Manual |
| Cabeceras de seguridad | `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options`, `Permissions-Policy`; CSP fuera de Development | `Program.cs` | Manual |

Supuesto S11: un día cuenta para la racha si la ronda diaria se terminó (ganada o perdida). Premia el hábito; confirmar si debe contar solo ganadas.

## Incremento 3 (2026-11-14): entrenamiento ilimitado

Solicitado por el usuario: "que no solo sea el reto diario, que se puedan hacer N juegos y/o entrenamientos".

| Modo | Regla | Cuenta para |
|---|---|---|
| Reto diario (sin cambios) | Una palabra por día y nivel, igual para todos | Racha, estadísticas, repaso |
| Práctica libre | Palabra aleatoria del nivel elegido (A2/B1/B2), ilimitada | Estadísticas y repaso; no racha |
| Práctica de palabras pasadas | Palabra aleatoria de los retos diarios terminados antes de hoy | Estadísticas y repaso; no racha |
| Desafío de escucha | Se escucha la palabra (Web Speech API) y se escribe; ilimitado | Solo repaso (si se falla) |
| Repaso | Sin cambios (repetición espaciada) | Estadísticas |

Reglas comunes de entrenamiento:
- Nunca se usa una palabra del reto diario de hoy (de ningún nivel), para no revelarla.
- Se evitan las últimas 20 palabras de entrenamiento mientras haya otras disponibles.
- Una sola ronda en curso por modo: "Play" reanuda la existente.
- "Skip this word" termina la ronda como fallida, revela la palabra y la manda a repaso. No existe en el reto diario ni en repaso.
- "Next word" (o Enter al terminar) inicia otra ronda con el mismo nivel/origen.

### Interacción del tablero (solicitado por el usuario)

- El botón para enviar el intento dice "Check answer" y está debajo del teclado; muestra cuántas letras faltan.
- Se puede elegir en qué casilla escribir: tocar/clic en una casilla de la fila actual la selecciona; las flechas ← → también mueven la selección. Al escribir, la letra va a la casilla seleccionada y la selección salta a la siguiente casilla vacía. Borrar limpia la casilla seleccionada (o la anterior si está vacía). Con la fila llena, para cambiar una letra se toca su casilla.
- Al terminar la ronda (ganada o perdida) el resultado aparece en un modal (`<dialog>`); en móvil se muestra como hoja inferior. Se cierra con ×, Escape o clic fuera, y se reabre con "See result". Al volver a abrir una ronda terminada, el modal aparece automáticamente.
- Banco ampliado a 36 palabras por nivel (108). Las de 6 letras pueden ser palabra del día; el resto (4–10 letras) solo entrenamiento.

Decisión y riesgo (desafío de escucha): la síntesis de voz ocurre en el navegador, así que el cliente recibe la palabra (`spokenWord`) desde el inicio. Por eso esas rondas no cuentan para palabras aprendidas, precisión ni promedio. Los demás modos mantienen la regla de no enviar la respuesta antes de terminar.

Requiere migración: el modelo agrega `ck_rounds_mode` y cambia `ck_rounds_daily_link` para aceptar los modos nuevos.

## Incremento 4 (2026-11-14): enganche

Elegido por el usuario tras el análisis: liga (por nivel y por grupos), XP, meta diaria, logros, protector de racha, pista con emoji, sonidos/animaciones y modo contrarreloj.

| Mejora | Regla | Implementación |
|---|---|---|
| XP | Diario: puntaje (o 5 si se pierde) + 25 de bono; repaso/práctica: puntaje o 5; escucha: 10/2; contrarreloj: 5 por palabra | `XpCalculator`, `rounds.xp`, `sprint_sessions.xp` |
| Liga semanal por nivel | Lunes a domingo (Ciudad de México); usuarios del mismo nivel con `show_in_leagues`; top 30 + tu posición; solo primer nombre | `LeagueService`, `/league` |
| Grupos | Crear con nombre (3–40) y compartir código de 6 caracteres; máx. 10 grupos por usuario y 100 miembros; el último en salir borra el grupo | `groups`, `group_members`, `/league/groups/{id}` |
| Meta diaria | 1–20 palabras al día (rondas terminadas + palabras del contrarreloj); anillo en Home y Play | `EngagementService.GetGoalAsync` |
| Logros | 15 logros (`AchievementCatalog`); se avisan una vez al terminar una ronda o un contrarreloj | `user_achievements`, `/api/me/progress` |
| Protector de racha | +1 cada 7 días de racha (máx. 2); se usa solo al abrir Home/Play si faltó 1–2 días; esos días no suman a la racha | `StreakFreezePolicy`, `streak_freeze_days` |
| Pista con emoji | Opcional, −10 pts, solo en 71 palabras concretas | `words.emoji`, `rounds.emoji_hint_used` |
| Combo | Victorias seguidas de entrenamiento hoy | `ComboCalculator` |
| Contrarreloj 60 s | Anagrama con definición; el servidor guarda palabra, tiempo y puntaje; respuesta idempotente por número de reto; acepta otros anagramas válidos | `SprintService`, `sprint_sessions`, `/play/sprint/{id}` |
| Sonidos y confeti | Web Audio sintetizado (sin archivos), confeti en canvas; se desactivan en Profile y respetan `prefers-reduced-motion` | `wwwroot/js/fx.js` |

Supuestos S12–S14: valores de XP, umbrales de logros y reglas del protector son decisiones de diseño, ajustables en `Domain/Game/Engagement.cs`.

Riesgos: el ranking global se calcula agregando las rondas de la semana del nivel (adecuado para cientos de usuarios; con miles conviene una tabla de totales semanales). La práctica ilimitada permite acumular XP jugando mucho; el rate limit (120/min) evita automatización burda pero no la elimina.

## Trazabilidad HU → implementación

| HU | Criterios | Implementación | Pruebas |
|---|---|---|---|
| HU-01 Adivinar con pistas | AC-01.1/2/3 | `GameService.GetOrStartDailyRoundAsync`, `SubmitGuessAsync`, `HintPolicy`, `GuessEvaluator`, `Views/Play/Round` | `GuessEvaluatorTests`, `HintPolicyTests` |
| HU-02 Pronunciación | AC-02.1/2/3 | `RoundResultView`, `wwwroot/js/speech.js`, `round.js` (oculta bocina si no hay soporte) | `RoundStateTests`; AC-02.2/02.3 requieren prueba manual en navegador |
| HU-03 Progreso | AC-03.1/2/3 | `StatsService`, `CampCalculator`, `Views/Progress` | `CampCalculatorTests` |
| HU-04 Repaso | AC-04.1/2/3 | `GameService.FinishRoundAsync`, `GetOrStartReviewRoundAsync`, `SpacedRepetitionScheduler`, Dashboard "Review today" | `SpacedRepetitionSchedulerTests` |
| HU-05 Cuenta y nivel | AC-05.1/2/3 | `AccountController`, `AccountService`, `ProfileController` | Pendiente: pruebas de integración |

## Reglas

- La validación de cada intento ocurre en el servidor; el cliente nunca recibe la respuesta antes de que la ronda termine.
- Intento válido: misma longitud que la respuesta, solo letras a–z y presente en `valid-guesses.txt` (lista separada del banco de respuestas).
- Cambio de día: `America/Mexico_City`.
- Una sola ronda diaria por usuario y día (índice único).
- Cambiar el nivel no altera una ronda diaria ya iniciada (AC-05.3).

## Supuestos (pendientes de confirmación)

| # | Supuesto | Motivo |
|---|---|---|
| S1 | Pistas automáticas en los intentos 1, 2, 4 y 5. | La propuesta dice "cada intento fallido desbloquea una pista", pero el wireframe (intento 3 = 2 de 4) y AC-01.3 (intento 4 = 3 de 4) indican estos umbrales. Se siguió el wireframe/AC. |
| S2 | Puntaje: 100 − 10 por intento adicional − 10 por pista anticipada; mínimo 10 si se gana; 0 si se pierde. | Solo se define "−10 pts" por pista. |
| S3 | Intervalos de repaso 1, 3, 7, 14, 30 días (configurables). | No definidos. |
| S4 | Campamentos Base (0), Slope (10), Ridge (30), Summit (100) palabras (configurables). | Solo se definen nombres; el wireframe muestra Ridge con 42 y Summit en 100. |
| S5 | Palabras aprendidas = palabras distintas acertadas; precisión = ganadas/terminadas; intentos promedio sobre rondas terminadas (perdida = 6). | No definidos. |
| S6 | El registro pide **nombre** además de correo, contraseña y nivel. | El modelo de datos y AC-05.1 requieren el nombre; el wireframe no lo muestra. |
| S7 | La pantalla de login no pide nivel (solo el registro). | El wireframe lo muestra en ambas pestañas; en login no tiene efecto. |
| S8 | `Round` guarda `WordId` y `DailyWordId` opcional, y `Mode` (Daily/Review). | El modelo original solo enlaza la ronda a la palabra del día; las rondas de repaso no tienen palabra del día. |
| S9 | Todas las respuestas del banco inicial tienen 6 letras; la cuadrícula se adapta a la longitud. | AC-01.1 indica cuadrícula de 6 letras. |
| S10 | El ajuste de audio controla la reproducción automática al terminar; el botón de bocina siempre está disponible si el navegador lo soporta. | HU-02 exige poder escuchar todas las palabras. |

## Riesgos conocidos

- El banco inicial (36 palabras, 12 por nivel) cubre demostraciones, no uso prolongado; los niveles CEFR son aproximados.
- "Review today" muestra el texto de la palabra (según AC-04.2 y wireframe), lo que revela la respuesta antes de repasarla.
- Render free: el servicio se suspende por inactividad y la base de datos gratuita expira; revisar el plan antes de la demo.
