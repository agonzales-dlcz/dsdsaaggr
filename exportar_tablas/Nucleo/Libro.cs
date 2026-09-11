// Lee un .xlsx o un .csv y devuelve hojas de texto crudo.
//
// Sin NuGet, igual que el escritor: un .xlsx es un zip con XML adentro y
// System.IO.Compression ya viene en el runtime.
//
// Todo sale como TEXTO, incluidos los numeros. Interpretarlos es trabajo del importador,
// que es el unico que sabe que parametro es cada columna y en que unidad estaba.
//
// Hay que aguantar las dos formas de guardar texto en un xlsx: la nuestra (cadenas en
// linea, <is><t>) y la de Excel cuando el usuario abre y guarda (tabla compartida,
// sharedStrings.xml). Un archivo exportado por esta herramienta y despues editado en Excel
// llega con la segunda.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace Riga.ExportarTablas.Nucleo
{
    internal sealed class HojaLeida
    {
        public string Nombre = "";
        public List<string[]> Filas = new List<string[]>();

        public string[] Cabecera { get { return Filas.Count > 0 ? Filas[0] : new string[0]; } }
        public int Datos { get { return Math.Max(0, Filas.Count - 1); } }
    }

    internal static class Libro
    {
        public static List<HojaLeida> Leer(string ruta)
        {
            if (string.IsNullOrEmpty(ruta) || !File.Exists(ruta))
                throw new FileNotFoundException("No se encontro el archivo.", ruta);

            string ext = (Path.GetExtension(ruta) ?? "").ToLowerInvariant();
            if (ext == ".csv" || ext == ".txt") return new List<HojaLeida> { LeerCsv(ruta) };
            if (ext == ".xlsx" || ext == ".xlsm") return LeerXlsx(ruta);

            throw new NotSupportedException("Solo .xlsx y .csv. Llego: " + ext);
        }

        // ---------------- csv ----------------

        static HojaLeida LeerCsv(string ruta)
        {
            var h = new HojaLeida { Nombre = Path.GetFileNameWithoutExtension(ruta) };
            string texto = File.ReadAllText(ruta, DetectarCodificacion(ruta));
            char sep = Separador(texto);

            foreach (var fila in PartirCsv(texto, sep)) h.Filas.Add(fila);
            return h;
        }

        static Encoding DetectarCodificacion(string ruta)
        {
            try
            {
                var bom = new byte[3];
                using (var fs = File.OpenRead(ruta)) fs.Read(bom, 0, 3);
                if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
            }
            catch { }
            return Encoding.UTF8;
        }

        /// <summary>Excel en espanol guarda con punto y coma. Se decide por la primera linea.</summary>
        static char Separador(string texto)
        {
            int corte = texto.IndexOf('\n');
            string primera = corte < 0 ? texto : texto.Substring(0, corte);
            int comas = primera.Count(c => c == ',');
            int pyc = primera.Count(c => c == ';');
            int tabs = primera.Count(c => c == '\t');
            if (tabs >= comas && tabs >= pyc && tabs > 0) return '\t';
            return pyc > comas ? ';' : ',';
        }

        static IEnumerable<string[]> PartirCsv(string texto, char sep)
        {
            var campos = new List<string>();
            var actual = new StringBuilder();
            bool comillas = false;

            for (int i = 0; i < texto.Length; i++)
            {
                char c = texto[i];

                if (comillas)
                {
                    if (c == '"')
                    {
                        if (i + 1 < texto.Length && texto[i + 1] == '"') { actual.Append('"'); i++; }
                        else comillas = false;
                    }
                    else actual.Append(c);
                    continue;
                }

                if (c == '"') { comillas = true; continue; }
                if (c == sep) { campos.Add(actual.ToString()); actual.Clear(); continue; }

                if (c == '\r') continue;
                if (c == '\n')
                {
                    campos.Add(actual.ToString());
                    actual.Clear();
                    yield return campos.ToArray();
                    campos.Clear();
                    continue;
                }

                actual.Append(c);
            }

            if (actual.Length > 0 || campos.Count > 0)
            {
                campos.Add(actual.ToString());
                yield return campos.ToArray();
            }
        }

        // ---------------- xlsx ----------------

        static List<HojaLeida> LeerXlsx(string ruta)
        {
            var hojas = new List<HojaLeida>();

            using (var zip = ZipFile.OpenRead(ruta))
            {
                var compartidas = Compartidas(zip);
                var rel = Relaciones(zip);

                foreach (var par in Pestanas(zip))
                {
                    string destino;
                    if (!rel.TryGetValue(par.Value, out destino)) continue;

                    string ruta2 = destino.StartsWith("/") ? destino.TrimStart('/') : "xl/" + destino;
                    var entrada = zip.GetEntry(ruta2) ?? zip.GetEntry(destino);
                    if (entrada == null) continue;

                    var h = new HojaLeida { Nombre = par.Key };
                    using (var s = entrada.Open()) LeerPestana(s, compartidas, h);
                    hojas.Add(h);
                }
            }

            return hojas;
        }

        static List<string> Compartidas(ZipArchive zip)
        {
            var r = new List<string>();
            var e = zip.GetEntry("xl/sharedStrings.xml");
            if (e == null) return r;

            using (var s = e.Open())
            using (var x = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = false }))
            {
                while (x.Read())
                {
                    if (x.NodeType != XmlNodeType.Element || x.LocalName != "si") continue;
                    if (x.IsEmptyElement) { r.Add(""); continue; }

                    // Un <si> puede ser <t>texto</t> o varios <r><t>trozo</t></r>: se
                    // concatenan todos los <t> que haya adentro.
                    using (var sub = x.ReadSubtree())
                    {
                        sub.Read();
                        r.Add(JuntarTextos(sub, null, null));
                    }
                }
            }
            return r;
        }

        static Dictionary<string, string> Relaciones(ZipArchive zip)
        {
            var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var e = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (e == null) return r;

            using (var s = e.Open())
            using (var x = XmlReader.Create(s))
                while (x.Read())
                    if (x.NodeType == XmlNodeType.Element && x.LocalName == "Relationship")
                    {
                        string id = x.GetAttribute("Id");
                        string destino = x.GetAttribute("Target");
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(destino)) r[id] = destino;
                    }
            return r;
        }

        /// <summary>Nombre de pestana -> r:id, en el orden del libro.</summary>
        static List<KeyValuePair<string, string>> Pestanas(ZipArchive zip)
        {
            var r = new List<KeyValuePair<string, string>>();
            var e = zip.GetEntry("xl/workbook.xml");
            if (e == null) return r;

            using (var s = e.Open())
            using (var x = XmlReader.Create(s))
                while (x.Read())
                    if (x.NodeType == XmlNodeType.Element && x.LocalName == "sheet")
                    {
                        string nombre = x.GetAttribute("name") ?? "";
                        string id = x.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
                                 ?? x.GetAttribute("r:id");
                        if (!string.IsNullOrEmpty(id)) r.Add(new KeyValuePair<string, string>(nombre, id));
                    }
            return r;
        }

        static void LeerPestana(Stream s, List<string> compartidas, HojaLeida h)
        {
            using (var x = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = false }))
            {
                var fila = new List<string>();
                bool enFila = false;

                while (x.Read())
                {
                    if (x.NodeType == XmlNodeType.Element && x.LocalName == "row")
                    {
                        enFila = true;
                        fila.Clear();
                        if (x.IsEmptyElement) { h.Filas.Add(new string[0]); enFila = false; }
                        continue;
                    }

                    if (enFila && x.NodeType == XmlNodeType.Element && x.LocalName == "c")
                    {
                        int col = Columna(x.GetAttribute("r"));
                        string tipo = x.GetAttribute("t");
                        string valor = Celda(x, tipo, compartidas);

                        if (col < 0) col = fila.Count;
                        while (fila.Count <= col) fila.Add("");
                        fila[col] = valor;
                        continue;
                    }

                    if (x.NodeType == XmlNodeType.EndElement && x.LocalName == "row")
                    {
                        h.Filas.Add(fila.ToArray());
                        enFila = false;
                    }
                }
            }
        }

        static string Celda(XmlReader x, string tipo, List<string> compartidas)
        {
            if (x.IsEmptyElement) return "";

            // ReadSubtree acota la lectura a esta celda. Sin eso, ReadElementContentAsString
            // deja el lector PASADO del nodo siguiente y el Read() del bucle se saltea el
            // </c>: las celdas se derraman una en otra. Costo un test: el Element ID salia
            // como "275866847.38092786188727S-01", que son tres celdas pegadas.
            using (var sub = x.ReadSubtree())
            {
                sub.Read();
                return JuntarTextos(sub, tipo, compartidas);
            }
        }

        /// <summary>
        /// Junta el contenido de los &lt;v&gt; y &lt;t&gt; que haya dentro del nodo actual.
        /// No vuelve a llamar a Read cuando ReadElementContentAsString ya avanzo, que es
        /// justo el detalle del que depende que no se pierdan nodos.
        /// </summary>
        static string JuntarTextos(XmlReader x, string tipo, List<string> compartidas)
        {
            var sb = new StringBuilder();
            bool avanzar = true;

            while (true)
            {
                if (avanzar) { if (!x.Read()) break; }
                else if (x.EOF) break;
                avanzar = true;

                if (x.NodeType != XmlNodeType.Element) continue;

                if (x.LocalName == "v" && compartidas != null)
                {
                    if (x.IsEmptyElement) continue;
                    string bruto = x.ReadElementContentAsString();
                    avanzar = false;

                    if (tipo == "s")
                    {
                        int i;
                        if (int.TryParse(bruto, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)
                            && i >= 0 && i < compartidas.Count)
                            sb.Append(compartidas[i]);
                    }
                    else if (tipo == "b") sb.Append(bruto == "1" ? "VERDADERO" : "FALSO");
                    else sb.Append(bruto);      // numero, fecha serial, o cadena de formula
                    continue;
                }

                if (x.LocalName == "t")
                {
                    if (x.IsEmptyElement) continue;
                    sb.Append(x.ReadElementContentAsString());
                    avanzar = false;
                }
            }

            return sb.ToString();
        }

        /// <summary>"AB12" -> 27. Devuelve -1 si la referencia no viene.</summary>
        static int Columna(string referencia)
        {
            if (string.IsNullOrEmpty(referencia)) return -1;
            int n = 0, i = 0;
            while (i < referencia.Length)
            {
                char c = char.ToUpperInvariant(referencia[i]);
                if (c < 'A' || c > 'Z') break;
                n = n * 26 + (c - 'A' + 1);
                i++;
            }
            return n - 1;
        }
    }
}
