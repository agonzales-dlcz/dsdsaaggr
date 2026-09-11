// La instalacion propiamente dicha, separada de la ventana para que la use tanto el modo
// con interfaz como el silencioso.
//
// Todo va a la carpeta de addins DEL USUARIO:
//
//     %APPDATA%\Autodesk\Revit\Addins\2025
//
// Es parte del perfil, no de Archivos de programa, asi que no hace falta contrasena de
// administrador ni elevar el proceso. Revit lee esa carpeta ademas de la del equipo.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Riga.Instalador
{
    internal static class Paquete
    {
        public const string PREFIJO = "paquete/";
        public const string VERSION_REVIT = "2025";

        // Restos de nombres anteriores: se borran para no terminar con botones duplicados.
        static readonly string[] VIEJOS =
        {
            "RigaPublicador.dll", "RigaPublicador.deps.json",
            "RigaPublicador.runtimeconfig.json", "RigaPublicador.addin",
            "RigaSectores.dll", "RigaSectores.deps.json",
            "RigaSectores.runtimeconfig.json", "RigaSectores.addin"
        };

        public static string Destino
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk", "Revit", "Addins", VERSION_REVIT);
            }
        }

        public static List<string> Recursos()
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .Where(x => x.StartsWith(PREFIJO, StringComparison.Ordinal))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool RevitAbierto()
        {
            try { return Process.GetProcessesByName("Revit").Length > 0; }
            catch { return false; }
        }

        /// <summary>Copia los addins. Devuelve false y explica en el registro si no pudo.</summary>
        public static bool Instalar(Action<string> registrar)
        {
            try
            {
                string destino = Destino;
                Directory.CreateDirectory(destino);

                int viejos = BorrarViejos();
                if (viejos > 0) registrar("Se quitaron " + viejos + " archivos de versiones anteriores.");

                var asm = Assembly.GetExecutingAssembly();
                int n = 0;
                foreach (var recurso in Recursos())
                {
                    string nombre = recurso.Substring(PREFIJO.Length);
                    using (var origen = asm.GetManifestResourceStream(recurso))
                    using (var salida = File.Create(Path.Combine(destino, nombre)))
                        origen.CopyTo(salida);

                    registrar("  copiado  " + nombre);
                    n++;
                }

                registrar("");
                registrar("Listo: " + n + " archivos en " + destino);
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                registrar("ERROR de permisos: " + ex.Message);
                registrar("Revisa que la carpeta no este bloqueada por el antivirus o por la sincronizacion.");
                return false;
            }
            catch (IOException ex)
            {
                registrar("ERROR de archivo: " + ex.Message);
                registrar("Suele ser que Revit quedo abierto y tiene la DLL tomada.");
                return false;
            }
            catch (Exception ex)
            {
                registrar("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        public static bool Desinstalar(Action<string> registrar)
        {
            int n = 0;
            foreach (var recurso in Recursos())
                if (Borrar(Path.Combine(Destino, recurso.Substring(PREFIJO.Length)))) n++;
            n += BorrarViejos();

            registrar(n > 0 ? "Se quitaron " + n + " archivos." : "No habia nada que quitar.");
            return true;
        }

        static int BorrarViejos()
        {
            int n = 0;
            foreach (var v in VIEJOS) if (Borrar(Path.Combine(Destino, v))) n++;
            return n;
        }

        static bool Borrar(string ruta)
        {
            try
            {
                if (!File.Exists(ruta)) return false;
                File.Delete(ruta);
                return true;
            }
            catch { return false; }
        }
    }
}
