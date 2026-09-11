// Escribe Sector y Nivel del elemento en cada elemento, segun donde caiga su centro
// geometrico dentro de los sectores del JSON.
//
// La geometria es la misma de siempre; lo que se volvio configurable es en que dos
// parametros se escribe.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Riga.Sectores.Comun;

namespace Riga.Sectores.Nucleo
{
    internal sealed class Zona
    {
        public string Sector, Nivel;
        public double ZMin, ZMax, XMin, XMax, YMin, YMax;
        public double[][] Pol;
    }

    internal sealed class Parametrizador
    {
        const double TOL = 1.0;          // mm de holgura en z

        readonly Document _doc;

        public int Asignados, SinParametro, Errores, Evaluados;

        public Parametrizador(Document doc) { _doc = doc; }

        // ---------------- lectura del JSON ----------------

        public static List<Zona> Leer(string ruta, out Guid gSector, out Guid gNivel)
        {
            var raiz = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(ruta, Encoding.UTF8));
            var pm = MiniJson.Obj(raiz, "parametros");
            gSector = MiniJson.Guid_(pm, "sector");
            gNivel = MiniJson.Guid_(pm, "nivel");

            var zonas = new List<Zona>();
            foreach (var o in MiniJson.Arr(raiz, "sectores"))
            {
                var d = (Dictionary<string, object>)o;
                var pol = MiniJson.Pol(MiniJson.Arr(d, "poligono"));
                if (pol.Length < 3) continue;

                zonas.Add(new Zona
                {
                    Sector = MiniJson.Str(d, "sector"),
                    Nivel = MiniJson.Str(d, "nivel"),
                    ZMin = MiniJson.Num(d, "z_min"),
                    ZMax = MiniJson.Num(d, "z_max"),
                    Pol = pol,
                    XMin = pol.Min(p => p[0]),
                    XMax = pol.Max(p => p[0]),
                    YMin = pol.Min(p => p[1]),
                    YMax = pol.Max(p => p[1])
                });
            }
            if (zonas.Count == 0) throw new InvalidOperationException("el json no trae sectores.");
            return zonas;
        }

        // ---------------- escritura ----------------

        public void Correr(IList<Zona> zonas, Destino dSector, Destino dNivel,
                           Action<int, int> avance, Func<bool> cancelado)
        {
            Asignados = SinParametro = Errores = Evaluados = 0;

            var marco = Marco.De(_doc);
            var candidatos = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => Categorias.EsCandidato(e, Categorias.Excluidas))
                .ToList();

            using (var tx = new Transaction(_doc, "Sector / Nivel del elemento"))
            {
                tx.Start();
                Silencio.Aplicar(tx);

                for (int i = 0; i < candidatos.Count; i++)
                {
                    if (cancelado != null && cancelado()) { tx.RollBack(); return; }
                    if (avance != null && (i % 500) == 0) avance(i, candidatos.Count);

                    var e = candidatos[i];
                    Evaluados++;

                    BoundingBoxXYZ bb = null;
                    try { bb = e.get_BoundingBox(null); } catch { }
                    if (bb == null) continue;

                    XYZ c = bb.Transform.OfPoint((bb.Min + bb.Max) * 0.5);
                    double[] xy = marco.XY(c);
                    double x = xy[0], y = xy[1], z = marco.Z(c.Z);

                    var mejor = Mejor(zonas, x, y, z);
                    if (mejor == null) continue;

                    var ps = dSector.En(e);
                    var pn = dNivel.En(e);
                    if (ps == null || pn == null) { SinParametro++; continue; }

                    try { ps.Set(mejor.Sector); pn.Set(mejor.Nivel); Asignados++; }
                    catch { Errores++; }
                }

                tx.Commit();
            }
        }

        /// <summary>El sector que contiene el punto, y entre varios el que lo deja mas holgado en z.</summary>
        static Zona Mejor(IList<Zona> zonas, double x, double y, double z)
        {
            Zona mejor = null;
            double holgura = double.NegativeInfinity;

            foreach (var zo in zonas)
            {
                if (z < zo.ZMin - TOL || z > zo.ZMax + TOL) continue;
                if (x < zo.XMin || x > zo.XMax || y < zo.YMin || y > zo.YMax) continue;
                if (!Geometria.EnLazo(x, y, zo.Pol)) continue;

                double h = Math.Min(z - zo.ZMin, zo.ZMax - z);
                if (h > holgura) { holgura = h; mejor = zo; }
            }
            return mejor;
        }
    }
}
