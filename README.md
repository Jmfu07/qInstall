# Qinstall

Instalador rápido, ligero y portable de aplicaciones para Windows (alternativa propia a **Ninite**), diseñado para llevarse directamente en un pendrive USB.

## Características

- **Interfaz nativa moderna con WebView2 (100% offline)**: Ventana ligera con temas **Oscuro Obsidian / Claro Gris / Sistema**, pantalla de carga y progreso animado **Nyan Cat**, vista **Cuadrícula vs Tabla Densa**, **Perfiles de Esenciales editables** (Estándar, Gamer, Dev, Técnico, Oficina), consola CLI desplegable y respaldo automático a WinForms clásico si faltara WebView2 Runtime.
- **Portable y ligero**: Compila en 1 segundo sin instalar Visual Studio ni SDKs adicionales usando `build.bat`.
- **Modo Online con Winget**: Descarga e instala siempre la última versión oficial de cada programa de forma silenciosa y sin bloatware. Si el equipo no tiene Winget, ofrece instalarlo con un clic.
- **Modo descarga offline para pendrive (`Descargar offline`)**: Marca cualquier aplicación (Winget, URL u Office) y descárgala directamente en `local/cache/` (o `office/Office/Data/` para Office) sin instalarla en tu PC; cuando lleves el pendrive a un equipo sin Internet, Qinstall detectará automáticamente esos instaladores (`OFFLINE`) y los instalará localmente. Si vuelves a un PC con Internet y existe una versión más nueva, exige reemplazar el instalador local (`ACTUALIZAR`).
- **Instaladores locales offline (`local/`)**: Agrega cualquier `.exe` o `.msi` propio con **`+ Instalador local`** o pégalo en `local\`.
- **Validación automática Online / Offline**: Detecta en vivo si el equipo tiene conexión a Internet y valida qué aplicaciones dependen de red (Winget, URLs, Office ODT sin datos offline, scripts remotos) frente a instaladores locales 100% offline.
- **Buscador integrado (Catálogo + Winget online)**: Escribe en `Buscar:` (atajo `/`) para filtrar tus apps o buscar e instalar al vuelo cualquier programa de Winget.
- **Microsoft Office integrado (2016, 2019, 2021 y 2024)**: Incluye Microsoft Office Deployment Tool (`office/setup.exe`) junto con las configuraciones `office2016.xml`, `office2019.xml`, `office2021.xml` y `office2024.xml`.

## Uso en tu pendrive

1. Compila el proyecto ejecutando `build.bat` (o usa el `Qinstall.exe` ya generado).
2. Copia `Qinstall.exe`, las 3 DLLs de WebView2 (`Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.WinForms.dll`, `WebView2Loader.dll`), `apps.json` y las carpetas `ui/`, `office/` y `local/` a tu pendrive.
3. En cualquier PC con Windows 10/11, ejecuta `Qinstall.exe`, marca las aplicaciones y presiona **Instalar seleccionadas** (o **Descargar offline** para guardarlas en el USB).

## Cómo compilar desde el código fuente

No necesitas instalar Visual Studio ni SDKs adicionales. Solo ejecuta:

```bat
build.bat
```

Esto utiliza el compilador `csc.exe` integrado en Windows para generar `Qinstall.exe`.

## Estructura de `apps.json`

```json
{
  "Id": "7zip",
  "Name": "7-Zip",
  "Category": "Utilidades",
  "Description": "Compresor de archivos ligero y libre",
  "Source": "winget",
  "Target": "7zip.7zip",
  "SilentArgs": "",
  "Essential": true
}
```

Los valores válidos para `Source` son:
- `"winget"`: `Target` es el ID del paquete en Winget (ej. `Google.Chrome`).
- `"url"`: `Target` es el enlace directo al `.exe` o `.msi` y `SilentArgs` los parámetros silenciosos (ej. `/S` o `/VERYSILENT`).
- `"local"`: `Target` es la ruta relativa junto a `Qinstall.exe` (ej. `office\setup.exe`).
- `"powershell"`: `Target` es un comando o script interactivo que se abre en PowerShell como Administrador (ej. `irm 'https://christitus.com/win' | iex` para Windows Utility).
