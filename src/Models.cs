using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Qinstall
{
    public class AppItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        /// <summary>
        /// "winget", "url", o "local"
        /// </summary>
        public string Source { get; set; }
        /// <summary>
        /// ID de Winget, URL directa (.exe/.msi), o ruta relativa al pendrive
        /// </summary>
        public string Target { get; set; }
        /// <summary>
        /// Argumentos de instalacion silenciosa (para url o local)
        /// </summary>
        public string SilentArgs { get; set; }
        public bool Essential { get; set; }

        public AppItem()
        {
            Id = string.Empty;
            Name = string.Empty;
            Category = "Otros";
            Description = string.Empty;
            Source = "winget";
            Target = string.Empty;
            SilentArgs = string.Empty;
            Essential = false;
        }
    }

    public class AppCatalog
    {
        public string Version { get; set; }
        public List<AppItem> Apps { get; set; }

        public AppCatalog()
        {
            Version = "1.0";
            Apps = new List<AppItem>();
        }

        public static string GetAppBaseDirectory()
        {
            string baseDir = null;
            try
            {
                string loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    baseDir = Path.GetDirectoryName(loc);
                }
            }
            catch
            {
            }
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = AppDomain.CurrentDomain.BaseDirectory;
            }
            return baseDir;
        }

        public static string GetLocalInstallersDirectory()
        {
            return Path.Combine(GetAppBaseDirectory(), "local");
        }

        public static string GetCatalogPath()
        {
            return Path.Combine(GetAppBaseDirectory(), "apps.json");
        }

        public static string GetProfilesPath()
        {
            return Path.Combine(GetAppBaseDirectory(), "profiles.json");
        }

        public static List<AppItem> DiscoverFolderLocalInstallers(AppCatalog existingCatalog)
        {
            List<AppItem> discovered = new List<AppItem>();
            string localDir = GetLocalInstallersDirectory();
            if (!Directory.Exists(localDir)) return discovered;

            HashSet<string> trackedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingCatalog != null && existingCatalog.Apps != null)
            {
                for (int i = 0; i < existingCatalog.Apps.Count; i++)
                {
                    AppItem a = existingCatalog.Apps[i];
                    if (string.Equals(a.Source, "local", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(a.Target))
                    {
                        trackedTargets.Add(a.Target.Trim());
                        trackedTargets.Add(Path.GetFileName(a.Target.Trim()));
                    }
                }
            }

            try
            {
                string[] files = Directory.GetFiles(localDir);
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                    if (ext != ".exe" && ext != ".msi") continue;

                    string fileName = Path.GetFileName(files[i]);
                    string relTarget = Path.Combine("local", fileName);
                    if (trackedTargets.Contains(relTarget) || trackedTargets.Contains(fileName)) continue;

                    AppItem item = new AppItem();
                    item.Id = "local_" + Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant().Replace(" ", "_");
                    item.Name = Path.GetFileNameWithoutExtension(fileName);
                    item.Category = "Instaladores Locales";
                    item.Description = "Instalador offline detectado automaticamente en carpeta local\\";
                    item.Source = "local";
                    item.Target = relTarget;
                    item.SilentArgs = string.Empty;
                    item.Essential = false;
                    discovered.Add(item);
                }
            }
            catch
            {
            }

            return discovered;
        }

        public static AppCatalog LoadOrCreateDefault()
        {
            string path = GetCatalogPath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    AppCatalog loaded = serializer.Deserialize<AppCatalog>(json);
                    if (loaded != null && loaded.Apps != null && loaded.Apps.Count > 0)
                    {
                        return loaded;
                    }
                }
                catch
                {
                    // Si el archivo esta corrupto, crear respaldo y regenerar
                    try
                    {
                        string backup = path + ".bak";
                        File.Copy(path, backup, true);
                    }
                    catch
                    {
                    }
                }
            }

            AppCatalog def = CreateDefault();
            try
            {
                Save(def);
            }
            catch
            {
                // Ignorar si la carpeta es de solo lectura
            }
            return def;
        }

        public static void Save(AppCatalog catalog)
        {
            string path = GetCatalogPath();
            string formatted = FormatCatalogJson(catalog);
            File.WriteAllText(path, formatted, new UTF8Encoding(false));
        }

        private static string FormatCatalogJson(AppCatalog catalog)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"Version\": " + JsonEscape(catalog.Version ?? "1.0") + ",");
            sb.AppendLine("  \"Apps\": [");

            for (int i = 0; i < catalog.Apps.Count; i++)
            {
                AppItem app = catalog.Apps[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"Id\": " + JsonEscape(app.Id) + ",");
                sb.AppendLine("      \"Name\": " + JsonEscape(app.Name) + ",");
                sb.AppendLine("      \"Category\": " + JsonEscape(app.Category) + ",");
                sb.AppendLine("      \"Description\": " + JsonEscape(app.Description) + ",");
                sb.AppendLine("      \"Source\": " + JsonEscape(app.Source) + ",");
                sb.AppendLine("      \"Target\": " + JsonEscape(app.Target) + ",");
                sb.AppendLine("      \"SilentArgs\": " + JsonEscape(app.SilentArgs) + ",");
                sb.AppendLine("      \"Essential\": " + (app.Essential ? "true" : "false"));
                sb.Append("    }");
                if (i < catalog.Apps.Count - 1)
                {
                    sb.Append(",");
                }
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string JsonEscape(string value)
        {
            if (value == null) value = string.Empty;
            StringBuilder sb = new StringBuilder("\"");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        public static AppCatalog CreateDefault()
        {
            AppCatalog cat = new AppCatalog();
            cat.Version = "1.0";
            cat.Apps = new List<AppItem>
            {
                // Navegadores
                Make("chrome", "Google Chrome", "Navegadores", "Navegador rapido de Google", "winget", "Google.Chrome", true),
                Make("firefox", "Mozilla Firefox", "Navegadores", "Navegador libre y enfocado en privacidad", "winget", "Mozilla.Firefox", false),
                Make("brave", "Brave Browser", "Navegadores", "Navegador con bloqueador integrado", "winget", "Brave.Brave", false),
                Make("operagx", "Opera GX", "Navegadores", "Navegador disenado para gaming", "winget", "Opera.OperaGX", false),

                // Utilidades
                Make("7zip", "7-Zip", "Utilidades", "Compresor de archivos ligero y libre", "winget", "7zip.7zip", true),
                Make("winrar", "WinRAR", "Utilidades", "Compresor clasico RAR y ZIP", "winget", "RARLab.WinRAR", false),
                Make("utorrent", "uTorrent Classic", "Utilidades", "Cliente BitTorrent clasico (descarga oficial)", "url", "https://download-new.utorrent.com/endpoint/utorrent/os/windows/track/stable/", false),
                Make("qbittorrent", "qBittorrent", "Utilidades", "Cliente BitTorrent libre y sin anuncios", "winget", "qBittorrent.qBittorrent", false),
                Make("everything", "Everything", "Utilidades", "Buscador instantaneo de archivos", "winget", "voidtools.Everything", true),
                Make("powertoys", "Microsoft PowerToys", "Utilidades", "Suite oficial de utilidades para Windows", "winget", "Microsoft.PowerToys", false),
                Make("rufus", "Rufus", "Utilidades", "Crea memorias USB booteables", "winget", "Rufus.Rufus", false),
                Make("crystaldisk", "CrystalDiskInfo", "Utilidades", "Diagnostico de salud de discos SSD/HDD", "winget", "CrystalDewWorld.CrystalDiskInfo", false),
                Make("cpuz", "CPU-Z", "Utilidades", "Informacion detallada del hardware", "winget", "CPUID.CPU-Z", false),
                Make("nzxt_cam", "NZXT CAM", "Utilidades", "Monitoreo de PC, temperaturas, ventiladores y control RGB NZXT", "winget", "NZXT.CAM", false),
                Make("treesize", "TreeSize Free", "Utilidades", "Analiza el espacio ocupado en disco", "winget", "JAMSoftware.TreeSize.Free", false),
                Make("wiztree", "WizTree", "Utilidades", "Analizador ultra rapido de espacio en disco", "winget", "AntibodySoftware.WizTree", false),
                Make("msstore_install", "Microsoft Store (Instalar/Reparar)", "Utilidades", "Instala o restaura la tienda oficial Microsoft Store (wsreset -i)", "powershell", "Write-Host '=== Instalando / Reparando Microsoft Store ===' -ForegroundColor Cyan; wsreset -i; Get-AppxPackage -AllUsers Microsoft.WindowsStore | ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register ($_.InstallLocation + '\\AppXManifest.xml') -ErrorAction SilentlyContinue }; Write-Host 'Solicitud enviada a Windows. Microsoft Store aparecera en unos momentos.' -ForegroundColor Green; Read-Host 'Presiona Enter para cerrar'", false),

                // Optimizacion
                Make("winutil", "Windows Utility (Chris Titus)", "Optimizacion", "Herramienta interactiva WinUtil para optimizar y limpiar Windows", "powershell", "irm 'https://christitus.com/win' | iex", false),
                Make("revios_setup", "ReviOS (AME Wizard + Playbook)", "Optimizacion", "Descarga AME Wizard oficial, abre descarga del Playbook ReviOS y lo lanza", "powershell", "$dir = Join-Path $env:USERPROFILE 'Desktop\\ReviOS_Setup'; New-Item -ItemType Directory -Path $dir -Force | Out-Null; $zip = Join-Path $dir 'AME_Wizard.zip'; Write-Host 'Descargando AME Wizard oficial...' -ForegroundColor Cyan; Invoke-WebRequest 'https://download.ameliorated.io/AME%20Wizard%20Beta.zip' -OutFile $zip -UseBasicParsing; Expand-Archive -Path $zip -DestinationPath $dir -Force; Remove-Item $zip -Force; Write-Host 'Abriendo descarga oficial del Playbook ReviOS (revi.cc)...' -ForegroundColor Green; Start-Process 'https://revi.cc/revios/download'; $exe = Get-ChildItem -Path $dir -Filter '*AME*.exe' | Select-Object -First 1; if ($exe) { Start-Process $exe.FullName }; Write-Host ('Carpeta lista en: ' + $dir) -ForegroundColor Yellow; Read-Host 'Descarga el Playbook .apbx y arrastralo a AME Wizard. Presiona Enter para cerrar'", false),
                Make("shutup10", "O&O ShutUp10++", "Optimizacion", "Desactiva telemetria y mejora privacidad en Windows", "winget", "OO-Software.ShutUp10", false),
                Make("winaero", "Winaero Tweaker", "Optimizacion", "Ajustes avanzados y optimizacion de Windows", "winget", "winaero.tweaker", false),
                Make("bleachbit", "BleachBit", "Optimizacion", "Limpiador ligero de archivos temporales y cache", "winget", "BleachBit.BleachBit", false),
                Make("bcuninstaller", "Bulk Crap Uninstaller", "Optimizacion", "Desinstalador masivo y limpieza profunda de restos", "winget", "Klocman.BulkCrapUninstaller", false),
                Make("revo", "Revo Uninstaller", "Optimizacion", "Desinstalador avanzado con limpieza de registro", "winget", "RevoUninstaller.RevoUninstaller", false),
                Make("autoruns", "Autoruns (Sysinternals)", "Optimizacion", "Control total de programas y servicios al arranque", "winget", "Microsoft.Sysinternals.Autoruns", false),
                Make("processlasso", "Process Lasso", "Optimizacion", "Optimizador de CPU y prioridades de procesos", "winget", "BitSum.ProcessLasso", false),
                Make("windhawk", "Windhawk", "Optimizacion", "Personalizacion y mejoras modulares para Windows", "winget", "RamenSoftware.Windhawk", false),

                // Arranque y Reparacion
                Make("efibooteditor", "EFI Boot Editor", "Arranque y Reparacion", "Editor y reparador de entradas de arranque UEFI/EFI", "winget", "EFIBootEditor.EFIBootEditor", false),
                Make("partboot", "PartBoot", "Arranque y Reparacion", "Gestor y reparador de arranque UEFI y particiones", "winget", "Haxllo.PartBoot", false),
                Make("diskgenius", "DiskGenius", "Arranque y Reparacion", "Reparacion de MBR/GPT, particiones y recuperacion de datos", "winget", "Eassos.DiskGenius", false),
                Make("testdisk", "TestDisk", "Arranque y Reparacion", "Recuperacion de particiones perdidas y sector de arranque", "winget", "CGSecurity.TestDisk", false),
                Make("aomei_partition", "AOMEI Partition Assistant", "Arranque y Reparacion", "Gestion de discos, conversion MBR/GPT y reparacion", "winget", "AOMEI.PartitionAssistant", false),
                Make("minitool_partition", "MiniTool Partition Wizard", "Arranque y Reparacion", "Gestion y reparacion de particiones y discos", "winget", "MiniTool.PartitionWizard.Free", false),
                Make("ventoy", "Ventoy", "Arranque y Reparacion", "Crea USB multiboot con ISOs de rescate y Windows", "winget", "Ventoy.Ventoy", false),
                Make("repair_dism_sfc", "Reparar Windows (DISM + SFC)", "Arranque y Reparacion", "Repara archivos del sistema corruptos con DISM y SFC", "powershell", "Write-Host '=== Reparando imagen de Windows (DISM) ===' -ForegroundColor Cyan; DISM /Online /Cleanup-Image /RestoreHealth; Write-Host '=== Verificando archivos del sistema (SFC) ===' -ForegroundColor Cyan; sfc /scannow; Read-Host 'Presiona Enter para cerrar'", false),
                Make("backup_bcd", "Ver/Respaldar Arranque (BCD)", "Arranque y Reparacion", "Lista entradas BCD y guarda respaldo automatico en Escritorio", "powershell", "Write-Host '=== Entradas de arranque actuales (BCDEdit) ===' -ForegroundColor Cyan; bcdedit /enum all; $bak = Join-Path $env:USERPROFILE ('Desktop\\BCD_Backup_' + (Get-Date -Format 'yyyyMMdd_HHmmss')); bcdedit /export $bak; Write-Host ('Respaldo BCD guardado en: ' + $bak) -ForegroundColor Green; Read-Host 'Presiona Enter para cerrar'", false),

                // Soporte y Drivers
                Make("restore_point", "Crear Punto de Restauracion", "Soporte y Drivers", "Crea un punto de restauracion en Windows antes de optimizar o instalar drivers", "powershell", "Write-Host '=== Creando Punto de Restauracion (Qinstall) ===' -ForegroundColor Cyan; Enable-ComputerRestore -Drive ($env:SystemDrive + '\\') -ErrorAction SilentlyContinue; Checkpoint-Computer -Description ('Qinstall_' + (Get-Date -Format 'yyyyMMdd_HHmm')) -RestorePointType 'MODIFY_SETTINGS'; Write-Host 'Punto de restauracion creado correctamente.' -ForegroundColor Green; Read-Host 'Presiona Enter para cerrar'", false),
                Make("hwinfo", "HWiNFO64", "Soporte y Drivers", "Diagnostico avanzado de hardware, temperaturas y sensores en tiempo real", "winget", "REALiX.HWiNFO", false),
                Make("gpuz", "GPU-Z", "Soporte y Drivers", "Informacion detallada y sensores de la tarjeta grafica (NVIDIA/AMD/Intel)", "winget", "TechPowerUp.GPU-Z", false),
                Make("ddu", "DDU (Display Driver Uninstaller)", "Soporte y Drivers", "Desinstalador limpio y profundo de drivers de video NVIDIA, AMD e Intel", "winget", "Wagnardsoft.DisplayDriverUninstaller", false),
                Make("nvcleanstall", "NVCleanstall (Drivers NVIDIA)", "Soporte y Drivers", "Descarga e instala drivers oficiales NVIDIA Game Ready / Studio limpios sin bloatware", "winget", "TechPowerUp.NVCleanstall", false),
                Make("nvidia_app", "NVIDIA App (Drivers GeForce)", "Soporte y Drivers", "Aplicacion oficial de NVIDIA para gestionar y actualizar drivers GeForce", "url", "https://us.download.nvidia.com/nvapp/client/11.0.3.218/NVIDIA_app_v11.0.3.218.exe", false, "-s -noreboot"),
                Make("nvidia_physx", "NVIDIA PhysX System Software", "Soporte y Drivers", "Driver y motor fisico oficial NVIDIA PhysX para juegos", "winget", "Nvidia.PhysX", false),
                Make("amd_autodetect", "AMD Drivers Auto-Detect (Radeon + Chipset)", "Soporte y Drivers", "Detecta tu GPU/CPU AMD en este equipo y abre el instalador oficial AMD Software Adrenalin / Chipset", "powershell", "Write-Host '=== Hardware AMD detectado en este equipo ===' -ForegroundColor Cyan; Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion | Format-Table -AutoSize; Get-CimInstance Win32_Processor | Select-Object Name | Format-Table -AutoSize; Write-Host 'Abriendo portal oficial de AMD Software Auto-Detect & Install...' -ForegroundColor Green; Start-Process 'https://www.amd.com/en/support/download/drivers.html'; Write-Host 'Nota: RDNA (RX 5000/6000/7000/9000 y Ryzen) usa Adrenalin actual; Polaris/Vega (RX 400/500) o GCN antiguas usan rama legacy o SDIO.' -ForegroundColor Yellow; Read-Host 'Presiona Enter para cerrar'", false),
                Make("sdio", "Snappy Driver Installer Origin (SDIO)", "Soporte y Drivers", "Instalador universal portable de drivers para cualquier PC (Red, Audio, Chipset, GPUs antiguas y modernas)", "winget", "GlennDelahoy.SnappyDriverInstallerOrigin", false),
                Make("msi_afterburner", "MSI Afterburner", "Soporte y Drivers", "Control de ventiladores, monitor OSD en juegos y ajuste de GPU", "winget", "Guru3D.Afterburner", false),
                Make("intel_dsa", "Intel Driver & Support Assistant", "Soporte y Drivers", "Detecta y actualiza automaticamente drivers oficiales de Intel", "winget", "Intel.IntelDriverAndSupportAssistant", false),
                Make("malwarebytes", "Malwarebytes", "Soporte y Drivers", "Escaner antimalware y limpieza profunda de amenazas en Windows", "winget", "Malwarebytes.Malwarebytes", false),
                Make("anydesk", "AnyDesk", "Soporte y Drivers", "Escritorio remoto ligero y rapido para soporte tecnico", "winget", "AnyDesk.AnyDesk", false),
                Make("teamviewer", "TeamViewer", "Soporte y Drivers", "Asistencia y control remoto multiplataforma", "winget", "TeamViewer.TeamViewer", false),
                Make("parsec", "Parsec", "Soporte y Drivers", "Acceso remoto de baja latencia y alta fluidez", "winget", "Parsec.Parsec", false),
                Make("bitwarden", "Bitwarden", "Soporte y Drivers", "Gestor de contraseñas seguro y de codigo abierto", "winget", "Bitwarden.Bitwarden", false),
                Make("keepassxc", "KeePassXC", "Soporte y Drivers", "Gestor de contraseñas 100% offline y cifrado local", "winget", "KeePassXCTeam.KeePassXC", false),

                // Multimedia
                Make("vlc", "VLC Media Player", "Multimedia", "Reproductor universal de video y audio", "winget", "VideoLAN.VLC", true),
                Make("spotify", "Spotify", "Multimedia", "Musica y podcasts en streaming", "winget", "Spotify.Spotify", false),
                Make("obs", "OBS Studio", "Multimedia", "Grabacion de pantalla y streaming", "winget", "OBSProject.OBSStudio", false),
                Make("sharex", "ShareX", "Multimedia", "Capturas de pantalla y GIFs avanzados", "winget", "ShareX.ShareX", false),
                Make("gimp", "GIMP", "Multimedia", "Editor de imagenes gratuito", "winget", "GIMP.GIMP.3", false),
                Make("paintnet", "Paint.NET", "Multimedia", "Editor de imagenes rapido y ligero", "winget", "dotPDN.PaintDotNet", false),
                Make("handbrake", "HandBrake", "Multimedia", "Conversor y compresor de video open-source (MP4, MKV, H.264/H.265/AV1)", "winget", "HandBrake.HandBrake", false),
                Make("kdenlive", "Kdenlive", "Multimedia", "Editor de video multipista profesional gratuito (alternativa a Premiere Pro)", "winget", "KDE.Kdenlive", false),
                Make("shotcut", "Shotcut", "Multimedia", "Editor de video ligero, rapido y open-source", "winget", "Meltytech.Shotcut", false),
                Make("capcut", "CapCut", "Multimedia", "Editor de video moderno con plantillas, subtitulos automaticos y efectos", "winget", "ByteDance.CapCut", false),
                Make("adobe_cc", "Adobe Creative Cloud", "Multimedia", "Gestor oficial de Adobe (Photoshop, Premiere, Illustrator, After Effects)", "winget", "Adobe.CreativeCloud", false),
                Make("inkscape", "Inkscape", "Multimedia", "Diseno vectorial SVG gratuito (alternativa libre a Adobe Illustrator)", "winget", "Inkscape.Inkscape", false),
                Make("krita", "Krita", "Multimedia", "Ilustracion digital y edicion avanzada gratuita (alternativa libre a Photoshop)", "winget", "KDE.Krita", false),
                Make("blender", "Blender", "Multimedia", "Suite profesional 3D, animacion, VFX y edicion de video open-source", "winget", "BlenderFoundation.Blender", false),
                Make("darktable", "Darktable", "Multimedia", "Revelado fotografico RAW gratuito (alternativa libre a Adobe Lightroom)", "winget", "darktable.darktable", false),
                Make("scribus", "Scribus", "Multimedia", "Maquetacion editorial gratuita (alternativa libre a Adobe InDesign)", "winget", "Scribus.Scribus", false),

                // Comunicacion
                Make("discord", "Discord", "Comunicacion", "Chat de voz, video y comunidades", "winget", "Discord.Discord", false),
                Make("whatsapp", "WhatsApp", "Comunicacion", "Aplicacion oficial de WhatsApp (Microsoft Store)", "winget", "9NKSQGP7F2NH", false),
                Make("telegram", "Telegram Desktop", "Comunicacion", "Mensajeria rapida en la nube", "winget", "Telegram.TelegramDesktop", false),
                Make("zoom", "Zoom Workplace", "Comunicacion", "Videollamadas y reuniones online", "winget", "Zoom.Zoom", false),

                // Oficina y Documentos
                Make("office2024", "Microsoft Office 2024", "Oficina", "Office 2024 ProPlus (64-bit ES) integrado en Qinstall", "local", "office\\setup.exe", false, "/configure office2024.xml"),
                Make("office2021", "Microsoft Office 2021", "Oficina", "Office 2021 ProPlus (64-bit ES) integrado en Qinstall", "local", "office\\setup.exe", false, "/configure office2021.xml"),
                Make("office2019", "Microsoft Office 2019", "Oficina", "Office 2019 ProPlus (64-bit ES) integrado en Qinstall", "local", "office\\setup.exe", false, "/configure office2019.xml"),
                Make("office2016", "Microsoft Office 2016", "Oficina", "Office 2016 ProPlus (64-bit ES) integrado en Qinstall", "local", "office\\setup.exe", false, "/configure office2016.xml"),
                Make("libreoffice", "LibreOffice", "Oficina", "Suite ofimatica gratuita completa", "winget", "TheDocumentFoundation.LibreOffice", false),
                Make("sumatrapdf", "SumatraPDF", "Oficina", "Lector de PDF ultra rapido", "winget", "SumatraPDF.SumatraPDF", true),
                Make("adobereader", "Adobe Acrobat Reader", "Oficina", "Lector oficial de documentos PDF", "winget", "Adobe.Acrobat.Reader.64-bit", false),
                Make("obsidian", "Obsidian", "Oficina", "Notas personales en Markdown", "winget", "Obsidian.Obsidian", false),
                Make("google_drive", "Google Drive", "Oficina", "Sincronizacion y unidad en la nube oficial de Google Drive", "winget", "Google.GoogleDrive", false),
                Make("pdfgear", "PDFgear", "Oficina", "Editor, conversor y firmador de PDF gratuito (alternativa a Acrobat Pro)", "winget", "PDFgear.PDFgear", false),

                // IA y Agentes
                Make("chatgpt", "ChatGPT", "IA y Agentes", "Aplicacion oficial de escritorio de OpenAI ChatGPT", "winget", "OpenAI.ChatGPT", false),
                Make("codex_cli", "OpenAI Codex CLI", "IA y Agentes", "Agente de programacion OpenAI Codex en terminal", "winget", "OpenAI.Codex", false),
                Make("claude", "Claude Desktop", "IA y Agentes", "Aplicacion oficial de escritorio de Anthropic Claude", "winget", "Anthropic.Claude", false),
                Make("claude_code", "Claude Code", "IA y Agentes", "Agente de programacion Claude Code en terminal", "winget", "Anthropic.ClaudeCode", false),
                Make("gemini", "Google Gemini", "IA y Agentes", "Aplicacion de escritorio de Google Gemini", "winget", "Google.GeminiDesktop", false),
                Make("gemini_cli", "Gemini API CLI", "IA y Agentes", "Interfaz de linea de comandos oficial de Gemini", "winget", "Google.GeminiAPICLI", false),
                Make("antigravity", "Google Antigravity", "IA y Agentes", "Plataforma y agente Google Antigravity", "winget", "Google.Antigravity", false),
                Make("antigravity_ide", "Antigravity IDE", "IA y Agentes", "Entorno de desarrollo Antigravity IDE", "winget", "Google.AntigravityIDE", false),
                Make("antigravity_cli", "Antigravity CLI", "IA y Agentes", "Interfaz de comandos de Antigravity", "winget", "Google.AntigravityCLI", false),
                Make("opencode", "OpenCode Desktop", "IA y Agentes", "Aplicacion de escritorio OpenCode", "winget", "Anomaly.OpenCode.Desktop", false),
                Make("opencode_cli", "OpenCode CLI", "IA y Agentes", "Agente de programacion OpenCode en terminal", "winget", "Anomaly.OpenCode", false),
                Make("copilot_app", "GitHub Copilot", "IA y Agentes", "Aplicacion oficial de GitHub Copilot", "winget", "GitHub.CopilotApp", false),
                Make("copilot_cli", "GitHub Copilot CLI", "IA y Agentes", "GitHub Copilot en linea de comandos", "winget", "GitHub.Copilot", false),
                Make("cursor", "Cursor", "IA y Agentes", "Editor de codigo nativo con IA y agentes", "winget", "Anysphere.Cursor", false),
                Make("windsurf", "Windsurf (Devin)", "IA y Agentes", "IDE con agentes de Codeium / Cognition AI (sucesor oficial de Windsurf)", "winget", "CognitionAI.DevinDesktop", false),
                Make("trae", "Trae AI IDE", "IA y Agentes", "IDE adaptativo con agentes de IA", "winget", "ByteDance.Trae", false),
                Make("perplexity_comet", "Perplexity Comet", "IA y Agentes", "Navegador y asistente de investigacion con IA de Perplexity", "winget", "Perplexity.Comet", false),
                Make("ollama", "Ollama", "IA y Agentes", "Ejecuta modelos LLM locales (Llama, DeepSeek, Qwen, Gemma)", "winget", "Ollama.Ollama", false),
                Make("lmstudio", "LM Studio", "IA y Agentes", "App visual para descargar y correr modelos IA locales offline", "winget", "ElementLabs.LMStudio", false),
                Make("jan_ai", "Jan AI", "IA y Agentes", "Chat de IA local de codigo abierto 100% privado", "winget", "Jan.Jan", false),
                Make("cherry_studio", "Cherry Studio", "IA y Agentes", "Cliente multi-IA (ChatGPT, Gemini, Claude, DeepSeek, Ollama)", "winget", "kangfenmao.CherryStudio", false),
                Make("comfyui", "ComfyUI Desktop", "IA y Agentes", "Generacion local de imagenes y video con IA por nodos", "winget", "Comfy.ComfyUI-Desktop", false),
                Make("grammarly", "Grammarly for Windows", "IA y Agentes", "Asistente de escritura, correccion y redaccion con IA para Windows", "winget", "Grammarly.Grammarly", false),
                Make("otter_ai", "Otter.ai (Escritorio / Web)", "IA y Agentes", "Transcripcion y resumenes automaticos de reuniones con IA (descarga oficial)", "powershell", "Write-Host '=== Otter.ai para Windows ===' -ForegroundColor Cyan; Write-Host 'Abriendo pagina oficial de descarga de Otter.ai para Windows...' -ForegroundColor Green; Start-Process 'https://otter.ai/apps'; Read-Host 'Descarga el instalador oficial desde tu navegador. Presiona Enter para cerrar'", false),

                // IDEs y JetBrains
                Make("jetbrains_toolbox", "JetBrains Toolbox", "IDEs y JetBrains", "Gestor oficial para instalar y actualizar todos los IDEs de JetBrains", "winget", "JetBrains.Toolbox", false),
                Make("intellij", "IntelliJ IDEA", "IDEs y JetBrains", "IDE insignia de JetBrains para Java, Kotlin y desarrollo empresarial", "winget", "JetBrains.IntelliJIDEA", false),
                Make("intellij_community", "IntelliJ IDEA Community", "IDEs y JetBrains", "Edicion gratuita y open-source de IntelliJ IDEA para Java y Kotlin", "winget", "JetBrains.IntelliJIDEA.Community", false),
                Make("pycharm", "PyCharm", "IDEs y JetBrains", "IDE profesional de JetBrains para Python, web y ciencia de datos", "winget", "JetBrains.PyCharm", false),
                Make("pycharm_community", "PyCharm Community", "IDEs y JetBrains", "Edicion gratuita de PyCharm para desarrollo en Python", "winget", "JetBrains.PyCharm.Community", false),
                Make("webstorm", "WebStorm", "IDEs y JetBrains", "IDE de JetBrains especializado en JavaScript, TypeScript y web", "winget", "JetBrains.WebStorm", false),
                Make("clion", "CLion", "IDEs y JetBrains", "IDE multiplataforma de JetBrains para C y C++", "winget", "JetBrains.CLion", false),
                Make("rider", "JetBrains Rider", "IDEs y JetBrains", "IDE rapido de JetBrains para .NET, C#, Unity y Unreal", "winget", "JetBrains.Rider", false),
                Make("phpstorm", "PhpStorm", "IDEs y JetBrains", "IDE de JetBrains para PHP, Laravel, Symfony y desarrollo web", "winget", "JetBrains.PhpStorm", false),
                Make("datagrip", "DataGrip", "IDEs y JetBrains", "IDE de JetBrains para bases de datos SQL (MySQL, PostgreSQL, SQL Server)", "winget", "JetBrains.DataGrip", false),
                Make("goland", "GoLand", "IDEs y JetBrains", "IDE de JetBrains dedicado al lenguaje Go", "winget", "JetBrains.GoLand", false),
                Make("rustrover", "RustRover", "IDEs y JetBrains", "IDE dedicado de JetBrains para programacion en Rust", "winget", "JetBrains.RustRover", false),
                Make("vs2022_community", "Visual Studio 2022 Community", "IDEs y JetBrains", "IDE completo y gratuito de Microsoft para C#, C++, .NET y desktop", "winget", "Microsoft.VisualStudio.2022.Community", false),
                Make("android_studio", "Android Studio", "IDEs y JetBrains", "IDE oficial de Google para desarrollo de apps Android y Kotlin", "winget", "Google.AndroidStudio", false),
                Make("eclipse_jee", "Eclipse IDE (Java / Web)", "IDEs y JetBrains", "IDE clasico de Eclipse para Java empresarial y desarrollo web", "winget", "EclipseFoundation.Eclipse.JEE", false),
                Make("eclipse_cpp", "Eclipse IDE (C/C++)", "IDEs y JetBrains", "Edicion de Eclipse IDE especializada en C y C++", "winget", "EclipseFoundation.Eclipse.CPP", false),
                Make("netbeans", "Apache NetBeans", "IDEs y JetBrains", "IDE gratuito de Apache para Java, PHP, HTML5 y C/C++", "winget", "Apache.NetBeans", false),
                Make("vscodium", "VSCodium", "IDEs y JetBrains", "Compilacion libre de Visual Studio Code sin telemetria", "winget", "VSCodium.VSCodium", false),
                Make("sublime_text", "Sublime Text 4", "IDEs y JetBrains", "Editor de codigo ultra rapido y ligero con multiples cursores", "winget", "SublimeHQ.SublimeText.4", false),
                Make("zed", "Zed", "IDEs y JetBrains", "Editor de codigo moderno de alto rendimiento escrito en Rust", "winget", "ZedIndustries.Zed", false),
                Make("neovim", "Neovim", "IDEs y JetBrains", "Editor extensible basado en Vim para terminal y flujo rapido", "winget", "Neovim.Neovim", false),
                Make("devcpp_592", "Dev-C++ 5.9.2 (Orwell)", "IDEs y JetBrains", "Version clasica Dev-C++ 5.9.2 con compilador TDM-GCC 4.8.1 integrado", "url", "https://sourceforge.net/projects/orwelldevcpp/files/Setup%20Releases/Dev-Cpp%205.9.2%20TDM-GCC%204.8.1%20Setup.exe/download", false, "/S"),
                Make("devcpp", "Embarcadero Dev-C++", "IDEs y JetBrains", "IDE clasico y ligero para programar en C y C++", "winget", "Embarcadero.Dev-C++", false),
                Make("codeblocks", "Code::Blocks (MinGW)", "IDEs y JetBrains", "IDE gratuito para C/C++ con compilador MinGW integrado", "winget", "CodeBlocks.CodeBlocks.MinGW", false),
                Make("arduino_ide", "Arduino IDE 2", "IDEs y JetBrains", "Entorno oficial para programar placas Arduino y microcontroladores", "winget", "ArduinoSA.IDE.stable", false),
                Make("godot", "Godot Engine", "IDEs y JetBrains", "Motor e IDE open-source para creacion de videojuegos 2D y 3D", "winget", "GodotEngine.GodotEngine", false),
                Make("unity_hub", "Unity Hub", "IDEs y JetBrains", "Gestor oficial de versiones y proyectos del motor Unity", "winget", "Unity.UnityHub", false),

                // Desarrollo
                Make("vscode", "Visual Studio Code", "Desarrollo", "Editor de codigo moderno de Microsoft", "winget", "Microsoft.VisualStudioCode", true),
                Make("github_desktop", "GitHub Desktop", "Desarrollo", "Cliente grafico oficial de GitHub", "winget", "GitHub.GitHubDesktop", true),
                Make("git", "Git", "Desarrollo", "Control de versiones distribuido", "winget", "Git.Git", true),
                Make("notepadpp", "Notepad++", "Desarrollo", "Editor de texto ligero y rapido", "winget", "Notepad++.Notepad++", true),
                Make("windows_terminal", "Windows Terminal", "Desarrollo", "Terminal moderna con pestanas para PowerShell, CMD y WSL", "winget", "Microsoft.WindowsTerminal", false),
                Make("python", "Python 3.12", "Desarrollo", "Lenguaje de programacion Python", "winget", "Python.Python.3.12", false),
                Make("nodejs", "Node.js LTS", "Desarrollo", "Entorno de ejecucion JavaScript", "winget", "OpenJS.NodeJS.LTS", false),
                Make("postman", "Postman", "Desarrollo", "Plataforma para disenar, probar y documentar APIs REST y GraphQL", "winget", "Postman.Postman", false),
                Make("dbeaver", "DBeaver Community", "Desarrollo", "Cliente SQL universal gratuito para MySQL, PostgreSQL, SQLite y mas", "winget", "DBeaver.DBeaver.Community", false),
                Make("docker_desktop", "Docker Desktop", "Desarrollo", "Contenedores Docker y entornos de desarrollo reproducibles en Windows", "winget", "Docker.DockerDesktop", false),

                // Juegos
                Make("steam", "Steam", "Juegos", "Plataforma principal de videojuegos PC", "winget", "Valve.Steam", false),
                Make("epic", "Epic Games Launcher", "Juegos", "Tienda y lanzador de Epic Games", "winget", "EpicGames.EpicGamesLauncher", false),

                // Runtimes
                Make("vcredist_aio", "Visual C++ AIO (2005-2022)", "Runtimes", "Paquete todo-en-uno con todos los Visual C++ x64 y x86", "winget", "abbodi1406.vcredist", true),
                Make("directx", "DirectX Runtime", "Runtimes", "Actualizador oficial DirectX End-User Runtime", "winget", "Microsoft.DirectX", true),
                Make("vcredist", "Visual C++ 2015-2022 x64", "Runtimes", "Librerias esenciales Visual C++ v14 (64-bit)", "winget", "Microsoft.VCRedist.2015+.x64", true),
                Make("vcredist_x86", "Visual C++ 2015-2022 x86", "Runtimes", "Librerias esenciales Visual C++ v14 (32-bit)", "winget", "Microsoft.VCRedist.2015+.x86", true),
                Make("vcredist_2013_x64", "Visual C++ 2013 x64", "Runtimes", "Redistributable Visual C++ 2013 (64-bit)", "winget", "Microsoft.VCRedist.2013.x64", false),
                Make("vcredist_2013_x86", "Visual C++ 2013 x86", "Runtimes", "Redistributable Visual C++ 2013 (32-bit)", "winget", "Microsoft.VCRedist.2013.x86", false),
                Make("vcredist_2012_x64", "Visual C++ 2012 x64", "Runtimes", "Redistributable Visual C++ 2012 (64-bit)", "winget", "Microsoft.VCRedist.2012.x64", false),
                Make("vcredist_2012_x86", "Visual C++ 2012 x86", "Runtimes", "Redistributable Visual C++ 2012 (32-bit)", "winget", "Microsoft.VCRedist.2012.x86", false),
                Make("vcredist_2010_x64", "Visual C++ 2010 x64", "Runtimes", "Redistributable Visual C++ 2010 (64-bit)", "winget", "Microsoft.VCRedist.2010.x64", false),
                Make("vcredist_2010_x86", "Visual C++ 2010 x86", "Runtimes", "Redistributable Visual C++ 2010 (32-bit)", "winget", "Microsoft.VCRedist.2010.x86", false),
                Make("vcredist_2008_x64", "Visual C++ 2008 x64", "Runtimes", "Redistributable Visual C++ 2008 (64-bit)", "winget", "Microsoft.VCRedist.2008.x64", false),
                Make("vcredist_2008_x86", "Visual C++ 2008 x86", "Runtimes", "Redistributable Visual C++ 2008 (32-bit)", "winget", "Microsoft.VCRedist.2008.x86", false),
                Make("vcredist_2005_x64", "Visual C++ 2005 x64", "Runtimes", "Redistributable Visual C++ 2005 (64-bit)", "winget", "Microsoft.VCRedist.2005.x64", false),
                Make("vcredist_2005_x86", "Visual C++ 2005 x86", "Runtimes", "Redistributable Visual C++ 2005 (32-bit)", "winget", "Microsoft.VCRedist.2005.x86", false),
                Make("xna4", "XNA Framework 4.0", "Runtimes", "Runtime grafico Microsoft XNA para juegos clasicos", "winget", "Microsoft.XNARedist", false),
                Make("openal", "OpenAL Audio Runtime", "Runtimes", "Libreria de audio 3D requerida por varios juegos y apps", "winget", "CreativeTechnology.OpenAL", false),
                Make("webview2", "WebView2 Runtime", "Runtimes", "Runtime Microsoft Edge WebView2 para apps modernas", "winget", "Microsoft.EdgeWebView2Runtime", false),
                Make("dotnet6", ".NET Desktop Runtime 6", "Runtimes", "Runtime .NET 6 para aplicaciones de escritorio", "winget", "Microsoft.DotNet.DesktopRuntime.6", false),
                Make("dotnet8", ".NET Desktop Runtime 8 LTS", "Runtimes", "Runtime .NET 8 LTS para aplicaciones modernas", "winget", "Microsoft.DotNet.DesktopRuntime.8", false),
                Make("dotnet9", ".NET Desktop Runtime 9", "Runtimes", "Runtime .NET 9 para aplicaciones de escritorio", "winget", "Microsoft.DotNet.DesktopRuntime.9", false),
                Make("dotnet10", ".NET Desktop Runtime 10 LTS", "Runtimes", "Runtime .NET 10 LTS para aplicaciones de escritorio", "winget", "Microsoft.DotNet.DesktopRuntime.10", false),
                Make("dotnet11", ".NET Desktop Runtime 11", "Runtimes", "Runtime .NET 11 mas reciente", "winget", "Microsoft.DotNet.DesktopRuntime.11", false),

                // Java
                Make("java8", "Java 8 (Oracle JRE)", "Java", "Java Runtime Environment 8 oficial (apps clasicas y Minecraft)", "winget", "Oracle.JavaRuntimeEnvironment", false),
                Make("java11_jre", "Java 11 LTS (Temurin JRE)", "Java", "Eclipse Temurin JRE 11 LTS", "winget", "EclipseAdoptium.Temurin.11.JRE", false),
                Make("java11_jdk", "Java 11 LTS (Temurin JDK)", "Java", "Eclipse Temurin JDK 11 LTS para desarrollo", "winget", "EclipseAdoptium.Temurin.11.JDK", false),
                Make("java17_jre", "Java 17 LTS (Temurin JRE)", "Java", "Eclipse Temurin JRE 17 LTS", "winget", "EclipseAdoptium.Temurin.17.JRE", false),
                Make("java17", "Java 17 LTS (Temurin JDK)", "Java", "Eclipse Temurin JDK 17 LTS para desarrollo", "winget", "EclipseAdoptium.Temurin.17.JDK", false),
                Make("java21_jre", "Java 21 LTS (Temurin JRE)", "Java", "Eclipse Temurin JRE 21 LTS", "winget", "EclipseAdoptium.Temurin.21.JRE", false),
                Make("java21", "Java 21 LTS (Temurin JDK)", "Java", "Eclipse Temurin JDK 21 LTS para desarrollo", "winget", "EclipseAdoptium.Temurin.21.JDK", false),
                Make("java25", "Java 25 LTS (Oracle JDK)", "Java", "Oracle Java SE Development Kit 25 LTS", "winget", "Oracle.JDK.25", false)
            };
            return cat;
        }

        private static AppItem Make(string id, string name, string category, string desc, string source, string target, bool essential, string silentArgs = "")
        {
            AppItem item = new AppItem();
            item.Id = id;
            item.Name = name;
            item.Category = category;
            item.Description = desc;
            item.Source = source;
            item.Target = target;
            item.SilentArgs = silentArgs ?? string.Empty;
            item.Essential = essential;
            return item;
        }
    }

    public enum InstallStatus
    {
        Success,
        AlreadyInstalled,
        Failed,
        Cancelled
    }

    public class InstallResult
    {
        public AppItem App { get; set; }
        public InstallStatus Status { get; set; }
        public string Message { get; set; }
    }
}
