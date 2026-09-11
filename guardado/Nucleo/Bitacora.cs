// Bitacora de lo que hizo el autoguardado.
//
// Existe porque el tema corre solo, sin nadie mirando: cuando algo salga mal el usuario
// necesita poder decir que paso y a que hora, sin depender de acordarse.
//
// Se recorta sola a 500 lineas para que no crezca sin fin.

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Riga.Guardado.Nucleo
{
    internal static class Bitacora
    {
        const int MAXIMO = 500;
        static readonly object CANDADO = new object();

        public static string Archivo
        {
            get { return Path.Combine(Ajustes.Carpeta, "autoguardado.log"); }
        }

        public static void Anotar(string texto)
        {
            try
            {
                lock (CANDADO)
                {
                    Directory.CreateDirectory(Ajustes.Carpeta);
                    File.AppendAllText(Archivo,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + texto + Environment.NewLine,
                        Encoding.UTF8);
                    Recortar();
                }
            }
            catch { /* la bitacora nunca debe romper el guardado */ }
        }

        static void Recortar()
        {
            try
            {
                var lineas = File.ReadAllLines(Archivo, Encoding.UTF8);
                if (lineas.Length <= MAXIMO * 2) return;
                File.WriteAllLines(Archivo, lineas.Skip(lineas.Length - MAXIMO), Encoding.UTF8);
            }
            catch { }
        }

        public static string UltimasLineas(int cuantas)
        {
            try
            {
                if (!File.Exists(Archivo)) return "";
                var lineas = File.ReadAllLines(Archivo, Encoding.UTF8);
                return string.Join(Environment.NewLine,
                                   lineas.Skip(Math.Max(0, lineas.Length - cuantas)));
            }
            catch { return ""; }
        }
    }
}
