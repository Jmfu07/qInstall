---
name: qinstall-ui
description: >-
  Guía oficial de diseño, arquitectura WebView2, tokens visuales, barra Nyan Cat,
  perfiles de esenciales y contrato C#/JS para la interfaz de Qinstall.
  Activar siempre al modificar o mejorar el frontend o la ventana principal de Qinstall.
---

# Qinstall UI & Design System Skill

Esta skill define la identidad visual, los estándares de calidad y la arquitectura híbrida (**WinForms + WebView2 + C# `InstallerEngine`**) del proyecto **Qinstall**.

## 1. Identidad Visual y Tokens de Color

### Modo Oscuro (Obsidian Charcoal + Electric Violet — predeterminado)
- `--bg-base`: `#090b10`
- `--bg-surface-1`: `#10131b`
- `--bg-surface-2`: `#161a24`
- `--bg-surface-3`: `#1e2433`
- `--primary`: `#8b5cf6` (hover `#7c3aed`, glow `rgba(139, 92, 246, 0.38)`)
- `--cyan-accent`: `#06b6d4`
- `--cta-green`: `#10b981` (hover `#059669`)
- `--text-primary`: `#f8fafc`, `--text-secondary`: `#94a3b8`, `--text-muted`: `#64748b`

### Modo Claro Gris (Luminoso sin blancos agresivos)
- `--bg-base`: `#d8dce6`
- `--bg-surface-1`: `#eaedf5`
- `--bg-surface-2`: `#dfe3ee`
- `--bg-surface-3`: `#d0d5e2`
- `--primary`: `#7c3aed`
- `--text-primary`: `#0f172a`, `--text-secondary`: `#334155`, `--text-muted`: `#64748b`

### Modo Sistema
- Sincroniza automáticamente con `window.matchMedia('(prefers-color-scheme: dark)')` y notifica a C# (`setWindowTheme`) para actualizar la barra de título DWM de Windows.

## 2. Elementos Distintivos Obligatorios

1. **Logo retro IBM 8-bar (`qInstall`)**: SVG inline en el splash screen y header.
2. **Pantalla de carga (Splash / Loading Overlay)**: Progreso real conectado a los 4 pasos de arranque del backend (red/Winget, catálogo, apps instaladas, cachés offline) + barra arcoíris **Nyan Cat** reproducible desde Ajustes.
3. **Nyan Cat Progress Bar en el Footer**: Refleja el progreso real (`0%–100%`) durante instalaciones y descargas offline.
4. **Doble Vista sincronizada**:
   - **Cuadrícula (Tarjetas)**: Grid responsivo `repeat(auto-fit, minmax(clamp(290px, 28vw, 420px), 1fr))`.
   - **Tabla Densa**: Columnas Aplicación, Categoría, Versión/Caché, Origen, Estado y Acción rápida.
5. **Perfiles de Esenciales editables**: Split-button `Esenciales ▾` con perfiles **Estándar, Gamer, Dev, Técnico, Oficina** y selección personalizada persistida en `localStorage`, respetando apps ya instaladas.
6. **Badges Semánticos**:
   - `INSTALADO` (verde esmeralda)
   - `OFFLINE` (turquesa — instalador listo en `local/` o `local/cache/`)
   - `ACTUALIZAR` (rosa/naranja pulsante — caché local desactualizada, reemplazo obligatorio con Internet)
   - `ONLINE` (violeta — Winget)
   - `URL` (cian — descarga directa)
   - `TOOL` (ámbar — utilidad PowerShell interactiva)
   - `SIN RED` (rojo tenue — requiere Internet en modo offline)

## 3. Reglas de Portabilidad, Offline y Bajo Consumo en WebView2

- **100% funcional sin Internet**: Todos los iconos (`ui/lucide.min.js` y SVGs inline) deben cargarse desde el host virtual local `https://qinstall.local/`. Nunca depender de CDN externo para que la UI renderice correctamente.
- **Fuentes con fallback nativo**: Usar `'Plus Jakarta Sans', 'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif` y `'JetBrains Mono', 'Cascadia Code', 'Consolas', monospace`.
- **Bajo consumo de RAM**:
  - `WebView2` debe almacenar su `UserDataFolder` en `%TEMP%\QinstallWebView2\` (nunca en el pendrive).
  - Desactivar DevTools, menú contextual del navegador, barra de estado, zoom y procesos de sincronización en segundo plano.
  - Preferir actualizaciones de DOM dirigidas (`appUpdated`) en lugar de reconstruir todo el árbol en cada evento de progreso.
- **Respaldo automático**: Si el equipo no cuenta con WebView2 Runtime, `Program.cs` debe caer limpiamente a la ventana clásica `MainForm` (WinForms).

## 4. Contrato de Mensajería C# ↔ JS

- JS envía JSON con `window.chrome.webview.postMessage({ action, ... })`.
- C# responde con `CoreWebView2.PostWebMessageAsJson(...)` usando `{ type, ... }`.
- Nunca usar `alert()` / `confirm()` bloqueantes del navegador: usar modales propios integrados y notificaciones toast limpias.
