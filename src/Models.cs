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

        public static string GetCatalogPath()
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
            return Path.Combine(baseDir, "apps.json");
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
                Make("treesize", "TreeSize Free", "Utilidades", "Analiza el espacio ocupado en disco", "winget", "JAMSoftware.TreeSize.Free", false),
                Make("wiztree", "WizTree", "Utilidades", "Analizador ultra rapido de espacio en disco", "winget", "AntibodySoftware.WizTree", false),

                // Optimizacion
                Make("winutil", "Windows Utility (Chris Titus)", "Optimizacion", "Herramienta interactiva WinUtil para optimizar y limpiar Windows", "powershell", "irm 'https://christitus.com/win' | iex", false),
                Make("revios_setup", "ReviOS (AME Wizard + Playbook)", "Optimizacion", "Descarga AME Wizard oficial, abre descarga del Playbook ReviOS y lo lanza", "powershell", "$dir = Join-Path $env:USERPROFILE 'Desktop\\ReviOS_Setup'; New-Item -ItemType Directory -Path $dir -Force | Out-Null; $zip = Join-Path $dir 'AME_Wizard.zip'; Write-Host 'Descargando AME Wizard oficial...' -ForegroundColor Cyan; Invoke-WebRequest 'https://download.ameliorated.io/AME%20Wizard%20Beta.zip' -OutFile $zip -UseBasicParsing; Expand-Archive -Path $zip -DestinationPath $dir -Force; Remove-Item $zip -Force; Write-Host 'Abriendo descarga oficial del Playbook ReviOS (revi.cc)...' -ForegroundColor Green; Start-Process 'https://revi.cc/revios/download'; $exe = Get-ChildItem -Path $dir -Filter '*AME*.exe' | Select-Object -First 1; if ($exe) { Start-Process $exe.FullName }; Write-Host ('Carpeta lista en: ' + $dir) -ForegroundColor Yellow; Read-Host 'Descarga el Playbook .apbx y arrastralo a AME Wizard. Presiona Enter para cerrar'", false),
                Make("revision_tool", "Revision Tool (ReviOS)", "Optimizacion", "Panel oficial de configuracion y ajustes de ReviOS (ultima version)", "url", "https://github.com/meetrevision/revision-tool/releases/latest/download/RevisionTool-Setup.exe", false, "/VERYSILENT /NORESTART"),
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

                // Multimedia
                Make("vlc", "VLC Media Player", "Multimedia", "Reproductor universal de video y audio", "winget", "VideoLAN.VLC", true),
                Make("spotify", "Spotify", "Multimedia", "Musica y podcasts en streaming", "winget", "Spotify.Spotify", false),
                Make("obs", "OBS Studio", "Multimedia", "Grabacion de pantalla y streaming", "winget", "OBSProject.OBSStudio", false),
                Make("sharex", "ShareX", "Multimedia", "Capturas de pantalla y GIFs avanzados", "winget", "ShareX.ShareX", false),
                Make("gimp", "GIMP", "Multimedia", "Editor de imagenes gratuito", "winget", "GIMP.GIMP.3", false),
                Make("paintnet", "Paint.NET", "Multimedia", "Editor de imagenes rapido y ligero", "winget", "dotPDN.PaintDotNet", false),

                // Comunicacion
                Make("discord", "Discord", "Comunicacion", "Chat de voz, video y comunidades", "winget", "Discord.Discord", false),
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

                // IA y Agentes
                Make("gemini", "Google Gemini", "IA y Agentes", "Aplicacion de escritorio de Google Gemini", "winget", "Google.GeminiDesktop", false),
                Make("antigravity", "Google Antigravity", "IA y Agentes", "Plataforma y agente Google Antigravity", "winget", "Google.Antigravity", false),
                Make("antigravity_ide", "Antigravity IDE", "IA y Agentes", "Entorno de desarrollo Antigravity IDE", "winget", "Google.AntigravityIDE", false),
                Make("antigravity_cli", "Antigravity CLI", "IA y Agentes", "Interfaz de comandos de Antigravity", "winget", "Google.AntigravityCLI", false),
                Make("opencode", "OpenCode Desktop", "IA y Agentes", "Aplicacion de escritorio OpenCode", "winget", "Anomaly.OpenCode.Desktop", false),
                Make("opencode_cli", "OpenCode CLI", "IA y Agentes", "Agente de programacion OpenCode en terminal", "winget", "Anomaly.OpenCode", false),
                Make("copilot_app", "GitHub Copilot", "IA y Agentes", "Aplicacion oficial de GitHub Copilot", "winget", "GitHub.CopilotApp", false),
                Make("copilot_cli", "GitHub Copilot CLI", "IA y Agentes", "GitHub Copilot en linea de comandos", "winget", "GitHub.Copilot", false),

                // Desarrollo
                Make("vscode", "Visual Studio Code", "Desarrollo", "Editor de codigo moderno de Microsoft", "winget", "Microsoft.VisualStudioCode", true),
                Make("github_desktop", "GitHub Desktop", "Desarrollo", "Cliente grafico oficial de GitHub", "winget", "GitHub.GitHubDesktop", true),
                Make("git", "Git", "Desarrollo", "Control de versiones distribuido", "winget", "Git.Git", true),
                Make("notepadpp", "Notepad++", "Desarrollo", "Editor de texto ligero y rapido", "winget", "Notepad++.Notepad++", true),
                Make("python", "Python 3.12", "Desarrollo", "Lenguaje de programacion Python", "winget", "Python.Python.3.12", false),
                Make("nodejs", "Node.js LTS", "Desarrollo", "Entorno de ejecucion JavaScript", "winget", "OpenJS.NodeJS.LTS", false),

                // Juegos
                Make("steam", "Steam", "Juegos", "Plataforma principal de videojuegos PC", "winget", "Valve.Steam", false),
                Make("epic", "Epic Games Launcher", "Juegos", "Tienda y lanzador de Epic Games", "winget", "EpicGames.EpicGamesLauncher", false),

                // Runtimes y Java
                Make("directx", "DirectX Runtime", "Runtimes y Java", "Actualizador oficial DirectX End-User Runtime", "winget", "Microsoft.DirectX", true),
                Make("vcredist", "Visual C++ 2015-2022 x64", "Runtimes y Java", "Librerias esenciales para apps y juegos", "winget", "Microsoft.VCRedist.2015+.x64", true),
                Make("dotnet8", ".NET Desktop Runtime 8", "Runtimes y Java", "Runtime para aplicaciones modernas .NET", "winget", "Microsoft.DotNet.DesktopRuntime.8", false),
                Make("java8", "Java 8 (Oracle JRE)", "Runtimes y Java", "Java Runtime Environment 8 oficial", "winget", "Oracle.JavaRuntimeEnvironment", false),
                Make("java17", "Java 17 LTS (Temurin JDK)", "Runtimes y Java", "Eclipse Temurin JDK 17 LTS", "winget", "EclipseAdoptium.Temurin.17.JDK", false),
                Make("java21", "Java 21 LTS (Temurin JDK)", "Runtimes y Java", "Eclipse Temurin JDK 21 LTS", "winget", "EclipseAdoptium.Temurin.21.JDK", false)
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
