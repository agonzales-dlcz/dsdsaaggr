// Lo que el usuario configura, y como sobrevive al cierre de Revit.
//
// Formato clave=valor, no JSON: son pocos campos y el resto de los addins tampoco arrastran
// dependencias. Un archivo que se puede abrir con el bloc de notas y arreglar a mano.
//
// Si el archivo no existe o esta roto se vuelve a los valores de fabrica, que a proposito
// dejan el autoguardado APAGADO: nadie quiere que una herramienta recien instalada empiece
// a sincronizar sola.
//
// DOS INTERVALOS
//
// Guardar la local es barato; sincronizar cuesta minutos y congela Revit. Por eso cada uno
// tiene su propio reloj: se puede guardar cada 10 minutos y sincronizar cada hora.
//
// Hasta la version anterior habia un solo "minutos". Si aparece esa clave y no las nuevas,
// se usa para las dos: asi el archivo viejo sigue valiendo y nadie pierde su configuracion.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Riga.Guardado.Nucleo
{
    internal class Ajustes
    {
        public bool Activo = false;

        public bool GuardarLocal = true;
        public int MinutosLocal = 15;

        public bool Sincronizar = false;
        public int MinutosSync = 60;

        public bool AvisoBarra = true;           // un renglon en la barra de estado, sin bloquear
        public bool SoloSiHayCambios = true;
        public bool LiberarPrestados = false;    // por defecto no suelta lo que estas editando
        public string Comentario = "Sincronizacion automatica";

        public const int MIN_MINUTOS = 1;
        public const int MAX_MINUTOS = 480;

        public static string Carpeta
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "dsdsaaggr");
            }
        }

        public static string Archivo { get { return Path.Combine(Carpeta, "autoguardado.txt"); } }

        /// <summary>Hay algo que hacer, o esta todo destildado.</summary>
        public bool HayTarea { get { return GuardarLocal || Sincronizar; } }

        public Ajustes Copia()
        {
            return new Ajustes
            {
                Activo = Activo,
                GuardarLocal = GuardarLocal,
                MinutosLocal = MinutosLocal,
                Sincronizar = Sincronizar,
                MinutosSync = MinutosSync,
                AvisoBarra = AvisoBarra,
                SoloSiHayCambios = SoloSiHayCambios,
                LiberarPrestados = LiberarPrestados,
                Comentario = Comentario
            };
        }

        public void Normalizar()
        {
            MinutosLocal = Acotar(MinutosLocal);
            MinutosSync = Acotar(MinutosSync);
            if (Comentario == null) Comentario = "";
            if (Comentario.Length > 200) Comentario = Comentario.Substring(0, 200);
        }

        static int Acotar(int m)
        {
            if (m < MIN_MINUTOS) return MIN_MINUTOS;
            if (m > MAX_MINUTOS) return MAX_MINUTOS;
            return m;
        }

        public static Ajustes Cargar()
        {
            var a = new Ajustes();
            try
            {
                if (!File.Exists(Archivo)) return a;

                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var linea in File.ReadAllLines(Archivo, Encoding.UTF8))
                {
                    var t = linea.Trim();
                    if (t.Length == 0 || t[0] == '#') continue;
                    int i = t.IndexOf('=');
                    if (i <= 0) continue;
                    d[t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
                }

                a.Activo = Bool(d, "activo", a.Activo);
                a.GuardarLocal = Bool(d, "guardar_local", a.GuardarLocal);
                a.Sincronizar = Bool(d, "sincronizar", a.Sincronizar);
                a.AvisoBarra = Bool(d, "aviso_barra", a.AvisoBarra);
                a.SoloSiHayCambios = Bool(d, "solo_si_hay_cambios", a.SoloSiHayCambios);
                a.LiberarPrestados = Bool(d, "liberar_prestados", a.LiberarPrestados);
                if (d.ContainsKey("comentario")) a.Comentario = d["comentario"];

                // El "minutos" unico de la version anterior vale para los dos, hasta que el
                // usuario los separe y se guarde el archivo nuevo.
                int viejo = Entero(d, "minutos", 0);
                a.MinutosLocal = Entero(d, "minutos_local", viejo > 0 ? viejo : a.MinutosLocal);
                a.MinutosSync = Entero(d, "minutos_sync", viejo > 0 ? viejo : a.MinutosSync);
            }
            catch { /* archivo ilegible: valores de fabrica */ }

            a.Normalizar();
            return a;
        }

        public void Guardar()
        {
            Normalizar();
            try
            {
                Directory.CreateDirectory(Carpeta);
                var sb = new StringBuilder();
                sb.AppendLine("# Autoguardado dsdsaaggr. Se relee al arrancar Revit.");
                sb.AppendLine("activo=" + (Activo ? "1" : "0"));
                sb.AppendLine("guardar_local=" + (GuardarLocal ? "1" : "0"));
                sb.AppendLine("minutos_local=" + MinutosLocal.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("sincronizar=" + (Sincronizar ? "1" : "0"));
                sb.AppendLine("minutos_sync=" + MinutosSync.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("aviso_barra=" + (AvisoBarra ? "1" : "0"));
                sb.AppendLine("solo_si_hay_cambios=" + (SoloSiHayCambios ? "1" : "0"));
                sb.AppendLine("liberar_prestados=" + (LiberarPrestados ? "1" : "0"));
                sb.AppendLine("comentario=" + Comentario.Replace("\r", " ").Replace("\n", " "));
                File.WriteAllText(Archivo, sb.ToString(), Encoding.UTF8);
            }
            catch { /* si no se puede escribir, al menos sigue valiendo en esta sesion */ }
        }

        static bool Bool(Dictionary<string, string> d, string clave, bool porDefecto)
        {
            string v;
            if (!d.TryGetValue(clave, out v)) return porDefecto;
            v = v.Trim();
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                            || v.Equals("si", StringComparison.OrdinalIgnoreCase);
        }

        static int Entero(Dictionary<string, string> d, string clave, int porDefecto)
        {
            string v;
            int n;
            if (d.TryGetValue(clave, out v) &&
                int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;
            return porDefecto;
        }
    }
}
