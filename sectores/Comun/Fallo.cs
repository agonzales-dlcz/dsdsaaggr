// Un mensaje suelto no alcanza para saber donde fallo: se deja el stack en un archivo.

using System;
using System.IO;

namespace Riga.Sectores.Comun
{
    internal static class Fallo
    {
        public static string Volcar(Exception ex, string quien)
        {
            var raiz = ex;
            while (raiz.InnerException != null) raiz = raiz.InnerException;

            string texto = quien + Environment.NewLine
                         + raiz.GetType().FullName + ": " + raiz.Message + Environment.NewLine
                         + Environment.NewLine + raiz.StackTrace;
            try
            {
                string carpeta = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sectores");
                Directory.CreateDirectory(carpeta);
                string ruta = Path.Combine(carpeta,
                    "error_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(ruta, texto);
                return raiz.Message + Environment.NewLine + Environment.NewLine + "Detalle en: " + ruta;
            }
            catch { return texto; }
        }
    }
}
