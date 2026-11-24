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
                Make("everything", "Everything", "Utilidades", "Buscador instantaneo de archivos", "winget", "voidtools.Everything", true),
                Make("powertoys", "Microsoft PowerToys", "Utilidades", "Suite oficial de utilidades para Windows", "winget", "Microsoft.PowerToys", false),
                Make("rufus", "Rufus", "Utilidades", "Crea memorias USB booteables", "winget", "Rufus.Rufus", false),
                Make("crystaldisk", "CrystalDiskInfo", "Utilidades", "Diagnostico de salud de discos SSD/HDD", "winget", "CrystalDewWorld.CrystalDiskInfo", false),
                Make("cpuz", "CPU-Z", "Utilidades", "Informacion detallada del hardware", "winget", "CPUID.CPU-Z", false),
                Make("treesize", "TreeSize Free", "Utilidades", "Analiza el espacio ocupado en disco", "winget", "JAMSoftware.TreeSize.Free", false),

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
                Make("libreoffice", "LibreOffice", "Oficina", "Suite ofimatica gratuita completa", "winget", "TheDocumentFoundation.LibreOffice", false),
                Make("sumatrapdf", "SumatraPDF", "Oficina", "Lector de PDF ultra rapido", "winget", "SumatraPDF.SumatraPDF", true),
                Make("adobereader", "Adobe Acrobat Reader", "Oficina", "Lector oficial de documentos PDF", "winget", "Adobe.Acrobat.Reader.64-bit", false),
                Make("obsidian", "Obsidian", "Oficina", "Notas personales en Markdown", "winget", "Obsidian.Obsidian", false),

                // Desarrollo
                Make("vscode", "Visual Studio Code", "Desarrollo", "Editor de codigo moderno de Microsoft", "winget", "Microsoft.VisualStudioCode", true),
                Make("git", "Git", "Desarrollo", "Control de versiones distribuido", "winget", "Git.Git", true),
                Make("notepadpp", "Notepad++", "Desarrollo", "Editor de texto ligero y rapido", "winget", "Notepad++.Notepad++", true),
                Make("python", "Python 3.12", "Desarrollo", "Lenguaje de programacion Python", "winget", "Python.Python.3.12", false),
                Make("nodejs", "Node.js LTS", "Desarrollo", "Entorno de ejecucion JavaScript", "winget", "OpenJS.NodeJS.LTS", false),

                // Juegos y Runtimes
                Make("steam", "Steam", "Juegos", "Plataforma principal de videojuegos PC", "winget", "Valve.Steam", false),
                Make("epic", "Epic Games Launcher", "Juegos", "Tienda y lanzador de Epic Games", "winget", "EpicGames.EpicGamesLauncher", false),
                Make("vcredist", "Visual C++ 2015-2022 x64", "Runtimes", "Librerias esenciales para apps y juegos", "winget", "Microsoft.VCRedist.2015+.x64", true),
                Make("dotnet8", ".NET Desktop Runtime 8", "Runtimes", "Runtime para aplicaciones modernas .NET", "winget", "Microsoft.DotNet.DesktopRuntime.8", false)
            };
            return cat;
        }

        private static AppItem Make(string id, string name, string category, string desc, string source, string target, bool essential)
        {
            AppItem item = new AppItem();
            item.Id = id;
            item.Name = name;
            item.Category = category;
            item.Description = desc;
            item.Source = source;
            item.Target = target;
            item.SilentArgs = string.Empty;
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
