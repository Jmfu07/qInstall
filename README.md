<div align="center">

  <img src="ui/icons/qinstall_logo.svg" alt="qInstall Logo" width="108" height="108" />

  <h1><code>qInstall</code></h1>

  <p>
    <b>Instalador por lotes 100% portable para Pendrives USB y Discos Externos en Windows 10 / 11</b><br />
    <sub>Alternativa moderna, visual y online/offline a Ninite · Pensada para post-formateo, soporte técnico y despliegue rápido</sub>
  </p>

  <p>
    <img src="https://img.shields.io/badge/Plataforma-Windows%2010%20%7C%2011%20(x64)-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Windows 10/11" />
    <img src="https://img.shields.io/badge/Modo-100%25%20Portable%20USB%20%2F%20HDD-8B5CF6?style=for-the-badge" alt="100% Portable" />
    <img src="https://img.shields.io/badge/Cat%C3%A1logo-176%20Apps%20%C2%B7%2014%20Categor%C3%ADas-10B981?style=for-the-badge" alt="176 Apps" />
    <img src="https://img.shields.io/badge/Motor-.NET%204.8%20%2B%20WebView2-06B6D4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 4.8 + WebView2" />
  </p>

  <p>
    <img src="ui/icons/chrome.png" width="26" />&nbsp;
    <img src="ui/icons/firefox.png" width="26" />&nbsp;
    <img src="ui/icons/vscode.png" width="26" />&nbsp;
    <img src="ui/icons/jetbrains_toolbox.png" width="26" />&nbsp;
    <img src="ui/icons/intellij.png" width="26" />&nbsp;
    <img src="ui/icons/pycharm.png" width="26" />&nbsp;
    <img src="ui/icons/chatgpt.png" width="26" />&nbsp;
    <img src="ui/icons/claude.png" width="26" />&nbsp;
    <img src="ui/icons/gemini.png" width="26" />&nbsp;
    <img src="ui/icons/nvidia_app.png" width="26" />&nbsp;
    <img src="ui/icons/amd_autodetect.png" width="26" />&nbsp;
    <img src="ui/icons/office.png" width="26" />&nbsp;
    <img src="ui/icons/discord.png" width="26" />&nbsp;
    <img src="ui/icons/steam.png" width="26" />&nbsp;
    <img src="ui/icons/vlc.png" width="26" />&nbsp;
    <img src="ui/icons/7zip.png" width="26" />
  </p>

</div>

---

## ✨ ¿Qué hace diferente a `qInstall`?

> [!IMPORTANT]
> **`qInstall` no es un programa fijo que se instala en la PC:** está diseñado exclusivamente para ejecutarse desde tu **pendrive USB o disco duro externo**, gestionando todo desde su propia interfaz gráfica sin scripts `.bat` externos ni archivos `.log` basura.

