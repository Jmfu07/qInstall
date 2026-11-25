---
name: code-optimization
description: >-
  Audits and optimizes application performance, startup speed, memory/RAM footprint,
  DOM rendering smoothness, async I/O concurrency, and asset size with measurement-first
  discipline and zero functional regressions. Use when optimizing code, reducing lag or
  memory usage, speeding up startup/loading, or refactoring hot paths.
---

# Code Optimization Skill

Usa esta skill para **optimizar código con criterio técnico y medición real**, priorizando impacto perceptible por el usuario (arranque rápido, interfaz fluida a 60 fps, bajo consumo de RAM y tamaño ligero) sin sacrificar legibilidad ni introducir regresiones.

## Regla de Oro: Medir Antes y Después

1. **Identifica el cuello de botella real** antes de tocar código (no hagas micro-optimizaciones ciegas).
2. **Aplica mejoras de alto impacto primero** (I/O redundante, renderizados completos innecesarios, tareas secuenciales paralelizables, assets sobredimensionados).
3. **Valida cero regresiones** ejecutando la suite de pruebas (`thorough-code-testing`) tras cada ronda de optimización.

## Playbook de Optimización por Capa

### 1. Frontend & WebView2 (Fluidez a 60 fps y Baja RAM)
- **Renderizado selectivo:**
  - Renderizar únicamente la vista activa y la categoría activa (evitar inyectar cientos de nodos DOM ocultos simultáneamente).
  - Para actualizaciones individuales (ej. progreso o estado de 1 app), parchear el nodo específico (`data-key`) en lugar de reconstruir todo el contenedor.
- **Animaciones y Layout:**
  - Usar `requestAnimationFrame` con propiedades compuestas en GPU (`transform`, `opacity`) y `will-change` solo en elementos animados; evitar `setInterval` o leer `offsetWidth`/`getBoundingClientRect` dentro de bucles de escritura (layout thrashing).
- **Imágenes e Íconos:**
  - Usar `loading="lazy" decoding="async"` en imágenes e íconos, redimensionados a su tamaño real de pantalla (`64×64` PNG compacto en lugar de imágenes de 512–2048 px).
  - Preferir SVGs inline reutilizables para elementos repetitivos (ej. checkboxes) en lugar de invocar `lucide.createIcons()` sobre cientos de nodos idénticos.
- **Memoria de WebView2:**
  - Mantener `UserDataFolder` en `%TEMP%`, desactivar DevTools/context menu/zoom innecesarios y evitar acumular listeners duplicados al re-renderizar.

### 2. Backend C# / .NET & Subprocesos
- **Paralelismo seguro:** Lanzar comprobaciones independientes en paralelo con `Task.WhenAll` (ej. conectividad de red + versión de Winget + escaneo de registro) en lugar de `await` en cascada.
- **Reducción de subprocesos:** Cada invocación a `winget` o `powershell` cuesta cientos de milisegundos; cachear resultados de una sesión y evitar repetir consultas idénticas (ej. consultar `winget list` una sola vez por escaneo en lugar de por cada app).
- **Estructuras de datos eficientes:** Usar `HashSet<string>(StringComparer.OrdinalIgnoreCase)` y `Dictionary<string, T>` para búsquedas `O(1)` en lugar de escaneos lineales `O(n²)` dentro de bucles.
- **Cadenas y memoria:** Usar `StringBuilder` al acumular salida de procesos o construir JSON/HTML; siempre disponer (`using`) recursos GDI+/IO/Registry/Process.

### 3. Portabilidad y Tamaño de Paquete
- Mantener assets 100% locales y optimizados (sin CDNs bloqueantes).
- Evitar duplicar archivos binarios mediante alias lógicos cuando múltiples entradas comparten el mismo ícono o instalador base.

## Verificación Obligatoria Post-Optimización
1. Comparar métricas antes vs después (tiempo de arranque/escaneo, nodos DOM renderizados, tamaño de assets en KB, llamadas a subprocesos).
2. Ejecutar verificación completa (`thorough-code-testing`) para confirmar que la optimización no rompió ningún comportamiento ni contrato.
