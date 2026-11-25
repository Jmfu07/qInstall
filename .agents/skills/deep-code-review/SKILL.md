---
name: deep-code-review
description: >-
  Performs rigorous, line-by-line static and architectural code review to uncover subtle
  bugs, serialization/type mismatches, escaping flaws, race conditions, contract drift,
  and resource leaks before they reach production. Use when reviewing diffs, auditing
  critical files, investigating elusive bugs, or when the user asks for a careful code review.
---

# Deep Code Review Skill

Usa esta skill para **revisar código con detenimiento** con mentalidad de auditor sénior. No te limites a verificar estilo: busca fallos silenciosos en tiempo de ejecución, contratos rotos entre capas y casos borde reales.

## Checklist Sistemática de Revisión

### 1. Contratos Entre Capas y Serialización
- **Deserialización estricta:** Verificar tipos reales devueltos por el serializador en runtime (en .NET `JavaScriptSerializer`, los arrays JSON se deserializan como `ArrayList` y los objetos como `Dictionary<string, object>`, **nunca** `object[]`). Usar `IEnumerable` (excluyendo `string`) y helpers defensivos.
- **Paridad de mensajes:** Todo `action` enviado por el frontend debe existir en el backend; todo `type` emitido por el backend debe procesarse en el frontend con el nombre exacto de cada propiedad.
- **Compatibilidad de compilador:** Confirmar sintaxis válida para el target real (en Qinstall: **C# 5 / .NET Framework 4.8**, prohibidos `$""`, `?.`, `nameof`, `out var`, miembros con `=>`).

### 2. Escapado, Inyección y Manejo de Strings Hostiles
- **HTML inline (`onclick`, `oncontextmenu`):** Nunca interpolar strings arbitrarios o `encodeURIComponent(...)` crudo dentro de comillas simples `'...'` (ya que `encodeURIComponent` NO escapa `'`). Usar `encodeJsArg` o `data-*` + `addEventListener`.
- **XSS / DOM:** Todo texto dinámico insertado en `innerHTML` debe pasar por `escapeHtml()`.
- **Argumentos de procesos / Shell:** Validar comillas, espacios en rutas y rutas relativas (`WorkingDirectory`, comillas en `--id`, `-d`, `/i`).

### 3. Concurrencia, Cancelación y Estado
- **Operaciones bloqueantes y cancelación:** Verificar que procesos elevados o largos no cuelguen la UI (`WaitForExit(250)` en bucle comprobando cancelación) y que `_isBusy` / flags de estado siempre se restauren en bloques `try / finally`.
- **Condiciones de carrera en UI:** Comprobar secuencias (`searchSeq`) para respuestas asíncronas fuera de orden, doble clic en botones destructivos o de instalación, y desincronización entre estado en memoria y DOM.
- **Bloqueo de archivos:** Comprobar accesos concurrentes, permisos de escritura en carpetas de solo lectura o ejecutables abiertos.

### 4. Manejo de Errores y Fugas de Recursos
- **Capturas silenciosas (`catch {}`):** Justificar cada `catch` vacío; nunca tragar errores que dejen al usuario sin feedback ni estado inconsistente.
- **Recursos no administrados:** Todo `Process`, `WebClient`, `RegistryKey`, `Bitmap`, `Graphics`, `MemoryStream` o `FileStream` debe cerrarse con `using` o `Dispose()`.

### 5. Precisión de Lógica de Dominio
- **Falsos positivos en detección/filtros:** Revisar prefijos ambiguos (ej. `IntelliJ IDEA` vs `IntelliJ IDEA Community`, `Dev-C++ 5.9` vs `Dev-C++ 6`, `Claude` vs `Claude Code`).
- **Comparaciones numéricas vs lexicográficas:** Versiones (`1.9` vs `1.10`) y tamaños nunca deben compararse como texto plano.

## Formato de Entrega de la Revisión

Presenta los hallazgos ordenados por severidad real:
1. **`CRÍTICO`** — Falla funcional segura, pérdida de datos, vulnerabilidad o bloqueo total.
2. **`ALTO`** — Bug en casos borde frecuentes, fuga de recursos grave o inconsistencia de estado.
3. **`MEDIO`** — Problema de mantenibilidad, rendimiento degradado o manejo de errores pobre.
4. **`BAJO`** — Mejora menor de claridad o robustez.

Para cada hallazgo incluye:
- **Archivo y líneas exactas** (con enlace `file://`).
- **Escenario concreto de fallo** (qué entrada o acción lo dispara).
- **Corrección exacta recomendada** (o aplicada si el usuario pidió corregir).
