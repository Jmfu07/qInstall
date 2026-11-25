# qInstall

**qInstall** es un instalador por lotes **100% portable** para Windows 10 y Windows 11, pensado específicamente para vivir en **pendrives USB y discos duros externos** como kit todo-en-uno de post-formateo, soporte técnico y despliegue rápido de programas (alternativa moderna, visual y offline/online a Ninite).

---

## Características principales

### 1. Diseñado para Pendrives y Discos Externos (100% desde la interfaz)
- **Panel integrado de `Almacenamiento`:** sin archivos `.bat` ni comandos manuales. Desde la propia interfaz puedes ver el espacio libre/usado de tu unidad, administrar cada instalador descargado y **copiar una versión portable limpia de `qInstall` a cualquier pendrive USB o disco externo** en un clic.
- **Perfiles portables (`profiles.json`):** tus perfiles de **Esenciales** (*Estándar, Gamer, Dev, Técnico, Oficina* o tu lista personalizada), tu tema visual, tu vista preferida y tus ajustes se guardan junto a `Qinstall.exe` para que viajen contigo a cualquier PC.
- **Detección en vivo (cero archivos `.log`):** detecta al instante qué programas ya están instalados en la PC conectada combinando el Registro de Windows (`HKLM` 64/32 bits + `HKCU`), `winget list` (Winget, ARP y Microsoft Store), Office Click-to-Run y librerías DirectX.

### 2. Interfaz Moderna WebView2 (100% Offline + Respaldo Nativo)
- **Cero dependencias de Internet para la UI:** todo el frontend (`ui/index.html`, `ui/styles.css`, `ui/app.js`, `ui/lucide.min.js`) y los **125 íconos oficiales a color (`ui/icons/*.png`, < 500 KB en total)** se sirven localmente mediante host virtual `https://qinstall.local/`.
- **3 modos de apariencia:** **Modo Oscuro** (*Obsidian Charcoal*), **Modo Claro** (*Gris descansado*) y **Sistema (Automático)** sincronizado con la barra de título nativa DWM de Windows.
- **3 vistas enfocadas por categoría** (sin sobrecargar la pantalla):
  - **Galería:** lista compacta con ícono, estado y selección rápida.
  - **Cuadrícula:** tarjetas visuales estilo tienda con logo oficial y descripción.
  - **Tabla:** vista técnica con columnas de categoría, caché local, origen, estado y acción.
- **Animación fluida a 60 fps con Nyan Cat:** pantalla de carga y barra de progreso inferior sincronizadas cuadro a cuadro (`requestAnimationFrame`), con avance gradual inteligente (`1% → 2% → ...`) mientras los instaladores descargan o trabajan en segundo plano.
- **Respaldo automático WinForms:** si conectas el pendrive en un Windows recién instalado que aún no tenga *Microsoft Edge WebView2 Runtime*, `qInstall` abre automáticamente su ventana clásica nativa WinForms (`MainForm`) para que nunca te quedes sin herramienta.

