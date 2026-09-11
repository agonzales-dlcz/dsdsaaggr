// Arma el libro de Excel: una hoja por tabla, una hoja consolidada con todo apilado y
// una hoja de notas con lo que no se pudo resolver.
//
// Toda hoja arranca con las mismas tres columnas de identificacion: modelo, GUID y
// Element ID. Sin eso una fila del Excel no se puede volver a encontrar en el modelo.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Riga.ExportarTablas.Nucleo
{
    internal sealed class Opciones
    {
        public string Ruta = "";
        public bool HojaConsolidada = true;
        public bool ColumnasOcultas = false;     // las que la tabla oculta, pero existen
        public string NombreConsolidada = "Consolidado";

        /// <summary>Nombres de columna que el usuario destildo en la ventana.</summary>
        public HashSet<string> ColumnasDescartadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Completar las columnas de formula leyendo lo que dibuja la tabla.</summary>
        public bool CompletarCalculadas = true;
    }

    internal sealed class Nota
    {
        public string Tabla, Columna, Motivo;
    }

    internal sealed class Exportador
    {
        readonly Document _doc;
        readonly Lector _lector;
        readonly string _modelo;

        public readonly List<Nota> Notas = new List<Nota>();
        public int TablasHechas, FilasTotal;

        public Exportador(Document doc)
        {
            _doc = doc;
            _lector = new Lector(doc);
            _modelo = SinExtension(doc.Title);
        }

        static string SinExtension(string titulo)
        {
            if (string.IsNullOrEmpty(titulo)) return "";
            return titulo.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
                ? titulo.Substring(0, titulo.Length - 4)
                : titulo;
        }

        /// <param name="avance">(indice base 0, nombre de la tabla) antes de cada tabla.</param>
        /// <param name="cancelado">se consulta entre tablas.</param>
        public void Correr(IList<ViewSchedule> tablas, Opciones op,
                           Action<int, string> avance, Func<bool> cancelado)
        {
            var hojas = new List<Xlsx.Hoja>();
            var consolidada = new Consolidada();

            for (int i = 0; i < tablas.Count; i++)
            {
                if (cancelado != null && cancelado()) break;

                var t = tablas[i];
                if (avance != null) avance(i, t.Name);

                try
                {
                    hojas.Add(Hoja(t, op, consolidada));
                    TablasHechas++;
                }
                catch (Exception ex)
                {
                    Notas.Add(new Nota { Tabla = t.Name, Columna = "(toda la tabla)", Motivo = Raiz(ex) });
                }
            }

            if (op.HojaConsolidada && consolidada.Filas.Count > 0)
                hojas.Add(consolidada.AHoja(op.NombreConsolidada));

            if (Notas.Count > 0) hojas.Add(HojaNotas());

            if (hojas.Count == 0)
                throw new InvalidOperationException("No se pudo armar ninguna hoja.");

            Xlsx.Escribir(op.Ruta, hojas);
        }

        // ---------------- una tabla ----------------

        Xlsx.Hoja Hoja(ViewSchedule tabla, Opciones op, Consolidada consolidada)
        {
            var columnas = _lector.Columnas(tabla);

            foreach (var c in columnas)
            {
                if (!op.ColumnasOcultas && c.Oculta) c.Elegida = false;   // solo las visibles
                if (op.ColumnasDescartadas != null && op.ColumnasDescartadas.Contains(c.Nombre)) c.Elegida = false;
            }

            var elegidas = columnas.Where(c => c.Elegida).ToList();

            var avisos = new List<string>();
            var apariciones = _lector.Elementos(tabla, _modelo, avisos);
            var elementos = apariciones.Select(a => a.E).ToList();
            int deVinculos = apariciones.Count(a => a.EsVinculo);

            foreach (var a in avisos)
                Notas.Add(new Nota { Tabla = tabla.Name, Columna = "(vinculos)", Motivo = a });

            if (deVinculos > 0) Contrastar(tabla, apariciones.Count, deVinculos);

            // Las columnas de formula no se pueden leer del elemento: se completan con lo
            // que dibuja la tabla, si se puede emparejar fila con elemento sin adivinar.
            Dictionary<Columna, object[]> espejo = null;
            string motivoEspejo = null;
            Espejo lector2 = null;
            if (op.CompletarCalculadas && elegidas.Any(c => c.Clase == Clase.Calculado))
            {
                // Con elementos de vinculos el espejo no sirve: empareja fila renderizada
                // con elemento por posicion, y las filas de los vinculos se intercalan en
                // un orden que desde aca no se puede reproducir.
                if (deVinculos > 0)
                    motivoEspejo = "la tabla incluye elementos de vinculos y las filas ya no se "
                                 + "pueden emparejar por posicion";
                else
                {
                    lector2 = new Espejo(_doc, _lector);
                    try { espejo = lector2.Completar(tabla, elementos, elegidas, out motivoEspejo); }
                    catch (Exception ex) { motivoEspejo = "fallo al leer la tabla: " + Raiz(ex); }
                }
            }

            foreach (var c in elegidas)
            {
                if (c.Nota.Length == 0) continue;
                if (c.Clase == Clase.Calculado)
                {
                    if (espejo != null && espejo.ContainsKey(c))
                    {
                        Notas.Add(new Nota
                        {
                            Tabla = tabla.Name,
                            Columna = c.Nombre,
                            Motivo = "campo calculado: Revit no expone la formula, el valor sale de la tabla renderizada"
                                   + (lector2 != null && lector2.Acomodo ? "; se detallo temporalmente y se revirtio" : "")
                                   + (lector2 != null && lector2.Preciso
                                        ? "; con precision ampliada"
                                        : "; con el redondeo que muestra la tabla")
                        });
                        continue;
                    }
                    Notas.Add(new Nota
                    {
                        Tabla = tabla.Name,
                        Columna = c.Nombre,
                        Motivo = motivoEspejo != null
                            ? "campo calculado y ademas " + motivoEspejo + ": la columna va vacia"
                            : c.Nota
                    });
                    continue;
                }
                Notas.Add(new Nota { Tabla = tabla.Name, Columna = c.Nombre, Motivo = c.Nota });
            }

            var hoja = new Xlsx.Hoja { Nombre = tabla.Name };
            hoja.Cabeceras.Add(Identidad.Fija(Identidad.COL_MODELO, "modelo"));
            hoja.Cabeceras.Add(Identidad.Fija(Identidad.COL_GUID, "guid"));
            hoja.Cabeceras.Add(Identidad.Fija(Identidad.COL_ID, "id"));
            foreach (var c in elegidas) hoja.Cabeceras.Add(Identidad.Cabecera(_doc, c));

            if (op.HojaConsolidada) consolidada.Cabecera(hoja.Cabeceras);

            for (int k = 0; k < apariciones.Count; k++)
            {
                var ap = apariciones[k];
                var e = ap.E;
                var valores = _lector.Fila(e, elegidas);

                if (espejo != null)
                    foreach (var kv in espejo)
                    {
                        int idx = elegidas.IndexOf(kv.Key);
                        if (idx >= 0 && k < kv.Value.Length) valores[idx] = kv.Value[k];
                    }

                var fila = new object[3 + valores.Length];
                fila[0] = ap.Modelo;
                fila[1] = Guid(e);
                fila[2] = e.Id.Value;
                Array.Copy(valores, 0, fila, 3, valores.Length);

                hoja.Filas.Add(fila);
                FilasTotal++;

                if (op.HojaConsolidada) consolidada.Agregar(fila);
            }

            // Una columna vacia en TODAS las filas merece explicacion: si no, el hueco
            // parece un dato y no lo es. Esto es lo que evita el error silencioso.
            if (hoja.Filas.Count > 0)
                for (int i = 0; i < elegidas.Count; i++)
                {
                    var c = elegidas[i];
                    if (c.Clase == Clase.Calculado) continue;          // esas ya llevan su nota
                    bool alguno = false;
                    foreach (var f in hoja.Filas) if (f[3 + i] != null) { alguno = true; break; }
                    if (alguno) continue;

                    Notas.Add(new Nota
                    {
                        Tabla = tabla.Name,
                        Columna = c.Nombre,
                        Motivo = "quedo vacia: el parametro no tiene valor en ninguno de los "
                               + hoja.Filas.Count + " elementos, ni en la instancia ni en el tipo"
                    });
                }

            return hoja;
        }

        static string Guid(Element e)
        {
            try { return e.UniqueId; } catch { return ""; }
        }

        /// <summary>
        /// Red de seguridad para las tablas con elementos de vinculos: se comparan las filas
        /// exportadas contra las que Revit dibuja. Los filtros de los vinculos se aplican a
        /// mano (ver Vinculos), asi que este contraste es lo que impide que una diferencia
        /// pase inadvertida. No es un error: es un numero al lado del otro.
        /// </summary>
        void Contrastar(ViewSchedule tabla, int exportadas, int deVinculos)
        {
            int dibujadas = -1;
            try
            {
                var cuerpo = tabla.GetTableData().GetSectionData(SectionType.Body);
                dibujadas = cuerpo.NumberOfRows;
            }
            catch { }

            string texto = "la tabla incluye elementos de vinculos: se exportaron " + exportadas
                         + " filas (" + (exportadas - deVinculos) + " del anfitrion y "
                         + deVinculos + " de vinculos)";

            if (dibujadas >= 0)
                texto += "; Revit dibuja " + dibujadas + " filas de cuerpo, cabecera y totales "
                       + "incluidos. Si la diferencia es mayor que eso, algun filtro de la tabla "
                       + "no se pudo reproducir del lado del vinculo";

            Notas.Add(new Nota { Tabla = tabla.Name, Columna = "(vinculos)", Motivo = texto });
        }

        // ---------------- hoja de notas ----------------

        Xlsx.Hoja HojaNotas()
        {
            var h = new Xlsx.Hoja { Nombre = "Notas" };
            h.Cabeceras.AddRange(new[] { "Tabla", "Columna", "Motivo" });
            foreach (var n in Notas)
                h.Filas.Add(new object[] { n.Tabla, n.Columna, n.Motivo });
            return h;
        }

        static string Raiz(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.Message.Replace("\r", " ").Replace("\n", " ");
        }

        // ---------------- consolidada ----------------

        /// <summary>
        /// Todas las tablas apiladas, literal: una tabla tras otra, cada fila con el orden
        /// de columnas de su propia tabla. No se unifica por nombre de cabecera a
        /// proposito: hacerlo partia en dos columnas lo que fisicamente es la misma
        /// posicion, por ejemplo "Metrado (m3)" y "Volumen (m3)".
        ///
        /// La cabecera es la de la primera tabla exportada. Si las tablas no comparten
        /// disposicion, la cabecera solo describe al primer bloque; el GUID y el Element ID
        /// de cada fila alcanzan para saber de donde salio.
        /// </summary>
        sealed class Consolidada
        {
            readonly List<string> _cabecera = new List<string>();
            public readonly List<object[]> Filas = new List<object[]>();

            public void Cabecera(IList<string> cabecera)
            {
                if (_cabecera.Count == 0 && cabecera != null) _cabecera.AddRange(cabecera);
            }

            public void Agregar(object[] fila) { Filas.Add(fila); }

            public Xlsx.Hoja AHoja(string nombre)
            {
                int ancho = _cabecera.Count;
                foreach (var f in Filas) if (f.Length > ancho) ancho = f.Length;

                var h = new Xlsx.Hoja { Nombre = nombre };
                h.Cabeceras.AddRange(_cabecera);
                while (h.Cabeceras.Count < ancho) h.Cabeceras.Add("");

                // Se le cambia la chapa a la primera columna para que el importador sepa que
                // esta hoja no se puede devolver: ver Identidad.CLAVE_CONSOLIDADO.
                if (h.Cabeceras.Count > 0)
                    h.Cabeceras[0] = Identidad.Fija(Identidad.COL_MODELO, Identidad.CLAVE_CONSOLIDADO);

                foreach (var f in Filas)
                {
                    if (f.Length == ancho) { h.Filas.Add(f); continue; }
                    var r = new object[ancho];
                    Array.Copy(f, r, f.Length);
                    h.Filas.Add(r);
                }
                return h;
            }
        }
    }
}