| Pilar | Cómo funciona |
| :--- | :--- |
| 💾 **Panel `Almacenamiento` integrado** | Copia `qInstall` limpio a cualquier pendrive o disco externo en 1 clic desde la cabecera, revisa el espacio libre en vivo y borra instaladores descargados uno por uno. |
| 🎒 **Perfiles portables (`profiles.json`)** | Tus perfiles de **Esenciales** (*Estándar, Gamer, Dev, Técnico, Oficina* o tu lista personalizada), tu tema y tu vista preferida viajan guardados en el USB junto a `Qinstall.exe`. |
| 🎨 **Interfaz WebView2 100% Offline** | **Modo Oscuro**, **Modo Claro** y **Sistema** sincronizado con la barra nativa de Windows, **125 íconos locales** (`< 500 KB`), **3 vistas** (*Galería*, *Cuadrícula*, *Tabla*) y barra **Nyan Cat** fluida a `60 fps`. |
| ⚡ **Híbrido Online + `DESCARGADO`** | Instala al vuelo la última versión vía **Winget / Microsoft Store / URL**, o pulsa **Descargar offline** para llevar los instaladores en `local\cache\` y usarlos en PCs **sin Internet**. |
| 🛡️ **Detección en vivo (cero logs)** | Escanea en tiempo real el Registro (`HKLM`/`HKCU`), `winget list`, Office Click-to-Run y DirectX para marcar lo que ya está `INSTALADO` y permitir **Desinstalar** con clic derecho o **Actualizar** todo el sistema. |
| 🪟 **Respaldo nativo automático** | Si abres el pendrive en un Windows recién instalado sin *WebView2 Runtime*, `qInstall` abre automáticamente su interfaz clásica nativa WinForms (`MainForm`). |

---

## 📏 Requisitos del Sistema y Reglas de Espacio

> [!WARNING]
> **Capacidad mínima obligatoria en el pendrive: `2 GB`** (tanto de tamaño total como de espacio libre). Si intentas instalar o copiar `qInstall` en una memoria menor a **2 GB**, el programa **bloqueará la operación automáticamente**. Se recomiendan **`4 GB o más`** (`16 GB+` si deseas llevar varios instaladores descargados offline).

- **Sistema Operativo:** Windows 10 (`1809+`) o Windows 11 (`64-bit`), ejecutado con permisos UAC de Administrador (solicitados automáticamente).
- **Sin dependencias externas:** Compilado sobre `.NET Framework 4.8` (incluido de fábrica en Windows 10/11) + librerías `WebView2` embebidas. Si el equipo no trae `Winget`, aparece un botón en la cabecera para instalarlo en un clic.
- **Restricción automática de suites gigantes en pendrive:** Programas que ocuparían varios gigabytes en caché local (*Microsoft Office 2016–2024, Visual Studio 2022, Android Studio, Docker Desktop, ComfyUI, LM Studio* e *IDEs pesados de JetBrains*) están **restringidos a instalación online** para proteger el espacio del USB (para Office viaja únicamente el lanzador ligero `office\setup.exe` de `~3.5 MB`).
- **Control de versiones obsoletas (`ACTUALIZAR`):** Cuando conectas tu pendrive en una PC con Internet, `qInstall` comprueba en paralelo (`4×` más rápido) si salió una versión más reciente de tus instaladores guardados en `local\cache\` y te pide **purgar o reemplazar** los antiguos para garantizar buen funcionamiento.

---

## 📦 Catálogo Incluido (`176 Apps` · `14 Categorías`)

### 🌐 Navegadores & 💬 Comunicación
| Categoría | Aplicaciones destacadas |
| :--- | :--- |
| **Navegadores** | <img src="ui/icons/chrome.png" width="16" align="absmiddle" /> Google Chrome · <img src="ui/icons/firefox.png" width="16" align="absmiddle" /> Mozilla Firefox · <img src="ui/icons/brave.png" width="16" align="absmiddle" /> Brave · <img src="ui/icons/operagx.png" width="16" align="absmiddle" /> Opera GX |
| **Comunicación** | <img src="ui/icons/discord.png" width="16" align="absmiddle" /> Discord · <img src="ui/icons/whatsapp.png" width="16" align="absmiddle" /> WhatsApp *(MS Store)* · <img src="ui/icons/telegram.png" width="16" align="absmiddle" /> Telegram Desktop · <img src="ui/icons/zoom.png" width="16" align="absmiddle" /> Zoom Workplace |

### 🛠️ Técnico, Drivers, Reparación & Optimización
| Categoría | Aplicaciones destacadas |
| :--- | :--- |
| **Soporte y Drivers** | <img src="ui/icons/restore_point.png" width="16" align="absmiddle" /> Punto de Restauración · <img src="ui/icons/nvcleanstall.png" width="16" align="absmiddle" /> NVCleanstall *(todas las familias NVIDIA)* · <img src="ui/icons/nvidia_app.png" width="16" align="absmiddle" /> NVIDIA App & PhysX · <img src="ui/icons/amd_autodetect.png" width="16" align="absmiddle" /> AMD Auto-Detect *(Radeon + Chipset)* · <img src="ui/icons/intel_dsa.png" width="16" align="absmiddle" /> Intel DSA · <img src="ui/icons/sdio.png" width="16" align="absmiddle" /> Snappy Driver Installer Origin *(SDIO)* · <img src="ui/icons/ddu.png" width="16" align="absmiddle" /> DDU · <img src="ui/icons/hwinfo.png" width="16" align="absmiddle" /> HWiNFO64 · <img src="ui/icons/gpuz.png" width="16" align="absmiddle" /> GPU-Z · <img src="ui/icons/msi_afterburner.png" width="16" align="absmiddle" /> MSI Afterburner · <img src="ui/icons/malwarebytes.png" width="16" align="absmiddle" /> Malwarebytes · <img src="ui/icons/anydesk.png" width="16" align="absmiddle" /> AnyDesk · <img src="ui/icons/teamviewer.png" width="16" align="absmiddle" /> TeamViewer · <img src="ui/icons/parsec.png" width="16" align="absmiddle" /> Parsec · <img src="ui/icons/bitwarden.png" width="16" align="absmiddle" /> Bitwarden · <img src="ui/icons/keepassxc.png" width="16" align="absmiddle" /> KeePassXC |
| **Utilidades** | <img src="ui/icons/7zip.png" width="16" align="absmiddle" /> 7-Zip · <img src="ui/icons/winrar.png" width="16" align="absmiddle" /> WinRAR · <img src="ui/icons/everything.png" width="16" align="absmiddle" /> Everything · <img src="ui/icons/powertoys.png" width="16" align="absmiddle" /> PowerToys · <img src="ui/icons/rufus.png" width="16" align="absmiddle" /> Rufus · <img src="ui/icons/crystaldisk.png" width="16" align="absmiddle" /> CrystalDiskInfo · <img src="ui/icons/cpuz.png" width="16" align="absmiddle" /> CPU-Z · <img src="ui/icons/nzxt_cam.png" width="16" align="absmiddle" /> NZXT CAM · <img src="ui/icons/wiztree.png" width="16" align="absmiddle" /> WizTree · <img src="ui/icons/treesize.png" width="16" align="absmiddle" /> TreeSize · <img src="ui/icons/qbittorrent.png" width="16" align="absmiddle" /> qBittorrent · <img src="ui/icons/utorrent.png" width="16" align="absmiddle" /> uTorrent · <img src="ui/icons/msstore_install.png" width="16" align="absmiddle" /> Instalar/Reparar Microsoft Store |
| **Optimización** | <img src="ui/icons/winutil.png" width="16" align="absmiddle" /> Chris Titus WinUtil · <img src="ui/icons/revios_setup.png" width="16" align="absmiddle" /> ReviOS *(AME + Playbook)* · <img src="ui/icons/shutup10.png" width="16" align="absmiddle" /> O&O ShutUp10++ · <img src="ui/icons/winaero.png" width="16" align="absmiddle" /> Winaero Tweaker · <img src="ui/icons/bleachbit.png" width="16" align="absmiddle" /> BleachBit · <img src="ui/icons/bcuninstaller.png" width="16" align="absmiddle" /> BCUninstaller · <img src="ui/icons/revo.png" width="16" align="absmiddle" /> Revo Uninstaller · <img src="ui/icons/autoruns.png" width="16" align="absmiddle" /> Autoruns · <img src="ui/icons/processlasso.png" width="16" align="absmiddle" /> Process Lasso · <img src="ui/icons/windhawk.png" width="16" align="absmiddle" /> Windhawk |
| **Arranque y Reparación** | <img src="ui/icons/ventoy.png" width="16" align="absmiddle" /> Ventoy · <img src="ui/icons/diskgenius.png" width="16" align="absmiddle" /> DiskGenius · <img src="ui/icons/testdisk.png" width="16" align="absmiddle" /> TestDisk · <img src="ui/icons/aomei_partition.png" width="16" align="absmiddle" /> AOMEI Partition · <img src="ui/icons/minitool_partition.png" width="16" align="absmiddle" /> MiniTool Partition · <img src="ui/icons/efibooteditor.png" width="16" align="absmiddle" /> EFI Boot Editor · PartBoot · <img src="ui/icons/repair_dism_sfc.png" width="16" align="absmiddle" /> DISM + SFC · <img src="ui/icons/backup_bcd.png" width="16" align="absmiddle" /> Respaldo BCD |

### 🤖 IA, 💻 IDEs, JetBrains & Desarrollo
| Categoría | Aplicaciones destacadas |
| :--- | :--- |
| **IA y Agentes** | <img src="ui/icons/chatgpt.png" width="16" align="absmiddle" /> ChatGPT & Codex CLI · <img src="ui/icons/claude.png" width="16" align="absmiddle" /> Claude Desktop & Claude Code · <img src="ui/icons/gemini.png" width="16" align="absmiddle" /> Google Gemini & Gemini CLI · <img src="ui/icons/antigravity.png" width="16" align="absmiddle" /> Google Antigravity / IDE / CLI · <img src="ui/icons/opencode.png" width="16" align="absmiddle" /> OpenCode Desktop & CLI · <img src="ui/icons/copilot.png" width="16" align="absmiddle" /> GitHub Copilot & CLI · <img src="ui/icons/cursor.png" width="16" align="absmiddle" /> Cursor · <img src="ui/icons/windsurf.png" width="16" align="absmiddle" /> Windsurf · <img src="ui/icons/trae.png" width="16" align="absmiddle" /> Trae · <img src="ui/icons/perplexity_comet.png" width="16" align="absmiddle" /> Perplexity Comet · <img src="ui/icons/grammarly.png" width="16" align="absmiddle" /> Grammarly · <img src="ui/icons/otter_ai.png" width="16" align="absmiddle" /> Otter.ai · <img src="ui/icons/ollama.png" width="16" align="absmiddle" /> Ollama · <img src="ui/icons/lmstudio.png" width="16" align="absmiddle" /> LM Studio · <img src="ui/icons/jan_ai.png" width="16" align="absmiddle" /> Jan AI · <img src="ui/icons/cherry_studio.png" width="16" align="absmiddle" /> Cherry Studio · <img src="ui/icons/comfyui.png" width="16" align="absmiddle" /> ComfyUI |
| **IDEs y JetBrains** | <img src="ui/icons/jetbrains_toolbox.png" width="16" align="absmiddle" /> JetBrains Toolbox · <img src="ui/icons/intellij.png" width="16" align="absmiddle" /> IntelliJ IDEA *(Pro & Community)* · <img src="ui/icons/pycharm.png" width="16" align="absmiddle" /> PyCharm *(Pro & Community)* · <img src="ui/icons/webstorm.png" width="16" align="absmiddle" /> WebStorm · <img src="ui/icons/clion.png" width="16" align="absmiddle" /> CLion · <img src="ui/icons/rider.png" width="16" align="absmiddle" /> Rider · <img src="ui/icons/phpstorm.png" width="16" align="absmiddle" /> PhpStorm · <img src="ui/icons/datagrip.png" width="16" align="absmiddle" /> DataGrip · <img src="ui/icons/goland.png" width="16" align="absmiddle" /> GoLand · <img src="ui/icons/rustrover.png" width="16" align="absmiddle" /> RustRover · <img src="ui/icons/vs2022_community.png" width="16" align="absmiddle" /> Visual Studio 2022 · <img src="ui/icons/android_studio.png" width="16" align="absmiddle" /> Android Studio · <img src="ui/icons/eclipse.png" width="16" align="absmiddle" /> Eclipse *(Java & C/C++)* · <img src="ui/icons/netbeans.png" width="16" align="absmiddle" /> NetBeans · <img src="ui/icons/vscodium.png" width="16" align="absmiddle" /> VSCodium · <img src="ui/icons/sublime_text.png" width="16" align="absmiddle" /> Sublime Text 4 · <img src="ui/icons/zed.png" width="16" align="absmiddle" /> Zed · <img src="ui/icons/neovim.png" width="16" align="absmiddle" /> Neovim · <img src="ui/icons/devcpp.png" width="16" align="absmiddle" /> Dev-C++ `5.9.2` & Embarcadero `6.3` · <img src="ui/icons/codeblocks.png" width="16" align="absmiddle" /> Code::Blocks · <img src="ui/icons/arduino_ide.png" width="16" align="absmiddle" /> Arduino IDE 2 · <img src="ui/icons/godot.png" width="16" align="absmiddle" /> Godot · <img src="ui/icons/unity_hub.png" width="16" align="absmiddle" /> Unity Hub |
| **Desarrollo** | <img src="ui/icons/vscode.png" width="16" align="absmiddle" /> VS Code · <img src="ui/icons/github_desktop.png" width="16" align="absmiddle" /> GitHub Desktop · <img src="ui/icons/git.png" width="16" align="absmiddle" /> Git · <img src="ui/icons/notepadpp.png" width="16" align="absmiddle" /> Notepad++ · <img src="ui/icons/windows_terminal.png" width="16" align="absmiddle" /> Windows Terminal · <img src="ui/icons/python.png" width="16" align="absmiddle" /> Python 3.12 · <img src="ui/icons/nodejs.png" width="16" align="absmiddle" /> Node.js LTS · <img src="ui/icons/postman.png" width="16" align="absmiddle" /> Postman · <img src="ui/icons/dbeaver.png" width="16" align="absmiddle" /> DBeaver · <img src="ui/icons/docker_desktop.png" width="16" align="absmiddle" /> Docker Desktop |

### 📄 Oficina, 🎬 Multimedia, 🎮 Juegos, ⚙️ Runtimes & ☕ Java
| Categoría | Aplicaciones destacadas |
| :--- | :--- |
| **Oficina** | <img src="ui/icons/office.png" width="16" align="absmiddle" /> Microsoft Office `2024` / `2021` / `2019` / `2016` *(ODT)* · <img src="ui/icons/libreoffice.png" width="16" align="absmiddle" /> LibreOffice · <img src="ui/icons/sumatrapdf.png" width="16" align="absmiddle" /> SumatraPDF · <img src="ui/icons/adobereader.png" width="16" align="absmiddle" /> Acrobat Reader · <img src="ui/icons/pdfgear.png" width="16" align="absmiddle" /> PDFgear *(alternativa Acrobat Pro)* · <img src="ui/icons/obsidian.png" width="16" align="absmiddle" /> Obsidian · <img src="ui/icons/google_drive.png" width="16" align="absmiddle" /> Google Drive |
| **Multimedia** | <img src="ui/icons/vlc.png" width="16" align="absmiddle" /> VLC · <img src="ui/icons/spotify.png" width="16" align="absmiddle" /> Spotify · <img src="ui/icons/obs.png" width="16" align="absmiddle" /> OBS Studio · <img src="ui/icons/sharex.png" width="16" align="absmiddle" /> ShareX · <img src="ui/icons/handbrake.png" width="16" align="absmiddle" /> HandBrake · <img src="ui/icons/kdenlive.png" width="16" align="absmiddle" /> Kdenlive · <img src="ui/icons/shotcut.png" width="16" align="absmiddle" /> Shotcut · <img src="ui/icons/capcut.png" width="16" align="absmiddle" /> CapCut · <img src="ui/icons/adobe_cc.png" width="16" align="absmiddle" /> Adobe Creative Cloud · <img src="ui/icons/gimp.png" width="16" align="absmiddle" /> GIMP · <img src="ui/icons/paintnet.png" width="16" align="absmiddle" /> Paint.NET · <img src="ui/icons/inkscape.png" width="16" align="absmiddle" /> Inkscape *(Illustrator)* · <img src="ui/icons/krita.png" width="16" align="absmiddle" /> Krita *(Photoshop)* · <img src="ui/icons/blender.png" width="16" align="absmiddle" /> Blender *(3D/VFX)* · <img src="ui/icons/darktable.png" width="16" align="absmiddle" /> Darktable *(Lightroom)* · <img src="ui/icons/scribus.png" width="16" align="absmiddle" /> Scribus *(InDesign)* |
| **Juegos** | <img src="ui/icons/steam.png" width="16" align="absmiddle" /> Steam · <img src="ui/icons/epic.png" width="16" align="absmiddle" /> Epic Games Launcher |
| **Runtimes** | <img src="ui/icons/vcredist.png" width="16" align="absmiddle" /> Visual C++ AIO *(2005–2022)* + paquetes `x64`/`x86` individuales · <img src="ui/icons/directx.png" width="16" align="absmiddle" /> DirectX Runtime · <img src="ui/icons/dotnet.png" width="16" align="absmiddle" /> .NET Desktop Runtime `6` / `8` / `9` / `10` / `11` · <img src="ui/icons/xna4.png" width="16" align="absmiddle" /> XNA 4.0 · OpenAL · <img src="ui/icons/webview2.png" width="16" align="absmiddle" /> WebView2 Runtime |
| **Java** | <img src="ui/icons/java.png" width="16" align="absmiddle" /> Java 8 *(Oracle JRE)* · <img src="ui/icons/temurin.png" width="16" align="absmiddle" /> Eclipse Temurin JRE & JDK `11` / `17` / `21` LTS · <img src="ui/icons/java.png" width="16" align="absmiddle" /> Oracle JDK `25` LTS |

---

## 🤫 Parámetros Silenciosos Explicados

> [!TIP]
> Con **Instalación silenciosa** activada, los programas se instalan solos en segundo plano sin pantallas de *«Siguiente → Acepto → Instalar»*. En apps de **Winget** (`--silent`) y en **instaladores descargados offline** (`.yaml` automático) `qInstall` lo configura solo.

Cuando agregas un instalador propio con **`+ Local`**, puedes elegir una plantilla rápida según el fabricante:

| Tipo de instalador | Parámetro recomendado | Ejemplo común |
| :--- | :--- | :--- |
| **NSIS** | `/S` | 7-Zip, VLC, Notepad++, Dev-C++ |
| **Inno Setup** | `/VERYSILENT /NORESTART` | Utilidades e instaladores independientes |
| **Windows Installer (`.msi`)** | `/qn /norestart` | *Automático si lo dejas en blanco* |
| **Microsoft / WiX / Burn** | `/quiet /norestart` | Runtimes e instaladores oficiales |
| **Instalación guiada normal** | *(Dejar vacío)* | Abre el asistente habitual del programa |

---

## ⌨️ Atajos y Controles Rápidos

- <kbd>/</kbd> — Enfocar instantáneamente la barra de búsqueda (catálogo local + Winget online).
- <kbd>Enter</kbd> *(en el buscador)* — Lanzar búsqueda directa en el repositorio online de Winget.
- <kbd>Esc</kbd> — Limpiar búsqueda o cerrar cualquier menú / ventana modal abierta.
- **Clic derecho sobre una app** — **Desinstalar** *(si ya está instalada)*, **Borrar descargado** *(si está en `local\cache\`)* o **Quitar del catálogo**.

---

## 🚀 Cómo llevarlo en tu Pendrive o Disco Externo

1. Abre **`Qinstall.exe`**.
2. Pulsa arriba a la derecha en **`Almacenamiento`**.
3. En **Copiar qInstall a Otra Unidad**, elige tu pendrive USB o disco externo (mínimo **2 GB**, recomendado **4 GB+**) y haz clic en **`Copiar a unidad`**.

<details>
<summary><b>📂 Ver estructura portable que se copia en tu unidad</b></summary>

```text
Qinstall/
├── Qinstall.exe                        # Ejecutable principal (x64, UAC Admin, Per-Monitor DPI)
├── Microsoft.Web.WebView2.Core.dll     # Motor WebView2 embebido
├── Microsoft.Web.WebView2.WinForms.dll
├── WebView2Loader.dll
├── apps.json                           # Catálogo editable de 163 aplicaciones
├── profiles.json                       # Tus perfiles y ajustes portables
├── ui/                                 # Interfaz 100% offline + 125 íconos en ui/icons/
├── office/                             # Microsoft Office Deployment Tool (setup.exe + XMLs 2016-2024)
└── local/                              # Tus instaladores .exe/.msi + caché offline (local/cache/)
```

</details>

<details>
<summary><b>🧩 Formato de <code>apps.json</code> y compilación QA (Desarrollo)</b></summary>

### Esquema de una aplicación en `apps.json`
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
- `Source`: `"winget"` (ID Winget o MS Store), `"url"` (enlace `.exe`/`.msi`), `"local"` (ruta relativa al USB) o `"powershell"` (herramienta elevada).

### Verificación QA y actualización automática de `Qinstall.exe`
No requiere instalar Visual Studio ni SDKs externos; usa el compilador C# nativo de `.NET Framework 4.8` incluido en Windows y ejecuta las 14 pruebas automatizadas con:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\.agents\skills\thorough-code-testing\scripts\verify_qinstall.ps1" -UpdateExe
```

</details>
