// La chapa que lleva cada columna en su propia celda de cabecera.
//
// Sin esto el Excel es un callejon sin salida: se puede leer pero no se puede volver. El
// nombre de un parametro no alcanza para encontrarlo (hay homonimos, y el usuario renombra
// columnas en la tabla), asi que cada cabecera se escribe como
//
//     Volumen (metros cubicos) {s:41bd...|u:autodesk.unit.unit:cubicMeters-1.0.1}
//
// QUE CLAVE SE USA EN CADA CASO, Y POR QUE
//
//   compartido  ->  {s:GUID}
//       El GUID es el unico identificador que sobrevive a salir del modelo. Para eso
//       existen los parametros compartidos. Si el Excel se lleva a otro modelo que use el
//       mismo archivo de parametros compartidos, sigue apuntando al mismo parametro.
//
//   de sistema  ->  {b:NOMBRE_DEL_ENUM}
//       Un parametro de sistema no tiene GUID: su identidad es el BuiltInParameter. Se
//       escribe el NOMBRE y no el numero por dos razones: se lee en el Excel, y en 2025 el
//       ElementId paso a long, asi que el numero suelto invita a confusion. Enum.TryParse
//       lo devuelve exacto.
//
//   de proyecto ->  {p:ID}
//       Un parametro de proyecto no compartido no tiene GUID, y su ElementId solo vale
//       DENTRO de este modelo. Se escribe igual porque el caso real es exportar y volver al
//       mismo modelo, pero al importar se prueba primero el id y despues el nombre.
//
//   no escribible -> {x}
//       Recuento, formula, combinado. Van marcadas para que el importador ni las mire, en
//       vez de fallar fila por fila.
//
// La unidad va en la misma chapa cuando hubo conversion. Es lo que permite deshacerla al
// importar: la etiqueta "metros cubicos" depende del idioma de Revit, el ForgeTypeId no.

using System;
using System.Text;
using Autodesk.Revit.DB;

namespace Riga.ExportarTablas.Nucleo
{
    internal enum ClaseClave { Ninguna, Compartido, Sistema, Proyecto, NoEscribible, Fija }

    internal sealed class Chapa
    {
        public ClaseClave Clase = ClaseClave.Ninguna;
        public string Clave = "";        // guid, nombre del enum, id, o el nombre fijo
        public string Nombre = "";       // el nombre visible, ya sin chapa ni unidad
        public ForgeTypeId Unidad;       // null si no hubo conversion

        public bool Escribible
        {
            get
            {
                return Clase == ClaseClave.Compartido
                    || Clase == ClaseClave.Sistema
                    || Clase == ClaseClave.Proyecto;
            }
        }
    }

    internal static class Identidad
    {
        public const string COL_MODELO = "Modelo";
        public const string COL_GUID = "GUID";
        public const string COL_ID = "Element ID";

        /// <summary>
        /// Marca de la hoja consolidada. Esa hoja apila tablas distintas bajo la cabecera de
        /// la PRIMERA, que es lo que se pidio: apilar literal, sin acomodar nada. Para leer
        /// esta perfecto, pero para volver es una trampa: las filas del segundo bloque en
        /// adelante escribirian en los parametros del primero. Se marca para que el
        /// importador la rechace en vez de hacer un desastre prolijo.
        /// </summary>
        public const string CLAVE_CONSOLIDADO = "consolidado";

        const char ABRE = '{';
        const char CIERRA = '}';

        // ---------------- escribir ----------------

        /// <summary>Cabecera de una columna fija de identificacion.</summary>
        public static string Fija(string nombre, string clave)
        {
            return nombre + " " + ABRE + "k:" + clave + CIERRA;
        }

        /// <summary>Cabecera completa de una columna de parametro: nombre, unidad y chapa.</summary>
        public static string Cabecera(Document doc, Columna c)
        {
            string visible = Unidades.Cabecera(Limpiar(c.Nombre), c.Unidad);

            var sb = new StringBuilder();
            sb.Append(visible).Append(' ').Append(ABRE);

            if (c.Clase != Clase.Parametro) sb.Append('x');
            else
            {
                string guid = GuidCompartido(doc, c.ParametroId);
                if (guid != null) sb.Append("s:").Append(guid);
                else if (c.UsaBip) sb.Append("b:").Append(c.Bip.ToString());
                else if (c.ParametroId != null && c.ParametroId.Value > 0)
                    sb.Append("p:").Append(c.ParametroId.Value);
                else sb.Append('x');

                if (c.Unidad != null && c.Unidad.Convierte)
                {
                    string u = null;
                    try { u = c.Unidad.Destino.TypeId; } catch { }
                    if (!string.IsNullOrEmpty(u)) sb.Append("|u:").Append(u);
                }

                if (c.EsDeTipo) sb.Append("|t");     // el parametro vive en el tipo
            }

            sb.Append(CIERRA);
            return sb.ToString();
        }

        static string GuidCompartido(Document doc, ElementId id)
        {
            if (id == null || id.Value <= 0) return null;
            try
            {
                var sp = doc.GetElement(id) as SharedParameterElement;
                return sp == null ? null : sp.GuidValue.ToString("D");
            }
            catch { return null; }
        }

        /// <summary>Las llaves son el delimitador de la chapa: no pueden estar en el nombre.</summary>
        static string Limpiar(string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return "";
            return nombre.Replace(ABRE, '(').Replace(CIERRA, ')');
        }

        // ---------------- leer ----------------

        /// <summary>Parte una cabecera exportada. Devuelve null si no lleva chapa.</summary>
        public static Chapa Leer(string cabecera)
        {
            if (string.IsNullOrEmpty(cabecera)) return null;

            int cierra = cabecera.LastIndexOf(CIERRA);
            if (cierra != cabecera.Length - 1) return null;
            int abre = cabecera.LastIndexOf(ABRE);
            if (abre < 0) return null;

            string cuerpo = cabecera.Substring(abre + 1, cierra - abre - 1);
            var ch = new Chapa { Nombre = cabecera.Substring(0, abre).TrimEnd() };

            foreach (var trozo in cuerpo.Split('|'))
            {
                if (trozo.Length == 0) continue;

                if (trozo == "x") { ch.Clase = ClaseClave.NoEscribible; continue; }
                if (trozo == "t") continue;                       // informativo: vive en el tipo

                int dp = trozo.IndexOf(':');
                if (dp <= 0) continue;
                string etiqueta = trozo.Substring(0, dp);
                string valor = trozo.Substring(dp + 1);

                switch (etiqueta)
                {
                    case "s": ch.Clase = ClaseClave.Compartido; ch.Clave = valor; break;
                    case "b": ch.Clase = ClaseClave.Sistema; ch.Clave = valor; break;
                    case "p": ch.Clase = ClaseClave.Proyecto; ch.Clave = valor; break;
                    case "k": ch.Clase = ClaseClave.Fija; ch.Clave = valor; break;
                    case "u":
                        try { ch.Unidad = new ForgeTypeId(valor); } catch { }
                        break;
                }
            }

            // El nombre visible todavia trae la unidad entre parentesis; se la saca para
            // poder comparar contra el nombre real del parametro.
            ch.Nombre = SinUnidad(ch.Nombre);
            return ch.Clase == ClaseClave.Ninguna ? null : ch;
        }

        static string SinUnidad(string visible)
        {
            if (string.IsNullOrEmpty(visible)) return "";
            if (!visible.EndsWith(")", StringComparison.Ordinal)) return visible;
            int abre = visible.LastIndexOf('(');
            return abre <= 0 ? visible : visible.Substring(0, abre).TrimEnd();
        }
    }
}
