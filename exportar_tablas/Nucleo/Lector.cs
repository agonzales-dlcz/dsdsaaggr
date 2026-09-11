// Lee una tabla de planificacion elemento por elemento.
//
// No se usa ViewSchedule.Export ni el texto de las celdas: eso devuelve lo que la tabla
// MUESTRA, o sea "<varia>" en las tablas no detalladas y numeros ya redondeados. Aca se
// rearma el conjunto de elementos y se leen los parametros del modelo, con toda su
// precision, convertidos a la unidad que la tabla usa para mostrar.
//
// Una columna tiene que saber resolverse en CUALQUIER documento, no solo en el anfitrion:
// si la tabla incluye elementos de vinculos, el mismo parametro hay que pedirlo en cada
// documento vinculado, donde tiene otro ElementId. Ver Columna.Pedir.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace Riga.ExportarTablas.Nucleo
{
    internal enum Clase
    {
        Parametro,      // parametro de instancia o de tipo: se lee del elemento
        Recuento,       // Count: en una exportacion detallada es 1 por elemento
        Combinado,      // varios parametros concatenados en una sola columna
        Calculado,      // formula o porcentaje: Revit no expone la expresion por API
        NoSoportado
    }

    /// <summary>Un elemento tal como aparece en la tabla, con el modelo del que salio.</summary>
    internal sealed class Aparicion
    {
        public Element E;
        public string Modelo = "";
        public bool EsVinculo;
    }

    internal sealed class Columna
    {
        public string Nombre = "";
        public string Cabecera = "";
        public Clase Clase = Clase.NoSoportado;
        public bool EsDeTipo;                       // el parametro vive en el tipo
        public ElementId ParametroId;
        public Unidad Unidad;
        public ScheduleField Campo;
        public bool Oculta;                         // oculta en la tabla, pero existe

        /// <summary>
        /// En que columna cae esta cabecera cuando la tabla se renderiza, o -1 si esta
        /// oculta. Es un entero a proposito: sobrevive a una transaccion revertida, cosa
        /// que los objetos de la API no hacen.
        /// </summary>
        public int IndiceRenderizado = -1;
        public string Nota = "";                    // por que queda vacia, si queda vacia
        public bool Elegida = true;                 // la marca el usuario en la ventana

        // Como pedirle el parametro al elemento, resuelto una sola vez por tabla.
        // Recorrer element.Parameters entero por cada fila cuesta el doble: medido.
        public bool UsaBip;
        public BuiltInParameter Bip;
        public Definition Def;                      // definicion en el documento anfitrion
        public Guid Compartido = Guid.Empty;        // vacio si no es un parametro compartido

        // Definiciones ya resueltas en documentos vinculados, por ruta de documento.
        readonly Dictionary<string, Definition> _porDoc = new Dictionary<string, Definition>();

        /// <summary>
        /// El parametro de este elemento, venga del modelo que venga.
        ///
        /// Los BuiltInParameter valen igual en todos los documentos. Un compartido se
        /// reencuentra por GUID, que es justamente para lo que existe. Uno de proyecto solo
        /// existe en su modelo, asi que en un vinculo se busca por nombre y puede no estar.
        /// </summary>
        public Parameter Pedir(Element portador, Document anfitrion)
        {
            if (portador == null) return null;
            try
            {
                if (UsaBip) return portador.get_Parameter(Bip);

                var doc = portador.Document;
                if (doc != null && anfitrion != null && doc.Equals(anfitrion))
                    return Def == null ? null : portador.get_Parameter(Def);

                var def = DefinicionEn(doc);
                if (def != null) return portador.get_Parameter(def);

                return string.IsNullOrEmpty(Nombre) ? null : portador.LookupParameter(Nombre);
            }
            catch { return null; }
        }

        Definition DefinicionEn(Document doc)
        {
            if (doc == null) return null;
            string llave = doc.PathName ?? doc.Title ?? "";

            Definition d;
            if (_porDoc.TryGetValue(llave, out d)) return d;

            d = null;
            if (Compartido != Guid.Empty)
            {
                try
                {
                    var sp = SharedParameterElement.Lookup(doc, Compartido);
                    if (sp != null) d = sp.GetDefinition();
                }
                catch { }
            }
            _porDoc[llave] = d;
            return d;
        }
    }

    internal sealed class Lector
    {
        readonly Document _doc;

        public Lector(Document doc) { _doc = doc; }

        public Document Documento { get { return _doc; } }

        // ---------------- columnas ----------------

        /// <summary>Las columnas de la tabla, clasificadas, con unidad y acceso resueltos.</summary>
        public List<Columna> Columnas(ViewSchedule tabla)
        {
            var r = new List<Columna>();
            ScheduleDefinition d;
            try { d = tabla.Definition; } catch { return r; }

            for (int i = 0; i < d.GetFieldCount(); i++)
            {
                ScheduleField f;
                try { f = d.GetField(i); } catch { continue; }
                var col = ColumnaDe(f);
                if (col != null) r.Add(col);
            }

            // La columna renderizada de un campo es su posicion entre los campos visibles.
            int visible = 0;
            foreach (var c in r) c.IndiceRenderizado = c.Oculta ? -1 : visible++;

            return r;
        }

        /// <summary>Clasifica un campo suelto. Lo usa tambien el espejo, para la ordenacion.</summary>
        public Columna ColumnaDe(ScheduleField f)
        {
            if (f == null) return null;
            {
                var c = new Columna { Campo = f, ParametroId = f.ParameterId };
                try { c.Nombre = f.GetName() ?? ""; } catch { c.Nombre = "campo"; }
                try { c.Oculta = f.IsHidden; } catch { }
                c.Unidad = Unidades.DeCampo(_doc, f);
                c.Cabecera = Unidades.Cabecera(c.Nombre, c.Unidad);

                switch (f.FieldType)
                {
                    case ScheduleFieldType.Instance:
                    case ScheduleFieldType.ViewBased:
                    case ScheduleFieldType.PhysicalInstance:
                        c.Clase = Clase.Parametro; c.EsDeTipo = false;
                        break;

                    case ScheduleFieldType.ElementType:
                    case ScheduleFieldType.PhysicalType:
                        c.Clase = Clase.Parametro; c.EsDeTipo = true;
                        break;

                    case ScheduleFieldType.Count:
                    case ScheduleFieldType.HostCount:
                        c.Clase = Clase.Recuento;
                        break;

                    case ScheduleFieldType.CombinedParameter:
                        c.Clase = Clase.Combinado;
                        break;

                    case ScheduleFieldType.Formula:
                    case ScheduleFieldType.Percentage:
                        c.Clase = Clase.Calculado;
                        c.Nota = "campo calculado: Revit no expone la formula por API, la columna va vacia";
                        break;

                    default:
                        c.Clase = Clase.NoSoportado;
                        c.Nota = "tipo de campo " + f.FieldType + ": todavia no soportado, la columna va vacia";
                        break;
                }

                if (c.Clase == Clase.Parametro) Resolver(c);
                return c;
            }
        }

        /// <summary>
        /// Deja listo como pedir el parametro. Los ids negativos son BuiltInParameter; los
        /// positivos son de proyecto o compartidos y hay que sacarles la definicion.
        /// </summary>
        void Resolver(Columna c)
        {
            if (c.ParametroId == null) return;
            long v = c.ParametroId.Value;

            if (v < 0)
            {
                try
                {
                    var b = (BuiltInParameter)v;
                    if (Enum.IsDefined(typeof(BuiltInParameter), b)) { c.Bip = b; c.UsaBip = true; }
                }
                catch { }
                return;
            }

            try
            {
                var pe = _doc.GetElement(c.ParametroId) as ParameterElement;
                if (pe != null) c.Def = pe.GetDefinition();

                var sp = pe as SharedParameterElement;
                if (sp != null) c.Compartido = sp.GuidValue;
            }
            catch { }
        }

        // ---------------- elementos ----------------

        /// <summary>
        /// Los elementos que la tabla muestra, del anfitrion y de los vinculos.
        ///
        /// El colector acotado a la vista de la tabla ya aplica su categoria, sus filtros y
        /// su fase: no hace falta reimplementarlos, y asi no se puede desincronizar.
        /// Verificado: cuatro tablas de la misma categoria con distintos filtros devuelven
        /// 30, 0, 10 y 5 elementos.
        ///
        /// La primera llamada obliga a Revit a calcular la tabla (segundos); la segunda
        /// sale de la cache y es instantanea. Tambien medido.
        ///
        /// Un colector NUNCA devuelve elementos de vinculos, asi que esos hay que ir a
        /// buscarlos documento por documento: de eso se ocupa Vinculos.
        /// </summary>
        public IList<Aparicion> Elementos(ViewSchedule tabla, string modelo, IList<string> avisos)
        {
            var r = new List<Aparicion>();

            bool conVinculos = false;
            ElementId categoria = null;
            try
            {
                var d = tabla.Definition;
                conVinculos = d != null && d.IncludeLinkedFiles;
                if (d != null) categoria = d.CategoryId;
            }
            catch { }

            try
            {
                var col = new FilteredElementCollector(_doc, tabla.Id).WhereElementIsNotElementType();

                // Cuando la tabla incluye elementos de vinculos, el colector acotado a la
                // vista devuelve las INSTANCIAS DE VINCULO, no los elementos de la tabla.
                // Medido: en la tabla EPACIOS devolvia 39 RevitLinkInstance mientras el
                // modelo no tiene ni una habitacion propia. Acotar a la categoria de la
                // tabla los saca. Solo se hace en este caso, para no tocar las demas.
                if (conVinculos && categoria != null && categoria != ElementId.InvalidElementId)
                    col = col.OfCategoryId(categoria);

                foreach (var e in col.ToElements())
                    r.Add(new Aparicion { E = e, Modelo = modelo, EsVinculo = false });
            }
            catch { }

            try { Vinculos.Agregar(_doc, this, tabla, r, avisos); }
            catch (Exception ex)
            {
                if (avisos != null)
                    avisos.Add("no se pudieron leer los elementos de vinculos: " + ex.Message);
            }

            return r;
        }

        // ---------------- filas ----------------

        /// <summary>Los valores de un elemento para las columnas dadas, en ese orden.</summary>
        public object[] Fila(Element e, IList<Columna> columnas)
        {
            var doc = e == null ? _doc : e.Document;

            Element tipo = null;
            bool tipoBuscado = false;
            Func<Element> elTipo = () =>
            {
                if (!tipoBuscado)
                {
                    tipoBuscado = true;
                    try
                    {
                        var ti = e.GetTypeId();
                        tipo = (ti == null || ti == ElementId.InvalidElementId) ? null : doc.GetElement(ti);
                    }
                    catch { }
                }
                return tipo;
            };

            var fila = new object[columnas.Count];
            for (int i = 0; i < columnas.Count; i++)
            {
                var c = columnas[i];
                fila[i] = c.EsDeTipo ? Valor(c, elTipo(), e) : Valor(c, e, elTipo());
            }
            return fila;
        }

        /// <param name="portador">Donde dice el campo que vive el parametro.</param>
        /// <param name="alterno">
        /// El otro: la instancia si el campo dice tipo, y al reves. Un parametro compartido
        /// puede estar declarado de instancia y tener el valor cargado en el tipo; medido en
        /// el modelo real, donde 11 de 32 elementos lo tenian asi. Solo se usa para rellenar
        /// lo que de otro modo quedaria vacio, nunca para pisar un valor.
        /// </param>
        public object Valor(Columna c, Element portador, Element alterno)
        {
            switch (c.Clase)
            {
                case Clase.Recuento:
                    return 1;                       // detallado por elemento: cada fila es uno

                case Clase.Combinado:
                    return Combinado(c, portador) ?? Combinado(c, alterno);

                case Clase.Calculado:
                case Clase.NoSoportado:
                    return null;

                default:
                    var p = c.Pedir(portador, _doc);
                    if (p == null || !p.HasValue)
                    {
                        var otro = c.Pedir(alterno, _doc);
                        if (otro != null && otro.HasValue) p = otro;
                    }
                    return Desde(p, c.Unidad);
            }
        }

        /// <summary>El valor crudo, en unidades internas: lo que necesitan los filtros.</summary>
        public Parameter Crudo(Columna c, Element e)
        {
            if (c == null || e == null) return null;
            Element tipo = null;
            try
            {
                var ti = e.GetTypeId();
                if (ti != null && ti != ElementId.InvalidElementId) tipo = e.Document.GetElement(ti);
            }
            catch { }

            var portador = c.EsDeTipo ? tipo : e;
            var alterno = c.EsDeTipo ? e : tipo;

            var p = c.Pedir(portador, _doc);
            if (p == null || !p.HasValue)
            {
                var otro = c.Pedir(alterno, _doc);
                if (otro != null && otro.HasValue) p = otro;
            }
            return p;
        }

        public object Desde(Parameter p, Unidad unidad)
        {
            if (p == null || !p.HasValue) return null;

            switch (p.StorageType)
            {
                case StorageType.Double:
                    double interno = p.AsDouble();
                    // Si el campo no declaraba especificacion, se la pedimos al parametro.
                    var u = (unidad != null && unidad.Convierte) ? unidad : Unidades.DeParametro(_doc, p);
                    return u.Convertir(interno);

                case StorageType.Integer:
                    if (EsSiNo(p)) return p.AsInteger() != 0;
                    return p.AsInteger();

                case StorageType.String:
                    return p.AsString();

                case StorageType.ElementId:
                    try
                    {
                        var doc = p.Element == null ? _doc : p.Element.Document;
                        var e = doc.GetElement(p.AsElementId());
                        if (e != null) return e.Name;
                    }
                    catch { }
                    return p.AsValueString();
            }
            return null;
        }

        static bool EsSiNo(Parameter p)
        {
            try { return p.Definition.GetDataType().Equals(SpecTypeId.Boolean.YesNo); }
            catch { return false; }
        }

        string Combinado(Columna c, Element portador)
        {
            IList<TableCellCombinedParameterData> partes;
            try { partes = c.Campo.GetCombinedParameters(); }
            catch { return null; }
            if (partes == null || partes.Count == 0 || portador == null) return null;

            var doc = portador.Document ?? _doc;
            var sb = new StringBuilder();
            foreach (var parte in partes)
            {
                Parameter p = null;
                try
                {
                    long v = parte.ParamId == null ? 0 : parte.ParamId.Value;
                    if (v < 0)
                    {
                        var b = (BuiltInParameter)v;
                        if (Enum.IsDefined(typeof(BuiltInParameter), b)) p = portador.get_Parameter(b);
                    }
                    else if (v > 0)
                    {
                        var pe = _doc.GetElement(parte.ParamId) as ParameterElement;
                        if (pe != null)
                        {
                            var sp = pe as SharedParameterElement;
                            if (sp != null && !doc.Equals(_doc))
                            {
                                var enVinculo = SharedParameterElement.Lookup(doc, sp.GuidValue);
                                if (enVinculo != null) p = portador.get_Parameter(enVinculo.GetDefinition());
                            }
                            if (p == null) p = portador.get_Parameter(pe.GetDefinition());
                        }
                    }
                }
                catch { }

                object v2 = Desde(p, null);
                string texto = v2 == null ? "" : Convert.ToString(v2, System.Globalization.CultureInfo.InvariantCulture);
                if (texto.Length == 0) continue;

                if (sb.Length > 0) sb.Append(parte.Separator ?? "");
                sb.Append(parte.Prefix ?? "").Append(texto).Append(parte.Suffix ?? "");
            }
            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}
