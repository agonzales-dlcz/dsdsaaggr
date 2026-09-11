// Vuelve del Excel al modelo.
//
// El camino de ida escribe en cada fila el GUID y el Element ID del elemento, y en cada
// cabecera la chapa del parametro (ver Identidad). Con esas dos cosas la vuelta no adivina
// nada: sabe exactamente que elemento y que parametro toca cada celda.
//
// LO QUE NO HACE, A PROPOSITO
//
//   * No escribe en elementos de vinculos. Un documento vinculado se abre de solo lectura;
//     esas filas se saltan con su motivo, no en silencio.
//   * No escribe en parametros de TIPO salvo que se pida expresamente. Cambiar un parametro
//     de tipo cambia todas las instancias de ese tipo: una fila del Excel puede terminar
//     modificando doscientos elementos. Va detras de una casilla, apagada.
//   * Una celda vacia no borra el valor, salvo que se pida. Lo normal es completar
//     columnas, no vaciarlas, y un Excel mal pegado no deberia poder limpiar el modelo.
//   * No toca parametros de solo lectura ni los que apuntan a otro elemento (nivel,
//     material...): de ida se exporto el NOMBRE del elemento, y volver de un nombre a un
//     elemento es ambiguo. Se anota y se salta.
//
// Primero se analiza todo sin transaccion y se muestra el resumen. Recien despues se
// escribe, de una, en una sola transaccion que se deshace con un Ctrl+Z.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace Riga.ExportarTablas.Nucleo
{
    internal sealed class OpcionesImportar
    {
        public string Ruta = "";
        public HashSet<string> Hojas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool PermitirTipo = false;
        public bool VaciarConCeldaVacia = false;
    }

    internal sealed class Cambio
    {
        public string Hoja = "";
        public int Fila;
        public Element E;
        public Chapa Ch;
        public string Columna = "";
        public string Antes = "";
        public string Despues = "";
        public object Valor;             // ya en unidades internas
        public bool EnTipo;
        public ElementId Destino;        // instancia o tipo, segun donde este el parametro
    }

    internal sealed class Salto
    {
        public string Hoja = "";
        public int Fila;
        public string Columna = "";
        public string Motivo = "";
    }

    internal sealed class Importador
    {
        readonly Document _doc;
        readonly string _modelo;

        public readonly List<Cambio> Cambios = new List<Cambio>();
        public readonly List<Salto> Saltos = new List<Salto>();
        public int Iguales, FilasLeidas, ElementosTocados;

        public Importador(Document doc)
        {
            _doc = doc;
            _modelo = SinExtension(doc.Title);
        }

        static string SinExtension(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            return t.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) ? t.Substring(0, t.Length - 4) : t;
        }

        // ---------------- analisis ----------------

        public void Analizar(IList<HojaLeida> hojas, OpcionesImportar op)
        {
            Cambios.Clear(); Saltos.Clear();
            Iguales = 0; FilasLeidas = 0;

            foreach (var h in hojas)
            {
                if (op.Hojas.Count > 0 && !op.Hojas.Contains(h.Nombre)) continue;
                if (h.Filas.Count < 2) continue;
                AnalizarHoja(h, op);
            }

            ElementosTocados = Cambios.Select(c => c.E.Id.Value).Distinct().Count();
        }

        void AnalizarHoja(HojaLeida h, OpcionesImportar op)
        {
            var cab = h.Cabecera;
            int cModelo = -1, cGuid = -1, cId = -1;
            var params_ = new Dictionary<int, Chapa>();

            for (int i = 0; i < cab.Length; i++)
            {
                var ch = Identidad.Leer(cab[i]);
                if (ch == null) continue;

                if (ch.Clase == ClaseClave.Fija)
                {
                    if (ch.Clave == Identidad.CLAVE_CONSOLIDADO)
                    {
                        Saltos.Add(new Salto
                        {
                            Hoja = h.Nombre,
                            Motivo = "es la hoja consolidada: apila tablas distintas bajo la cabecera "
                                   + "de la primera, asi que las filas de los demas bloques escribirian "
                                   + "en el parametro equivocado. Importa las hojas de cada tabla."
                        });
                        return;
                    }
                    if (ch.Clave == "modelo") cModelo = i;
                    else if (ch.Clave == "guid") cGuid = i;
                    else if (ch.Clave == "id") cId = i;
                    continue;
                }
                if (ch.Escribible) params_[i] = ch;
            }

            if (cGuid < 0 && cId < 0)
            {
                Saltos.Add(new Salto
                {
                    Hoja = h.Nombre,
                    Motivo = "la cabecera no tiene las columnas GUID ni Element ID con su chapa: "
                           + "esta hoja no salio de este exportador, o se le borro la fila de titulos"
                });
                return;
            }

            if (params_.Count == 0)
            {
                Saltos.Add(new Salto
                {
                    Hoja = h.Nombre,
                    Motivo = "ninguna columna lleva chapa de parametro escribible, no hay nada que importar"
                });
                return;
            }

            for (int f = 1; f < h.Filas.Count; f++)
            {
                var fila = h.Filas[f];
                if (fila.Length == 0 || fila.All(string.IsNullOrWhiteSpace)) continue;
                FilasLeidas++;
                AnalizarFila(h.Nombre, f + 1, fila, cModelo, cGuid, cId, params_, op);
            }
        }

        void AnalizarFila(string hoja, int nfila, string[] fila, int cModelo, int cGuid, int cId,
                          Dictionary<int, Chapa> columnas, OpcionesImportar op)
        {
            if (cModelo >= 0 && cModelo < fila.Length)
            {
                string m = (fila[cModelo] ?? "").Trim();
                if (m.Length > 0 && !m.Equals(_modelo, StringComparison.OrdinalIgnoreCase))
                {
                    Saltos.Add(new Salto
                    {
                        Hoja = hoja, Fila = nfila,
                        Motivo = "la fila es del modelo \"" + m + "\", que aca es un vinculo: "
                               + "un documento vinculado se abre de solo lectura"
                    });
                    return;
                }
            }

            Element e = Encontrar(fila, cGuid, cId);
            if (e == null)
            {
                Saltos.Add(new Salto
                {
                    Hoja = hoja, Fila = nfila,
                    Motivo = "no se encontro el elemento (GUID "
                           + Texto(fila, cGuid) + ", Element ID " + Texto(fila, cId) + ")"
                });
                return;
            }

            foreach (var kv in columnas)
            {
                int i = kv.Key;
                var ch = kv.Value;
                string texto = i < fila.Length ? (fila[i] ?? "") : "";

                if (texto.Trim().Length == 0 && !op.VaciarConCeldaVacia) continue;

                bool enTipo;
                string motivo;
                var p = Resolver(e, ch, op.PermitirTipo, out enTipo, out motivo);
                if (p == null)
                {
                    Saltos.Add(new Salto { Hoja = hoja, Fila = nfila, Columna = ch.Nombre, Motivo = motivo });
                    continue;
                }

                object valor;
                string error;
                if (!Convertir(p, ch, texto, out valor, out error))
                {
                    Saltos.Add(new Salto { Hoja = hoja, Fila = nfila, Columna = ch.Nombre, Motivo = error });
                    continue;
                }

                if (YaEsta(p, valor)) { Iguales++; continue; }

                Cambios.Add(new Cambio
                {
                    Hoja = hoja, Fila = nfila, E = e, Ch = ch, Columna = ch.Nombre,
                    Antes = Muestra(p, ch), Despues = texto.Trim(),
                    Valor = valor, EnTipo = enTipo,
                    Destino = enTipo ? e.GetTypeId() : e.Id
                });
            }
        }

        static string Texto(string[] fila, int i)
        {
            return (i >= 0 && i < fila.Length) ? (fila[i] ?? "") : "(no venia)";
        }

        Element Encontrar(string[] fila, int cGuid, int cId)
        {
            if (cGuid >= 0 && cGuid < fila.Length)
            {
                string g = (fila[cGuid] ?? "").Trim();
                if (g.Length > 0)
                {
                    try
                    {
                        var e = _doc.GetElement(g);
                        if (e != null) return e;
                    }
                    catch { }
                }
            }

            if (cId >= 0 && cId < fila.Length)
            {
                long n;
                string s = (fila[cId] ?? "").Trim();
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                {
                    try { return _doc.GetElement(new ElementId(n)); }
                    catch { }
                }
            }
            return null;
        }

        // ---------------- resolver el parametro ----------------

        Parameter Resolver(Element e, Chapa ch, bool permitirTipo, out bool enTipo, out string motivo)
        {
            enTipo = false;
            motivo = "";

            var enInstancia = Buscar(e, ch);
            if (enInstancia != null)
            {
                if (enInstancia.IsReadOnly)
                {
                    motivo = "el parametro es de solo lectura en este elemento";
                    return null;
                }
                if (enInstancia.StorageType == StorageType.ElementId)
                {
                    motivo = "el parametro apunta a otro elemento (nivel, material, tipo...): "
                           + "de ida se exporto el nombre y volver de un nombre a un elemento es ambiguo";
                    return null;
                }
                return enInstancia;
            }

            Element tipo = null;
            try
            {
                var ti = e.GetTypeId();
                if (ti != null && ti != ElementId.InvalidElementId) tipo = _doc.GetElement(ti);
            }
            catch { }

            var enT = tipo == null ? null : Buscar(tipo, ch);
            if (enT == null)
            {
                motivo = "el elemento no tiene ese parametro, ni en la instancia ni en el tipo";
                return null;
            }

            if (!permitirTipo)
            {
                motivo = "el parametro solo existe en el TIPO. Escribirlo cambiaria todas las "
                       + "instancias de ese tipo; marca la casilla si es lo que queres";
                return null;
            }
            if (enT.IsReadOnly) { motivo = "el parametro del tipo es de solo lectura"; return null; }
            if (enT.StorageType == StorageType.ElementId)
            {
                motivo = "el parametro del tipo apunta a otro elemento: no se puede volver desde el nombre";
                return null;
            }

            enTipo = true;
            return enT;
        }

        Parameter Buscar(Element e, Chapa ch)
        {
            if (e == null) return null;
            try
            {
                switch (ch.Clase)
                {
                    case ClaseClave.Sistema:
                        BuiltInParameter b;
                        if (Enum.TryParse(ch.Clave, false, out b)) return e.get_Parameter(b);
                        return null;

                    case ClaseClave.Compartido:
                        Guid g;
                        if (!Guid.TryParse(ch.Clave, out g)) return null;
                        var sp = SharedParameterElement.Lookup(_doc, g);
                        if (sp != null)
                        {
                            var p = e.get_Parameter(sp.GetDefinition());
                            if (p != null) return p;
                        }
                        return e.LookupParameter(ch.Nombre);

                    case ClaseClave.Proyecto:
                        long id;
                        if (long.TryParse(ch.Clave, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                        {
                            var pe = _doc.GetElement(new ElementId(id)) as ParameterElement;
                            if (pe != null)
                            {
                                var p = e.get_Parameter(pe.GetDefinition());
                                if (p != null) return p;
                            }
                        }
                        // El ElementId de un parametro de proyecto solo vale en su modelo:
                        // si el Excel viene de otro, el nombre es lo unico que queda.
                        return e.LookupParameter(ch.Nombre);
                }
            }
            catch { }
            return null;
        }

        // ---------------- valores ----------------

        bool Convertir(Parameter p, Chapa ch, string texto, out object valor, out string error)
        {
            valor = null; error = "";
            string t = (texto ?? "").Trim();

            switch (p.StorageType)
            {
                case StorageType.String:
                    valor = t;
                    return true;

                case StorageType.Integer:
                    if (EsSiNo(p))
                    {
                        bool? si = Booleano(t);
                        if (si == null) { error = "no se entiende \"" + t + "\" como si/no"; return false; }
                        valor = si.Value ? 1 : 0;
                        return true;
                    }
                    {
                        int n;
                        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
                            || int.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out n))
                        { valor = n; return true; }

                        double d;
                        if (Numero(t, out d)) { valor = (int)Math.Round(d); return true; }

                        error = "no se entiende \"" + t + "\" como numero entero";
                        return false;
                    }

                case StorageType.Double:
                    {
                        double d;
                        if (!Numero(t, out d)) { error = "no se entiende \"" + t + "\" como numero"; return false; }

                        if (ch.Unidad != null)
                        {
                            try { d = UnitUtils.ConvertToInternalUnits(d, ch.Unidad); }
                            catch
                            {
                                error = "no se pudo pasar de la unidad de la cabecera a unidades internas";
                                return false;
                            }
                        }
                        valor = d;
                        return true;
                    }
            }

            error = "tipo de almacenamiento no soportado: " + p.StorageType;
            return false;
        }

        /// <summary>Acepta el punto y la coma decimal: el Excel del usuario usa coma.</summary>
        static bool Numero(string t, out double d)
        {
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return true;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out d)) return true;

            string limpio = t.Replace(" ", "").Replace(" ", "");
            int comas = limpio.Count(c => c == ','), puntos = limpio.Count(c => c == '.');
            if (comas == 1 && puntos == 0) limpio = limpio.Replace(',', '.');
            else if (comas > 0 && puntos > 0) limpio = limpio.Replace(".", "").Replace(',', '.');

            return double.TryParse(limpio, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        static bool? Booleano(string t)
        {
            string s = t.Trim().ToLowerInvariant();
            if (s == "1" || s == "si" || s == "sí" || s == "true" || s == "verdadero" || s == "x") return true;
            if (s == "0" || s == "no" || s == "false" || s == "falso" || s == "") return false;
            return null;
        }

        static bool EsSiNo(Parameter p)
        {
            try { return p.Definition.GetDataType().Equals(SpecTypeId.Boolean.YesNo); }
            catch { return false; }
        }

        static bool YaEsta(Parameter p, object valor)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        return string.Equals(p.AsString() ?? "", (string)valor, StringComparison.Ordinal);
                    case StorageType.Integer:
                        return p.HasValue && p.AsInteger() == (int)valor;
                    case StorageType.Double:
                        if (!p.HasValue) return false;
                        double a = p.AsDouble(), b = (double)valor;
                        return Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Abs(a));
                }
            }
            catch { }
            return false;
        }

        string Muestra(Parameter p, Chapa ch)
        {
            try
            {
                if (!p.HasValue) return "(vacio)";
                switch (p.StorageType)
                {
                    case StorageType.String: return p.AsString() ?? "";
                    case StorageType.Integer:
                        return EsSiNo(p) ? (p.AsInteger() != 0 ? "si" : "no")
                                         : p.AsInteger().ToString(CultureInfo.InvariantCulture);
                    case StorageType.Double:
                        double d = p.AsDouble();
                        if (ch.Unidad != null)
                            try { d = UnitUtils.ConvertFromInternalUnits(d, ch.Unidad); } catch { }
                        return d.ToString("0.####", CultureInfo.InvariantCulture);
                }
            }
            catch { }
            return "";
        }

        // ---------------- escritura ----------------

        /// <summary>Escribe los cambios. Tiene que llamarse con una transaccion ya abierta.</summary>
        public int Aplicar(IList<Cambio> cambios, IList<string> fallos)
        {
            int hechos = 0;
            foreach (var c in cambios)
            {
                try
                {
                    var destino = _doc.GetElement(c.Destino);
                    if (destino == null)
                    {
                        fallos.Add(c.Hoja + " fila " + c.Fila + ": el elemento ya no existe");
                        continue;
                    }

                    bool sinUso;
                    string porque;
                    var p = Resolver(destino, c.Ch, true, out sinUso, out porque);
                    if (p == null)
                    {
                        fallos.Add(c.Hoja + " fila " + c.Fila + " (" + c.Columna + "): " + porque);
                        continue;
                    }

                    bool ok;
                    if (c.Valor is string) ok = p.Set((string)c.Valor);
                    else if (c.Valor is int) ok = p.Set((int)c.Valor);
                    else if (c.Valor is double) ok = p.Set((double)c.Valor);
                    else ok = false;

                    if (ok) hechos++;
                    else fallos.Add(c.Hoja + " fila " + c.Fila + " (" + c.Columna + "): Revit rechazo el valor");
                }
                catch (Exception ex)
                {
                    fallos.Add(c.Hoja + " fila " + c.Fila + " (" + c.Columna + "): " + ex.Message);
                }
            }
            return hechos;
        }
    }
}
