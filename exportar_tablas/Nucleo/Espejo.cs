// Completa las columnas calculadas (formula y porcentaje) con lo que la tabla renderiza.
//
// Por que hace falta: Revit calcula esas columnas dentro de la tabla y NO expone la
// expresion por API. El unico lugar donde existe el numero es la tabla renderizada.
//
// Como se lee: con ViewSchedule.Export, no con GetCellText. Medido en el modelo real,
// GetCellText devuelve en blanco las columnas de texto aunque el elemento tenga valor;
// Export las trae todas. Y sus opciones permiten pedir la tabla sin titulo, sin
// cabeceras y sin las filas de agrupacion y totales, que era lo que ensuciaba el conteo.
//
// Que se acomoda y como se deshace: si la tabla no esta detallada, sus filas agrupan
// varios elementos y no hay uno a uno posible. Entonces se la detalla y se le sube la
// precision a las columnas calculadas dentro de una transaccion que SIEMPRE se revierte.
// Si la transaccion no se puede abrir, se lee la tabla como esta: da el valor con su
// redondeo, pero lo da.
//
// REGLA IMPORTANTE: nada de objetos de la API cruza la transaccion. Al revertirla quedan
// invalidos y usarlos despues tira "The referenced object is not valid". Por eso todo lo
// que se necesita despues (indices de columna, firmas de los elementos) se resuelve ANTES
// y se guarda como enteros y cadenas.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace Riga.ExportarTablas.Nucleo
{
    internal sealed class Espejo
    {
        readonly Document _doc;
        readonly Lector _lector;

        /// <summary>Hizo falta acomodar la tabla (y se revirtio).</summary>
        public bool Acomodo;

        /// <summary>Se pudo subir la precision del valor calculado.</summary>
        public bool Preciso;

        public Espejo(Document doc, Lector lector) { _doc = doc; _lector = lector; }

        public Dictionary<Columna, object[]> Completar(ViewSchedule tabla, IList<Element> elementos,
                                                       IList<Columna> columnas, out string motivo)
        {
            motivo = null;
            var calculadas = columnas.Where(c => c.Clase == Clase.Calculado && c.IndiceRenderizado >= 0).ToList();
            if (calculadas.Count == 0 || elementos.Count == 0) return null;

            // --- todo lo que use la API, antes de la transaccion ---

            // Columnas de texto que sirven para verificar el emparejamiento: se leen del
            // modelo y ademas se renderizan, asi que se pueden comparar exacto.
            var deFirma = columnas
                .Where(c => c.Clase == Clase.Parametro && c.IndiceRenderizado >= 0)
                .ToList();

            var firmaElemento = new string[elementos.Count];
            try
            {
                for (int k = 0; k < elementos.Count; k++) firmaElemento[k] = Firma(elementos[k], deFirma);
            }
            catch (Exception ex) { motivo = "no se pudieron leer los valores de control: " + Raiz(ex); return null; }

            var nfi = FormatoNumerico();

            // --- ahora si, la tabla ---

            List<string[]> filas;
            try { filas = Renderizar(tabla, calculadas, out motivo); }
            catch (Exception ex) { motivo = "no se pudo leer la tabla: " + Raiz(ex); return null; }
            if (filas == null) return null;

            if (filas.Count != elementos.Count)
            {
                motivo = "la tabla rinde " + filas.Count + " filas y el modelo devuelve "
                       + elementos.Count + " elementos: no se pueden emparejar";
                return null;
            }

            var orden = Emparejar(filas, firmaElemento, deFirma, out motivo);
            if (orden == null) return null;

            var r = new Dictionary<Columna, object[]>();
            foreach (var c in calculadas)
            {
                var valores = new object[elementos.Count];
                for (int i = 0; i < filas.Count; i++)
                {
                    var fila = filas[i];
                    if (c.IndiceRenderizado >= fila.Length) continue;
                    valores[orden[i]] = Interpretar(fila[c.IndiceRenderizado], nfi);
                }
                r[c] = valores;
            }
            return r.Count == 0 ? null : r;
        }

        // ---------------- renderizado ----------------

        /// <summary>
        /// Las filas de datos que rinde la tabla, como texto plano. Si hace falta la
        /// acomoda dentro de una transaccion que se revierte siempre.
        /// </summary>
        List<string[]> Renderizar(ViewSchedule tabla, IList<Columna> calculadas, out string motivo)
        {
            motivo = null;

            bool detallada;
            try { detallada = tabla.Definition.IsItemized; } catch { detallada = true; }

            using (var t = new Transaction(_doc, "Exportar tablas (temporal)"))
            {
                bool abierta = false;
                try
                {
                    t.Start();
                    abierta = true;

                    var d = tabla.Definition;
                    if (!d.IsItemized) { d.IsItemized = true; Acomodo = true; }
                    foreach (var c in calculadas) if (SubirPrecision(c)) Preciso = true;

                    try { tabla.RefreshData(); } catch { }
                    return Exportar(tabla);          // devuelve solo cadenas
                }
                catch (Exception ex)
                {
                    // No se pudo acomodar: modelo con trabajo compartido, modo de solo
                    // lectura, u otro comando con una transaccion abierta.
                    Acomodo = false; Preciso = false;

                    if (!detallada)
                    {
                        motivo = "la tabla no esta detallada y no se pudo acomodar temporalmente ("
                               + Raiz(ex) + ")";
                        return null;
                    }
                    try { return Exportar(tabla); }   // ya estaba detallada: alcanza con leerla
                    catch (Exception ex2) { motivo = "no se pudo exportar la tabla: " + Raiz(ex2); return null; }
                }
                finally
                {
                    try { if (abierta && t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                }
            }
        }

        /// <summary>Sube la precision de una columna numerica y le saca los adornos.</summary>
        bool SubirPrecision(Columna c)
        {
            try
            {
                var spec = c.Campo.GetSpecTypeId();
                if (spec == null || spec.Empty() || !UnitUtils.IsMeasurableSpec(spec)) return false;

                var fo = c.Campo.GetFormatOptions();
                if (fo == null) return false;

                if (fo.UseDefault)
                {
                    fo.UseDefault = false;
                    var u = _doc.GetUnits().GetFormatOptions(spec).GetUnitTypeId();
                    var devuelto = fo.SetUnitTypeId(u);
                    if (devuelto != null) fo = devuelto;
                }

                bool subida = false;
                foreach (double a in new[] { 0.000000001, 0.00000001, 0.000001, 0.0001 })
                {
                    if (!fo.IsValidAccuracy(a)) continue;
                    fo.Accuracy = a; subida = true; break;
                }

                fo.UseDigitGrouping = false;          // sin separador de miles: se puede parsear
                try { fo.SetSymbolTypeId(new ForgeTypeId()); } catch { }   // sin "m3" pegado

                c.Campo.SetFormatOptions(fo);
                return subida;
            }
            catch { return false; }
        }

        /// <summary>
        /// Pide la tabla sin titulo, sin cabeceras y sin filas de agrupacion ni totales:
        /// lo que vuelve son exactamente las filas de datos.
        /// </summary>
        List<string[]> Exportar(ViewSchedule tabla)
        {
            string carpeta = Path.Combine(Path.GetTempPath(), "ExportarTablas", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(carpeta);
            try
            {
                var op = new ViewScheduleExportOptions
                {
                    Title = false,
                    ColumnHeaders = ExportColumnHeaders.None,
                    HeadersFootersBlanks = false,
                    FieldDelimiter = "\t",
                    TextQualifier = ExportTextQualifier.DoubleQuote
                };
                tabla.Export(carpeta, "espejo.txt", op);

                string ruta = Path.Combine(carpeta, "espejo.txt");
                if (!File.Exists(ruta)) throw new InvalidOperationException("Revit no genero el archivo.");

                var filas = new List<string[]>();
                foreach (var linea in File.ReadAllLines(ruta, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(linea)) continue;
                    var celdas = Partir(linea, '\t');
                    if (celdas.All(string.IsNullOrWhiteSpace)) continue;
                    filas.Add(celdas);
                }
                return filas;
            }
            finally
            {
                try { Directory.Delete(carpeta, true); } catch { }
            }
        }

        /// <summary>Parte una linea delimitada respetando las comillas dobles.</summary>
        static string[] Partir(string linea, char delim)
        {
            var r = new List<string>();
            var sb = new StringBuilder();
            bool entreComillas = false;

            for (int i = 0; i < linea.Length; i++)
            {
                char ch = linea[i];
                if (entreComillas)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < linea.Length && linea[i + 1] == '"') { sb.Append('"'); i++; }
                        else entreComillas = false;
                    }
                    else sb.Append(ch);
                }
                else if (ch == '"') entreComillas = true;
                else if (ch == delim) { r.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            r.Add(sb.ToString());
            return r.ToArray();
        }

        // ---------------- emparejamiento ----------------

        /// <summary>
        /// Para la fila i de la tabla, que indice de la lista de elementos le corresponde.
        /// No se supone: se verifica comparando las columnas de texto que tambien se leen
        /// del modelo.
        /// </summary>
        int[] Emparejar(List<string[]> filas, string[] firmaElemento, IList<Columna> deFirma, out string motivo)
        {
            motivo = null;
            int n = firmaElemento.Length;

            var firmaFila = new string[filas.Count];
            for (int i = 0; i < filas.Count; i++)
            {
                var partes = new List<string>(deFirma.Count);
                foreach (var c in deFirma)
                    partes.Add(c.IndiceRenderizado < filas[i].Length
                        ? Normalizar(filas[i][c.IndiceRenderizado]) : "");
                firmaFila[i] = string.Join("", partes);
            }

            // Caso normal: la tabla muestra los elementos en el mismo orden que el colector.
            bool porPosicion = true;
            for (int i = 0; i < n; i++)
                if (!string.Equals(firmaFila[i], firmaElemento[i], StringComparison.Ordinal)) { porPosicion = false; break; }
            if (porPosicion) return Enumerable.Range(0, n).ToArray();

            // Si no, se emparejan por firma. Dos elementos con la misma firma son
            // intercambiables: su columna calculada da lo mismo.
            var porFirma = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
            for (int k = 0; k < n; k++)
            {
                Queue<int> q;
                if (!porFirma.TryGetValue(firmaElemento[k], out q)) { q = new Queue<int>(); porFirma[firmaElemento[k]] = q; }
                q.Enqueue(k);
            }

            var orden = new int[filas.Count];
            for (int i = 0; i < filas.Count; i++)
            {
                Queue<int> q;
                if (!porFirma.TryGetValue(firmaFila[i], out q) || q.Count == 0)
                {
                    motivo = "las filas de la tabla no coinciden con los elementos del modelo: "
                           + "no se puede saber a cual corresponde cada valor";
                    return null;
                }
                orden[i] = q.Dequeue();
            }
            return orden;
        }

        /// <summary>Firma de un elemento: solo los valores de texto, que se comparan exacto.</summary>
        string Firma(Element e, IList<Columna> columnas)
        {
            if (columnas.Count == 0) return "";
            var valores = _lector.Fila(e, columnas);
            var partes = new List<string>(valores.Length);
            foreach (var v in valores)
                partes.Add(v is string ? Normalizar((string)v) : "");   // los numeros vienen redondeados: no sirven
            return string.Join("", partes);
        }

        static string Normalizar(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
        }

        // ---------------- valores ----------------

        NumberFormatInfo FormatoNumerico()
        {
            var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            try
            {
                bool coma = _doc.GetUnits().DecimalSymbol == DecimalSymbol.Comma;
                nfi.NumberDecimalSeparator = coma ? "," : ".";
                nfi.NumberGroupSeparator = coma ? "." : ",";
            }
            catch { }
            return nfi;
        }

        static object Interpretar(string texto, NumberFormatInfo nfi)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            string s = texto.Trim();

            double v;
            if (double.TryParse(s, NumberStyles.Any, nfi, out v)) return v;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;

            // Puede quedar algun simbolo pegado pese a habersele pedido que no.
            var limpio = new string(s.Where(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-' || c == '+').ToArray());
            if (limpio.Length > 0 && double.TryParse(limpio, NumberStyles.Any, nfi, out v)) return v;

            return s;
        }

        static string Raiz(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.Message.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
