using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace Riga.Sectores.Comun
{
    internal static class Geometria
    {
        // Cara inferior de mayor area del solido del elemento.
        public static PlanarFace CaraInferior(Element e)
        {
            PlanarFace mejor = null;
            var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
            var ge = e.get_Geometry(opt);
            if (ge == null) return null;

            var pila = new Stack<GeometryElement>();
            pila.Push(ge);
            while (pila.Count > 0)
            {
                foreach (GeometryObject go in pila.Pop())
                {
                    var gi = go as GeometryInstance;
                    if (gi != null) { var ig = gi.GetInstanceGeometry(); if (ig != null) pila.Push(ig); continue; }
                    var s = go as Solid;
                    if (s == null || s.Volume <= 1e-9) continue;
                    foreach (Face f in s.Faces)
                    {
                        var pf = f as PlanarFace;
                        if (pf == null || pf.FaceNormal.Z > -0.999) continue;
                        if (mejor == null || pf.Area > mejor.Area) mejor = pf;
                    }
                }
            }
            return mejor;
        }

        // Lazo exterior (el de mayor area) de una cara.
        public static List<XYZ> Contorno(PlanarFace cara)
        {
            List<XYZ> mejor = new List<XYZ>();
            double maxA = -1;
            foreach (var lazo in cara.GetEdgesAsCurveLoops())
            {
                var pts = Vertices(lazo);
                double a = Math.Abs(Area(pts.Select(p => new double[] { p.X, p.Y }).ToList()));
                if (a > maxA) { maxA = a; mejor = pts; }
            }
            return mejor;
        }

        public static List<XYZ> Vertices(CurveLoop lazo)
        {
            var pts = new List<XYZ>();
            foreach (Curve c in lazo) Teselar(c, pts);
            Cerrar(pts);
            return pts;
        }

        public static List<XYZ> Vertices(IList<Autodesk.Revit.DB.BoundarySegment> lazo)
        {
            var pts = new List<XYZ>();
            foreach (var seg in lazo)
            {
                Curve c = null;
                try { c = seg.GetCurve(); } catch { }
                if (c != null) Teselar(c, pts);
            }
            Cerrar(pts);
            return pts;
        }

        static void Teselar(Curve c, List<XYZ> pts)
        {
            var t = c.Tessellate();
            for (int i = 0; i < t.Count - 1; i++)
                if (pts.Count == 0 || pts[pts.Count - 1].DistanceTo(t[i]) > 1e-6) pts.Add(t[i]);
        }

        static void Cerrar(List<XYZ> pts)
        {
            if (pts.Count > 1 && pts[0].DistanceTo(pts[pts.Count - 1]) <= 1e-6) pts.RemoveAt(pts.Count - 1);
        }

        // Area con signo (shoelace): positiva = antihorario.
        public static double Area(List<double[]> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i][0] * q[1] - q[0] * p[i][1]; }
            return a / 2.0;
        }

        // Par-impar sobre un lazo.
        public static bool EnLazo(double x, double y, double[][] pol)
        {
            bool d = false;
            for (int i = 0, j = pol.Length - 1; i < pol.Length; j = i++)
            {
                double xi = pol[i][0], yi = pol[i][1], xj = pol[j][0], yj = pol[j][1];
                if (((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi)) d = !d;
            }
            return d;
        }

        // Par-impar sobre contorno + huecos.
        public static bool Dentro(double x, double y, double[][][] lazos)
        {
            bool d = false;
            foreach (var pol in lazos) if (EnLazo(x, y, pol)) d = !d;
            return d;
        }

        public static string N(double v)
        {
            return (Math.Abs(v) < 1e-9 ? 0.0 : v).ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string Pol(List<double[]> p)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < p.Count; i++) { if (i > 0) sb.Append(", "); sb.Append("[" + N(p[i][0]) + ", " + N(p[i][1]) + "]"); }
            return sb.Append("]").ToString();
        }

        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        }
    }
}
