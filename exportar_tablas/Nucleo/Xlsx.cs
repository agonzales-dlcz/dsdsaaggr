// Escritor de XLSX sin dependencias externas.
//
// Un .xlsx es un ZIP con partes XML. .NET trae System.IO.Compression, asi que no hace
// falta ninguna libreria de terceros (que ademas habria que bajar, y aca no hay NuGet).
// Se usan cadenas en linea (inlineStr) en vez de tabla de cadenas compartidas: ocupa algo
// mas pero evita una parte entera y el mapa de indices.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Riga.ExportarTablas.Nucleo
{
    internal sealed class Xlsx
    {
        public sealed class Hoja
        {
            public string Nombre;
            public List<string> Cabeceras = new List<string>();
            public List<object[]> Filas = new List<object[]>();   // string, double, int, bool o null
        }

        // Estilos declarados en styles.xml, por indice.
        const int NORMAL = 0;
        const int CABECERA = 1;
        const int DECIMAL = 2;

        public static void Escribir(string ruta, IList<Hoja> hojas)
        {
            if (hojas == null || hojas.Count == 0)
                throw new ArgumentException("No hay ninguna hoja para escribir.");

            var nombres = NombresUnicos(hojas);

            using (var fs = new FileStream(ruta, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Parte(zip, "[Content_Types].xml", ContentTypes(hojas.Count));
                Parte(zip, "_rels/.rels", Rels());
                Parte(zip, "xl/workbook.xml", Workbook(nombres));
                Parte(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(hojas.Count));
                Parte(zip, "xl/styles.xml", Styles());

                for (int i = 0; i < hojas.Count; i++)
                    Parte(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", Sheet(hojas[i]));
            }
        }

        static void Parte(ZipArchive zip, string nombre, string contenido)
        {
            var e = zip.CreateEntry(nombre, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
                w.Write(contenido);
        }

        // ---------- partes fijas ----------

        static string ContentTypes(int hojas)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 1; i <= hojas; i++)
                sb.Append("<Override PartName=\"/xl/worksheets/sheet").Append(i)
                  .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        static string Rels()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                 + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                 + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                 + "</Relationships>";
        }

        static string Workbook(IList<string> nombres)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
            sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            for (int i = 0; i < nombres.Count; i++)
                sb.Append("<sheet name=\"").Append(Esc(nombres[i])).Append("\" sheetId=\"").Append(i + 1)
                  .Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
            sb.Append("</sheets></workbook>");
            return sb.ToString();
        }

        static string WorkbookRels(int hojas)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 1; i <= hojas; i++)
                sb.Append("<Relationship Id=\"rId").Append(i)
                  .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet")
                  .Append(i).Append(".xml\"/>");
            sb.Append("<Relationship Id=\"rId").Append(hojas + 1)
              .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        // Excel exige al menos dos rellenos y una fuente; menos que esto y no abre el archivo.
        static string Styles()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                 + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                 + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"0.############\"/></numFmts>"
                 + "<fonts count=\"2\">"
                 + "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>"
                 + "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>"
                 + "</fonts>"
                 + "<fills count=\"2\">"
                 + "<fill><patternFill patternType=\"none\"/></fill>"
                 + "<fill><patternFill patternType=\"gray125\"/></fill>"
                 + "</fills>"
                 + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
                 + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                 + "<cellXfs count=\"3\">"
                 + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
                 + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
                 + "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
                 + "</cellXfs>"
                 + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
                 + "</styleSheet>";
        }

        // ---------- hoja ----------

        static string Sheet(Hoja h)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            // Deja la fila de cabecera fija al desplazarse.
            if (h.Cabeceras.Count > 0)
                sb.Append("<sheetViews><sheetView workbookViewId=\"0\">")
                  .Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>")
                  .Append("</sheetView></sheetViews>");

            sb.Append("<sheetData>");

            int fila = 1;
            if (h.Cabeceras.Count > 0)
            {
                sb.Append("<row r=\"1\">");
                for (int c = 0; c < h.Cabeceras.Count; c++)
                    Celda(sb, c, fila, h.Cabeceras[c], CABECERA);
                sb.Append("</row>");
                fila++;
            }

            foreach (var f in h.Filas)
            {
                sb.Append("<row r=\"").Append(fila).Append("\">");
                for (int c = 0; c < f.Length; c++)
                    Celda(sb, c, fila, f[c], NORMAL);
                sb.Append("</row>");
                fila++;
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        static void Celda(StringBuilder sb, int col, int fila, object valor, int estiloTexto)
        {
            if (valor == null) return;                       // celda ausente = celda vacia

            string refe = Columna(col) + fila;

            if (valor is double || valor is float || valor is decimal)
            {
                double d = Convert.ToDouble(valor, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) return;
                int estilo = estiloTexto == CABECERA ? CABECERA : DECIMAL;
                sb.Append("<c r=\"").Append(refe).Append("\" s=\"").Append(estilo).Append("\"><v>")
                  .Append(d.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                return;
            }

            if (valor is int || valor is long || valor is short)
            {
                sb.Append("<c r=\"").Append(refe).Append("\" s=\"").Append(estiloTexto).Append("\"><v>")
                  .Append(Convert.ToInt64(valor).ToString(CultureInfo.InvariantCulture)).Append("</v></c>");
                return;
            }

            if (valor is bool)
            {
                sb.Append("<c r=\"").Append(refe).Append("\" s=\"").Append(estiloTexto).Append("\" t=\"b\"><v>")
                  .Append(((bool)valor) ? "1" : "0").Append("</v></c>");
                return;
            }

            string s = valor.ToString();
            if (s.Length == 0) return;
            sb.Append("<c r=\"").Append(refe).Append("\" s=\"").Append(estiloTexto)
              .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
              .Append(Esc(s)).Append("</t></is></c>");
        }

        static string Columna(int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i > 0)
            {
                int r = (i - 1) % 26;
                sb.Insert(0, (char)('A' + r));
                i = (i - 1) / 26;
            }
            return sb.ToString();
        }

        static string Esc(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default:
                        // XML 1.0 no admite los caracteres de control, y Revit a veces los trae.
                        if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') break;
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // ---------- nombres de hoja ----------

        static readonly char[] PROHIBIDOS = { ':', '\\', '/', '?', '*', '[', ']' };

        /// <summary>Excel: 31 caracteres, sin ciertos signos, sin repetir y sin vacios.</summary>
        static List<string> NombresUnicos(IList<Hoja> hojas)
        {
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var r = new List<string>(hojas.Count);

            foreach (var h in hojas)
            {
                string n = h.Nombre ?? "";
                foreach (char c in PROHIBIDOS) n = n.Replace(c, '-');
                n = n.Trim('\'', ' ');
                if (n.Length == 0) n = "Hoja";
                if (n.Length > 31) n = n.Substring(0, 31);

                string baseN = n;
                for (int i = 2; !usados.Add(n); i++)
                {
                    string suf = " (" + i + ")";
                    n = (baseN.Length + suf.Length > 31 ? baseN.Substring(0, 31 - suf.Length) : baseN) + suf;
                }
                r.Add(n);
            }
            return r;
        }
    }
}