### 3. Motor Híbrido: Online + Instaladores Descargados (`local\cache\`)
- **4 orígenes combinados:**
  1. **Winget & Microsoft Store (`winget` / `msstore`):** descarga e instala siempre la última versión oficial sin adware (incluye soporte directo para apps de Microsoft Store como WhatsApp aunque la app Tienda esté desinstalada; si falta Winget, ofrece instalarlo con un clic).
  2. **Enlaces directos (`url`):** descarga directa de instaladores oficiales (ej. *Dev-C++ 5.9.2*, *NVIDIA App*, *uTorrent Classic*).
  3. **Instaladores locales (`local`):** ejecuta archivos `.exe` o `.msi` desde el pendrive (`local\` y plantillas de `office\`).
  4. **Herramientas interactivas (`powershell`):** utilidades técnicas elevadas como Administrador (*Crear Punto de Restauración*, *DISM + SFC*, *Respaldo BCD*, *Chris Titus WinUtil*, *ReviOS*, *Reparar Microsoft Store*, *AMD Auto-Detect*).
- **Botón `Descargar offline`:** guarda los instaladores oficiales en `local\cache\<id>\` junto con su manifiesto `.yaml` para instalarlos después en PCs **sin Internet** (marcados como **`DESCARGADO`**).
- **Control inteligente de espacio y versiones:**
  - **Restricción de paquetes gigantes en pendrive:** programas que ocuparían varios GB offline (*Microsoft Office*, *Visual Studio 2022*, *Android Studio*, *Docker Desktop*, *ComfyUI*, *LM Studio* e *IDEs individuales de JetBrains*) están restringidos a **instalación online** para no saturar el pendrive (para Office viaja únicamente el lanzador ligero `office\setup.exe` de ~3.5 MB).
  - **Purga/reemplazo de versiones antiguas (`ACTUALIZAR`):** cuando conectas el pendrive en una PC con Internet, `qInstall` verifica en paralelo (`4×` más rápido) si salió una versión nueva de tus instaladores descargados y solicita purgarlos o reemplazarlos para evitar fallos.
  - **Gestor individual de descargados:** desde **Almacenamiento** (o con clic derecho → *Borrar descargado*) puedes ver cuánto pesa cada instalador en MB/GB y borrar uno puntual sin tocar los demás.

### 4. Mantenimiento Técnico Integrado
- **Actualizar todas las apps del equipo (`Actualizar`):** ejecuta `winget upgrade --all` con salida CLI en vivo y cancelación segura.
- **Desinstalar desde la interfaz:** clic derecho sobre cualquier app instalada → **Desinstalar** vía Winget.
- **Punto de restauración en 1 clic:** crea un punto de restauración de Windows (100% offline) antes de instalar drivers o aplicar optimizaciones.
- **Buscador dual (`/`):** filtra al instante entre las 163 apps del catálogo y consulta simultáneamente el repositorio online de Winget (con botón `+` para guardar nuevos programas en tu `apps.json`).
- **Modal `+ Local` integrado:** agrega tus propios instaladores `.exe` o `.msi` copiándolos automáticamente a `local\` (con verificación previa de espacio libre en el pendrive).

---

## Requisitos del sistema y de almacenamiento

| Requisito | Detalle |
| :--- | :--- |
| **Sistema Operativo** | Windows 10 (1809+) o Windows 11 (`x64`) |
| **Permisos** | Administrador (solicitado automáticamente vía manifiesto UAC para instalar sin bloqueos) |
| **Capacidad de Pendrive / Disco** | **Mínimo obligatorio: `2 GB`** (capacidad total y libre; unidades menores a `2 GB` son bloqueadas automáticamente) · **Recomendado: `4 GB o más`** (`16 GB+` si planeas guardar varios instaladores offline en `local\cache\`) |
| **Dependencias externas** | **Ninguna.** Usa `.NET Framework 4.8` nativo de Windows + librerías `WebView2` incluidas |

---

## Catálogo incluido (163 aplicaciones en 14 categorías)

| Categoría | Ejemplos incluidos |
| :--- | :--- |
| **Navegadores** | Google Chrome, Mozilla Firefox, Brave, Opera GX |
| **Utilidades** | 7-Zip, WinRAR, Everything, PowerToys, Rufus, CrystalDiskInfo, CPU-Z, NZXT CAM, WizTree, TreeSize, qBittorrent, uTorrent, Reparar Microsoft Store |
| **Optimizacion** | Chris Titus WinUtil, ReviOS (AME + Playbook), O&O ShutUp10++, Winaero Tweaker, BleachBit, Bulk Crap Uninstaller, Revo Uninstaller, Autoruns, Process Lasso, Windhawk |
| **Arranque y Reparacion** | Ventoy, DiskGenius, TestDisk, AOMEI Partition Assistant, MiniTool Partition Wizard, EFI Boot Editor, PartBoot, DISM + SFC, Respaldo BCD |
| **Soporte y Drivers** | Crear Punto de Restauración, NVCleanstall (Drivers NVIDIA), NVIDIA App, NVIDIA PhysX, AMD Drivers Auto-Detect (Radeon + Chipset), Intel Driver & Support Assistant, Snappy Driver Installer Origin (SDIO), DDU, HWiNFO64, GPU-Z, MSI Afterburner, Malwarebytes, AnyDesk, TeamViewer, Parsec, Bitwarden, KeePassXC |
| **Multimedia** | VLC, Spotify, OBS Studio, ShareX, GIMP, Paint.NET |
| **Comunicacion** | Discord, WhatsApp (Microsoft Store), Telegram Desktop, Zoom Workplace |
| **Oficina** | Microsoft Office 2024 / 2021 / 2019 / 2016 (ODT), LibreOffice, SumatraPDF, Adobe Acrobat Reader, Obsidian, Google Drive |
| **IA y Agentes** | ChatGPT, Codex CLI, Claude Desktop, Claude Code, Google Gemini, Gemini CLI, Google Antigravity / IDE / CLI, OpenCode Desktop & CLI, GitHub Copilot & CLI, Cursor, Windsurf, Trae, Perplexity Comet, Ollama, LM Studio, Jan AI, Cherry Studio, ComfyUI |
| **IDEs y JetBrains** | JetBrains Toolbox, IntelliJ IDEA (Pro & Community), PyCharm (Pro & Community), WebStorm, CLion, Rider, PhpStorm, DataGrip, GoLand, RustRover, Visual Studio 2022 Community, Android Studio, Eclipse (Java/Web & C/C++), Apache NetBeans, VSCodium, Sublime Text 4, Zed, Neovim, Dev-C++ 5.9.2 (Orwell), Embarcadero Dev-C++, Code::Blocks (MinGW), Arduino IDE 2, Godot Engine, Unity Hub |
| **Desarrollo** | Visual Studio Code, GitHub Desktop, Git, Notepad++, Windows Terminal, Python 3.12, Node.js LTS, Postman, DBeaver Community, Docker Desktop |
| **Juegos** | Steam, Epic Games Launcher |
| **Runtimes** | Visual C++ AIO (2005–2022) + paquetes individuales x64/x86 (2005–2022), DirectX Runtime, .NET Desktop Runtime 6 / 8 / 9 / 10 / 11, XNA Framework 4.0, OpenAL, WebView2 Runtime |
| **Java** | Java 8 (Oracle JRE), Temurin JRE & JDK 11 / 17 / 21 LTS, Oracle JDK 25 LTS |

---

## Guía rápida de parámetros silenciosos (`+ Local`)

La opción **Instalación silenciosa** permite instalar programas en segundo plano sin asistentes (*«Siguiente → Acepto → Finalizar»*):
- **Paquetes de Winget:** `qInstall` aplica `--silent` automáticamente.
- **Instaladores descargados offline (`local\cache\`):** `qInstall` extrae automáticamente el parámetro silencioso oficial desde el manifiesto `.yaml`.
- **Tus propios archivos `.exe` / `.msi` agregados con `+ Local`:**

| Tipo de instalador | Parámetro silencioso recomendado |
| :--- | :--- |
| **NSIS** *(7-Zip, VLC, Notepad++, Dev-C++)* | `/S` |
| **Inno Setup** | `/VERYSILENT /NORESTART` |
| **Windows Installer (`.msi`)** | `/qn /norestart` *(se aplica solo si se deja vacío)* |
| **Microsoft / WiX / Burn** | `/quiet /norestart` |
| **Desconocido / Instalación manual guiada** | *Dejar vacío* (mostrará el asistente normal) |

---

## Cómo llevarlo en tu Pendrive o Disco Externo

1. Abre `Qinstall.exe`.
2. Haz clic arriba a la derecha en **`Almacenamiento`**.
3. En **Copiar qInstall a Otra Unidad**, selecciona tu pendrive USB o disco duro externo (mínimo **2 GB**, recomendado **4 GB+**) y pulsa **`Copiar a unidad`**.
4. `qInstall` copiará automáticamente todos los archivos necesarios listos para usar en cualquier PC:

```text
Qinstall/
├── Qinstall.exe                        # Ejecutable principal (x64, UAC Admin, Per-Monitor DPI)
├── Microsoft.Web.WebView2.Core.dll     # Motor WebView2 embebido
├── Microsoft.Web.WebView2.WinForms.dll
├── WebView2Loader.dll
├── apps.json                           # Catálogo editable de aplicaciones
├── profiles.json                       # Tus perfiles y preferencias portables (se crea al guardar)
├── ui/                                 # Interfaz offline + 125 íconos PNG en ui/icons/
├── office/                             # Microsoft Office Deployment Tool (setup.exe + XMLs 2016-2024)
└── local/                              # Tus instaladores .exe/.msi + caché offline (local/cache/)
```

---

## Formato de `apps.json`

Puedes editar `apps.json` manualmente o desde la propia interfaz (`+` en búsqueda online, `+ Local` o clic derecho → *Quitar del catálogo*):

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

Valores admitidos en `Source`:
- `"winget"` — ID oficial de Winget o Microsoft Store (ej. `Google.Chrome` o `9NKSQGP7F2NH`).
- `"url"` — URL directa a un `.exe` o `.msi` junto con `SilentArgs`.
- `"local"` — Ruta relativa dentro del pendrive (ej. `local\mi_app.exe` o `office\setup.exe`).
- `"powershell"` — Comando interactivo ejecutado en PowerShell elevado.

---

## Compilación y verificación QA (Desarrollo)

No requiere instalar Visual Studio ni SDKs pesados: compila directamente con el compilador C# de `.NET Framework 4.8` incluido en Windows y valida todo el proyecto (JSON, paridad de catálogo, contratos C# ↔ JS, escape de eventos HTML, modo 100% offline y actualización automática de `Qinstall.exe`) ejecutando:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\.agents\skills\thorough-code-testing\scripts\verify_qinstall.ps1" -UpdateExe
```
