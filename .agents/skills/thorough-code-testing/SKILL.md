---
name: thorough-code-testing
description: >-
  Runs deep, methodical pre-ship testing and end-to-end verification across backend,
  frontend, serialization contracts, and compiled binaries to catch subtle runtime bugs
  before delivery. Use whenever finishing a feature, fixing a bug, modifying C#/JS bridges,
  updating catalogs/JSON, or when the user asks to thoroughly test or verify code.
---

# Thorough Code Testing & Verification Skill

Usa esta skill para **probar código con cuidado y detenimiento** antes de dar cualquier cambio por terminado. Su objetivo es detectar **bugs reales en tiempo de ejecución** que el compilador por sí solo no ve (como `ArrayList` vs `object[]` en `JavaScriptSerializer`, comillas sin escapar en `onclick`, desajustes de contrato C# ↔ JS o rutas rotas).

## Principios Obligatorios

1. **Compilar no equivale a funcionar:** Un build limpio solo prueba sintaxis; siempre valida el flujo real de datos de extremo a extremo.
2. **Probar los límites entre capas:** Los fallos más críticos ocurren donde dos mundos se cruzan (JSON ↔ C#, JS inline HTML ↔ DOM, CLI externo ↔ parser de stdout, archivo en disco ↔ ruta relativa).
3. **Probar casos hostiles (edge cases):** Caracteres especiales (`'`, `"`, `\`, `|`, espacios, tildes), listas vacías, listas de 1 elemento, listas grandes, ausencia de Internet, proceso bloqueado y cancelación a mitad de ejecución.
4. **Nunca ocultar un fallo con suposiciones:** Si un test falla o no puede ejecutarse, repórtalo con evidencia exacta y corrígelo antes de entregar.

## Protocolo de Pruebas Paso a Paso

### Fase 1 — Integridad de Datos y Configuración
- Validar sintaxis JSON (`ConvertFrom-Json` / parser estricto), claves obligatorias y ausencia de IDs duplicados.
- Verificar paridad entre archivos de datos externos (ej. `apps.json`) y valores por defecto en código (ej. `Models.cs`).
- Comprobar que los recursos referenciados (íconos en `ui/icons/`, scripts, XMLs de Office, DLLs de WebView2) existan y no estén vacíos (`Length > 0`).

### Fase 2 — Compilación y Actualización Automática del `.exe`
- **No depender de `build.bat` manuales.** Siempre compilar y actualizar `Qinstall.exe` directamente al terminar cambios en C# ejecutando el script con `-UpdateExe` (si el usuario tiene una ventana abierta, realiza hot-swap atómico del binario antiguo hacia `%TEMP%` y deja el nuevo `Qinstall.exe` actualizado al instante).

### Fase 3 — Pruebas de Contrato y Round-Trip en Tiempo de Ejecución
- **Puente C# ↔ JS (WebView2 / API):**
  - Verificar que **cada** `action` enviada desde JS (`postToHost({ action: '...' })`) exista en el dispatcher del backend (`WebViewForm.cs`).
  - Verificar que **cada** `{ type: '...' }` enviado desde C# tenga su `case` correspondiente en JS.
  - Ejecutar pruebas reales de deserialización usando el **mismo serializador de producción** (`JavaScriptSerializer.Deserialize<Dictionary<string, object>>`) con payloads reales (`keys`, `onlineApps`, booleanos, strings con comillas/barras) comprobando que arrays (`ArrayList`) y objetos anidados se lean sin devolver `null` ni listas vacías.
- **Lógica crítica de negocio:**
  - Probar comparadores de versiones (`1.9` vs `1.10`, `2025.3` vs `2026.3.0.1`, iguales, vacíos).
  - Probar carga de catálogo desde directorio de ensamblado y reglas de detección sin falsos positivos (ej. `IntelliJ IDEA` vs `IntelliJ IDEA Community`, `Dev-C++ 5.9.2` vs `Embarcadero Dev-C++`).

### Fase 4 — Auditoría de Frontend y Eventos DOM
- Verificar que todos los manejadores inline (`onclick`, `onchange`, `oncontextmenu`) referencien funciones definidas en `ui/app.js`.
- Comprobar que ningún `encodeURIComponent(...)` crudo se inyecte entre comillas simples `'...'` dentro de atributos HTML (usar `encodeJsArg` que escapa `'` como `%27`).
- Verificar que `ui/index.html` no dependa de CDNs externos para garantizar funcionamiento 100% offline.

### Fase 5 — Script Automatizado de Verificación (Qinstall)
Para proyectos Qinstall, ejecutar el script incluido en esta skill (con `-UpdateExe` siempre que haya cambios de C#):
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\.agents\skills\thorough-code-testing\scripts\verify_qinstall.ps1" -UpdateExe
```
(O desde la ruta global `~/.gemini/config/skills/thorough-code-testing/scripts/verify_qinstall.ps1 -ProjectRoot "<ruta>" -UpdateExe`).

## Criterio de Salida
Solo marcar la tarea como terminada cuando:
- Todas las verificaciones automatizadas terminen con código de salida `0` y `FAILED: 0`.
- Cualquier limitación ambiental (ej. `.exe` abierto que impide sobrescribir el binario final) quede claramente comunicada al usuario con el paso exacto para completarla.
