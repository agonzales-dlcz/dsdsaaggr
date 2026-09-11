// Los elementos de los modelos vinculados que la tabla muestra.
//
// POR QUE HACE FALTA ESTO
//
// Cuando una tabla tiene marcado "Incluir elementos de vinculos", Revit dibuja tambien las
// filas de los modelos vinculados. Un FilteredElementCollector NO: solo devuelve elementos
// de su propio documento. Medido en el modelo de IREN: la tabla EPACIOS renderiza 772 filas
// y el colector del anfitrion devuelve 39. Sin esto se exportaba el 5% sin avisar.
//
// LO QUE SE PUEDE Y LO QUE NO
//
// Del otro lado no hay vista de tabla a la que acotar el colector, asi que hay que
// reproducir a mano lo que la tabla hace: su categoria y sus filtros. Los filtros de la API
// (ScheduleFilter) se aplican aca uno por uno.
//
// Lo que NO se reproduce queda anotado, nunca callado:
//   * la fase de la vista;
//   * los filtros contra parametros globales, que la API expone como tipo pero no como
//     valor resoluble desde afuera;
//   * las tablas multicategoria, donde CategoryId no dice una categoria concreta.
//
// Ademas, al final, el que llama compara cuantas filas salieron contra cuantas dibuja
// Revit. Si no coinciden, eso tambien se anota. Es la red de seguridad: aunque un filtro se
// aplique mal, el Excel lo dice.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace Riga.ExportarTablas.Nucleo
{
    internal static class Vinculos
    {
        public static void Agregar(Document doc, Lector lector, ViewSchedule tabla,
                                   List<Aparicion> destino, IList<string> avisos)
        {
            ScheduleDefinition d;
            try { d = tabla.Definition; } catch { return; }
            if (d == null) return;

            bool incluye;
            try { incluye = d.IncludeLinkedFiles; } catch { return; }
            if (!incluye) return;

            var categoria = Categoria(d, avisos);

            // Las columnas se reusan para evaluar filtros: ya saben resolverse en cualquier
            // documento, que es justo lo que hace falta aca.
            var columnas = lector.Columnas(tabla);
            var filtros = Filtros(d, columnas, avisos);

            var instancias = new FilteredElementCollector(doc)
                                 .OfClass(typeof(RevitLinkInstance))
                                 .Cast<RevitLinkInstance>()
                                 .ToList();

            if (instancias.Count == 0)
            {
                Anotar(avisos, "la tabla incluye elementos de vinculos pero el modelo no tiene ninguno");
                return;
            }

            var vistos = new HashSet<string>();      // un mismo vinculo puede estar dos veces
            int descargados = 0;

            foreach (var li in instancias)
            {
                Document dv = null;
                try { dv = li.GetLinkDocument(); } catch { }
                if (dv == null) { descargados++; continue; }

                string llave = dv.PathName ?? dv.Title ?? "";
                if (!vistos.Add(llave)) continue;     // dos instancias del mismo archivo

                string nombre = SinExtension(dv.Title);

                IEnumerable<Element> candidatos;
                try
                {
                    var col = new FilteredElementCollector(dv).WhereElementIsNotElementType();
                    if (categoria != null) col = col.OfCategoryId(categoria);
                    candidatos = col.ToElements();
                }
                catch (Exception ex)
                {
                    Anotar(avisos, "no se pudo recorrer el vinculo " + nombre + ": " + ex.Message);
                    continue;
                }

                foreach (var e in candidatos)
                {
                    if (!Pasa(lector, e, filtros)) continue;
                    destino.Add(new Aparicion { E = e, Modelo = nombre, EsVinculo = true });
                }
            }

            if (descargados > 0)
                Anotar(avisos, descargados + " vinculo(s) descargado(s): sus elementos no entraron");
        }

        // ---------------- categoria ----------------

        static ElementId Categoria(ScheduleDefinition d, IList<string> avisos)
        {
            ElementId id = null;
            try { id = d.CategoryId; } catch { }

            if (id == null || id == ElementId.InvalidElementId)
            {
                Anotar(avisos, "la tabla no declara una categoria concreta (multicategoria): "
                             + "de los vinculos entran todos los elementos que pasen los filtros");
                return null;
            }

            if (id.Value == (long)BuiltInCategory.OST_MultiCategoryTags
                || id.Value == (long)BuiltInCategory.OST_IOSModelGroups)
            {
                Anotar(avisos, "categoria multiple: de los vinculos entran todos los elementos "
                             + "que pasen los filtros");
                return null;
            }

            return id;
        }

        // ---------------- filtros ----------------

        sealed class Filtro
        {
            public Columna Col;
            public ScheduleFilterType Tipo;
            public string Texto;
            public double Numero;
            public int Entero;
            public ElementId Id;
            public bool EsTexto, EsNumero, EsEntero, EsId, EsNulo;
        }

        static List<Filtro> Filtros(ScheduleDefinition d, List<Columna> columnas, IList<string> avisos)
        {
            var r = new List<Filtro>();
            IList<ScheduleFilter> crudos;
            try { crudos = d.GetFilters(); } catch { return r; }
            if (crudos == null) return r;

            foreach (var f in crudos)
            {
                int idx;
                try { idx = d.GetFieldIndex(f.FieldId); } catch { idx = -1; }
                if (idx < 0 || idx >= columnas.Count)
                {
                    Anotar(avisos, "un filtro de la tabla apunta a un campo que no se pudo ubicar: "
                                 + "puede que entren filas de mas desde los vinculos");
                    continue;
                }

                if (f.FilterType == ScheduleFilterType.IsAssociatedWithGlobalParameter
                    || f.FilterType == ScheduleFilterType.IsNotAssociatedWithGlobalParameter)
                {
                    Anotar(avisos, "filtro por parametro global en \"" + columnas[idx].Nombre
                                 + "\": no se puede evaluar fuera del anfitrion, se ignoro");
                    continue;
                }

                var nf = new Filtro { Col = columnas[idx], Tipo = f.FilterType };
                try
                {
                    if (f.IsStringValue) { nf.EsTexto = true; nf.Texto = f.GetStringValue() ?? ""; }
                    else if (f.IsDoubleValue) { nf.EsNumero = true; nf.Numero = f.GetDoubleValue(); }
                    else if (f.IsIntegerValue) { nf.EsEntero = true; nf.Entero = f.GetIntegerValue(); }
                    else if (f.IsElementIdValue) { nf.EsId = true; nf.Id = f.GetElementIdValue(); }
                    else nf.EsNulo = true;
                }
                catch { nf.EsNulo = true; }

                r.Add(nf);
            }
            return r;
        }

        static bool Pasa(Lector lector, Element e, List<Filtro> filtros)
        {
            foreach (var f in filtros)
                if (!PasaUno(lector, e, f)) return false;
            return true;
        }

        static bool PasaUno(Lector lector, Element e, Filtro f)
        {
            var p = lector.Crudo(f.Col, e);

            switch (f.Tipo)
            {
                case ScheduleFilterType.HasParameter: return p != null;
                case ScheduleFilterType.HasValue: return p != null && p.HasValue;
                case ScheduleFilterType.HasNoValue: return p == null || !p.HasValue;
            }

            if (p == null || !p.HasValue) return false;

            // Comparacion de texto: contra lo que el parametro vale como texto, igual que
            // hace la tabla.
            if (f.EsTexto)
            {
                string v = Texto(lector, p);
                string b = f.Texto ?? "";
                switch (f.Tipo)
                {
                    case ScheduleFilterType.Equal: return Igual(v, b);
                    case ScheduleFilterType.NotEqual: return !Igual(v, b);
                    case ScheduleFilterType.Contains: return Indice(v, b) >= 0;
                    case ScheduleFilterType.NotContains: return Indice(v, b) < 0;
                    case ScheduleFilterType.BeginsWith: return v.StartsWith(b, Cmp);
                    case ScheduleFilterType.NotBeginsWith: return !v.StartsWith(b, Cmp);
                    case ScheduleFilterType.EndsWith: return v.EndsWith(b, Cmp);
                    case ScheduleFilterType.NotEndsWith: return !v.EndsWith(b, Cmp);
                    case ScheduleFilterType.GreaterThan: return string.Compare(v, b, Cmp) > 0;
                    case ScheduleFilterType.GreaterThanOrEqual: return string.Compare(v, b, Cmp) >= 0;
                    case ScheduleFilterType.LessThan: return string.Compare(v, b, Cmp) < 0;
                    case ScheduleFilterType.LessThanOrEqual: return string.Compare(v, b, Cmp) <= 0;
                }
                return true;
            }

            if (f.EsId)
            {
                ElementId v = null;
                try { if (p.StorageType == StorageType.ElementId) v = p.AsElementId(); } catch { }
                bool igual = v != null && f.Id != null && v.Value == f.Id.Value;
                if (f.Tipo == ScheduleFilterType.Equal) return igual;
                if (f.Tipo == ScheduleFilterType.NotEqual) return !igual;
                return true;
            }

            // Numerico. El valor del filtro viene en unidades internas, igual que AsDouble.
            double n;
            if (f.EsNumero) n = f.Numero;
            else if (f.EsEntero) n = f.Entero;
            else return true;

            double x;
            try
            {
                if (p.StorageType == StorageType.Double) x = p.AsDouble();
                else if (p.StorageType == StorageType.Integer) x = p.AsInteger();
                else return true;
            }
            catch { return true; }

            const double EPS = 1e-9;
            switch (f.Tipo)
            {
                case ScheduleFilterType.Equal: return Math.Abs(x - n) <= EPS;
                case ScheduleFilterType.NotEqual: return Math.Abs(x - n) > EPS;
                case ScheduleFilterType.GreaterThan: return x > n + EPS;
                case ScheduleFilterType.GreaterThanOrEqual: return x >= n - EPS;
                case ScheduleFilterType.LessThan: return x < n - EPS;
                case ScheduleFilterType.LessThanOrEqual: return x <= n + EPS;
            }
            return true;
        }

        const StringComparison Cmp = StringComparison.CurrentCultureIgnoreCase;

        static bool Igual(string a, string b) { return string.Compare(a, b, Cmp) == 0; }
        static int Indice(string a, string b)
        {
            if (b.Length == 0) return 0;
            return a.IndexOf(b, Cmp);
        }

        static string Texto(Lector lector, Parameter p)
        {
            try
            {
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                var v = lector.Desde(p, null);
                if (v == null) return "";
                return Convert.ToString(v, CultureInfo.CurrentCulture) ?? "";
            }
            catch { return ""; }
        }

        // ---------------- varios ----------------

        static void Anotar(IList<string> avisos, string texto)
        {
            if (avisos != null && !avisos.Contains(texto)) avisos.Add(texto);
        }

        static string SinExtension(string titulo)
        {
            if (string.IsNullOrEmpty(titulo)) return "";
            return titulo.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
                ? titulo.Substring(0, titulo.Length - 4)
                : titulo;
        }
    }
}
