// Punto de entrada propio.
//
// A proposito NO se usa App.xaml con StartupUri: el Main que genera WPF para .NET no
// recibe argumentos, y con el los modos /instalar y /desinstalar no llegaban a
// ejecutarse. Con un Main propio el comportamiento es el que se lee aca.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace Riga.Instalador
{
    internal static class Programa
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool instalar = args.Any(a => a.Equals("/instalar", StringComparison.OrdinalIgnoreCase));
            bool desinstalar = args.Any(a => a.Equals("/desinstalar", StringComparison.OrdinalIgnoreCase));

            if (instalar || desinstalar) return Silencioso(instalar, args);

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var ventana = new Ventana();
            app.MainWindow = ventana;
            ventana.Show();
            return app.Run();
        }

        /// <summary>Sin ventana, para desplegarlo por script. Deja un registro al lado del exe.</summary>
        static int Silencioso(bool instalar, string[] args)
        {
            var log = new StringBuilder();
            log.AppendLine("argumentos: " + string.Join(" ", args));
            log.AppendLine("carpeta destino: " + Paquete.Destino);
            log.AppendLine("archivos en el paquete: " + Paquete.Recursos().Count);
            log.AppendLine();

            bool ok;
            if (Paquete.RevitAbierto())
            {
                log.AppendLine("Revit esta abierto: cerralo y volve a intentar.");
                ok = false;
            }
            else
            {
                Action<string> registrar = t => log.AppendLine(t);
                ok = instalar ? Paquete.Instalar(registrar) : Paquete.Desinstalar(registrar);
            }

            Guardar(log.ToString());
            return ok ? 0 : 1;
        }

        static void Guardar(string texto)
        {
            foreach (var carpeta in Carpetas())
            {
                try
                {
                    File.WriteAllText(Path.Combine(carpeta, "InstalarHerramientas.log"),
                                      texto, new UTF8Encoding(false));
                    return;
                }
                catch { }
            }
        }

        static string[] Carpetas()
        {
            string junto = null;
            try
            {
                // En un exe de un solo archivo, ProcessPath es el exe de verdad;
                // BaseDirectory apunta a donde se descomprime, que no sirve.
                var p = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(p)) junto = Path.GetDirectoryName(p);
            }
            catch { }

            return junto == null ? new[] { Path.GetTempPath() } : new[] { junto, Path.GetTempPath() };
        }
    }
}
