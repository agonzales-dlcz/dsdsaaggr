// Iconos de la cinta, dibujados por codigo.
//
// A proposito no son archivos PNG: asi no hay imagenes sueltas que copiar al instalar ni
// rutas que se rompan. Se dibujan al arrancar Revit.
//
// Son siluetas de guerreros de distintas culturas. A 32 y 16 pixeles una figura entera se
// vuelve una mancha, asi que cada icono es el casco o el tocado, que es lo que identifica.
//
// Regla que costo un intento: dos formas negras pegadas se funden. Cada parte va separada
// por aire, y los huecos (el ojo del casco) se calan con EvenOdd.
//
// El archivo es el mismo en todos los addins, solo cambia el namespace: asi cambiar cual
// icono va en cada boton es cambiar una palabra en App.cs, sin tocar dibujos.

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Riga.Guardado.Comun
{
    internal static class Iconos
    {
        static readonly Brush TINTA = Congelar(Color.FromRgb(0x18, 0x1C, 0x20));

        public static ImageSource Samurai(int lado) { return Dibujar(lado, PintarSamurai); }
        public static ImageSource Espartano(int lado) { return Dibujar(lado, PintarEspartano); }
        public static ImageSource Azteca(int lado) { return Dibujar(lado, PintarAzteca); }
        public static ImageSource Inca(int lado) { return Dibujar(lado, PintarInca); }
        public static ImageSource Vikingo(int lado) { return Dibujar(lado, PintarVikingo); }
        public static ImageSource Mongol(int lado) { return Dibujar(lado, PintarMongol); }
        public static ImageSource Apache(int lado) { return Dibujar(lado, PintarApache); }

        // ---------------- siluetas ----------------

        /// <summary>Kabuto: media luna, cuenco y faldon de nuca, con aire entre las tres.</summary>
        static void PintarSamurai(DrawingContext dc, double u)
        {
            var luna = Figura(g =>
            {
                g.BeginFigure(P(u, 0.22, 0.26), true, true);
                g.BezierTo(P(u, 0.30, 0.02), P(u, 0.66, 0.02), P(u, 0.78, 0.22), true, false);
                g.BezierTo(P(u, 0.64, 0.12), P(u, 0.36, 0.12), P(u, 0.32, 0.28), true, false);
            });
            dc.DrawGeometry(TINTA, null, luna);

            var cuenco = Figura(g =>
            {
                g.BeginFigure(P(u, 0.22, 0.66), true, true);
                g.BezierTo(P(u, 0.20, 0.40), P(u, 0.36, 0.32), P(u, 0.50, 0.32), true, false);
                g.BezierTo(P(u, 0.64, 0.32), P(u, 0.80, 0.40), P(u, 0.78, 0.66), true, false);
                g.LineTo(P(u, 0.62, 0.62), true, false);
                g.LineTo(P(u, 0.38, 0.62), true, false);
            });
            dc.DrawGeometry(TINTA, null, cuenco);

            // faldon de nuca, separado del cuenco por una franja de aire
            var faldon = Figura(g =>
            {
                g.BeginFigure(P(u, 0.12, 0.96), true, true);
                g.LineTo(P(u, 0.24, 0.74), true, false);
                g.LineTo(P(u, 0.76, 0.74), true, false);
                g.LineTo(P(u, 0.88, 0.96), true, false);
                g.LineTo(P(u, 0.62, 0.88), true, false);
                g.LineTo(P(u, 0.38, 0.88), true, false);
            });
            dc.DrawGeometry(TINTA, null, faldon);
        }

        /// <summary>Casco corintio de perfil: cimera arriba, ojo calado y hueco de la boca.</summary>
        static void PintarEspartano(DrawingContext dc, double u)
        {
            var cimera = Figura(g =>
            {
                g.BeginFigure(P(u, 0.08, 0.34), true, true);
                g.BezierTo(P(u, 0.24, 0.04), P(u, 0.62, 0.00), P(u, 0.78, 0.22), true, false);
                g.LineTo(P(u, 0.64, 0.29), true, false);
                g.BezierTo(P(u, 0.54, 0.13), P(u, 0.32, 0.17), P(u, 0.22, 0.37), true, false);
            });
            dc.DrawGeometry(TINTA, null, cimera);

            var casco = Figura(g =>
            {
                g.BeginFigure(P(u, 0.22, 0.54), true, true);
                g.BezierTo(P(u, 0.22, 0.34), P(u, 0.78, 0.34), P(u, 0.80, 0.56), true, false);
                g.LineTo(P(u, 0.80, 0.68), true, false);
                g.BezierTo(P(u, 0.80, 0.86), P(u, 0.70, 0.97), P(u, 0.60, 0.97), true, false);
                g.LineTo(P(u, 0.58, 0.74), true, false);
                g.LineTo(P(u, 0.48, 0.74), true, false);
                g.LineTo(P(u, 0.46, 0.97), true, false);
                g.BezierTo(P(u, 0.30, 0.95), P(u, 0.22, 0.82), P(u, 0.22, 0.64), true, false);
            });

            var ojo = Figura(g =>
            {
                g.BeginFigure(P(u, 0.50, 0.58), true, true);
                g.BezierTo(P(u, 0.60, 0.49), P(u, 0.74, 0.51), P(u, 0.77, 0.60), true, false);
                g.BezierTo(P(u, 0.68, 0.66), P(u, 0.55, 0.66), P(u, 0.50, 0.58), true, false);
            });

            dc.DrawGeometry(TINTA, null,
                new CombinedGeometry(GeometryCombineMode.Exclude, casco, ojo));
        }

        /// <summary>Guerrero aguila: penacho abierto y el rostro dentro del pico.</summary>
        static void PintarAzteca(DrawingContext dc, double u)
        {
            double[] px = { 0.02, 0.10, 0.24, 0.42 };
            double[] py = { 0.32, 0.16, 0.05, 0.00 };

            for (int i = 0; i < px.Length; i++)
            {
                int k = i;
                var pluma = Figura(g =>
                {
                    g.BeginFigure(P(u, 0.44, 0.54), true, true);
                    g.BezierTo(P(u, 0.30, 0.44), P(u, px[k] + 0.06, py[k] + 0.14), P(u, px[k], py[k]), true, false);
                    g.BezierTo(P(u, px[k] + 0.11, py[k] + 0.09), P(u, 0.35, 0.46), P(u, 0.49, 0.60), true, false);
                });
                dc.DrawGeometry(TINTA, null, pluma);
            }

            // pico abierto, arriba y separado del rostro
            var pico = Figura(g =>
            {
                g.BeginFigure(P(u, 0.44, 0.42), true, true);
                g.BezierTo(P(u, 0.58, 0.26), P(u, 0.82, 0.30), P(u, 0.86, 0.42), true, false);
                g.LineTo(P(u, 0.99, 0.46), true, false);
                g.LineTo(P(u, 0.84, 0.53), true, false);
                g.BezierTo(P(u, 0.76, 0.43), P(u, 0.56, 0.41), P(u, 0.48, 0.51), true, false);
            });
            dc.DrawGeometry(TINTA, null, pico);

            var cara = Figura(g =>
            {
                g.BeginFigure(P(u, 0.46, 0.64), true, true);
                g.BezierTo(P(u, 0.56, 0.58), P(u, 0.75, 0.62), P(u, 0.77, 0.74), true, false);
                g.BezierTo(P(u, 0.79, 0.88), P(u, 0.62, 0.98), P(u, 0.50, 0.94), true, false);
                g.BezierTo(P(u, 0.42, 0.88), P(u, 0.40, 0.72), P(u, 0.46, 0.64), true, false);
            });
            dc.DrawGeometry(TINTA, null, cara);
        }

        /// <summary>Guerrero andino: tres plumas, vincha, y rostro con orejeras.</summary>
        static void PintarInca(DrawingContext dc, double u)
        {
            double[] cx = { 0.28, 0.50, 0.72 };
            double[] arriba = { 0.20, 0.03, 0.20 };

            for (int i = 0; i < cx.Length; i++)
            {
                int k = i;
                var pluma = Figura(g =>
                {
                    g.BeginFigure(P(u, cx[k] - 0.06, 0.36), true, true);
                    g.BezierTo(P(u, cx[k] - 0.07, arriba[k] + 0.10), P(u, cx[k] - 0.02, arriba[k]), P(u, cx[k], arriba[k]), true, false);
                    g.BezierTo(P(u, cx[k] + 0.02, arriba[k]), P(u, cx[k] + 0.07, arriba[k] + 0.10), P(u, cx[k] + 0.06, 0.36), true, false);
                });
                dc.DrawGeometry(TINTA, null, pluma);
            }

            // vincha, con aire arriba y abajo
            dc.DrawRectangle(TINTA, null, new Rect(u * 0.18, u * 0.44, u * 0.64, u * 0.11));

            var cara = Figura(g =>
            {
                g.BeginFigure(P(u, 0.30, 0.61), true, true);
                g.LineTo(P(u, 0.70, 0.61), true, false);
                g.LineTo(P(u, 0.70, 0.75), true, false);
                g.BezierTo(P(u, 0.70, 0.91), P(u, 0.58, 0.99), P(u, 0.50, 0.99), true, false);
                g.BezierTo(P(u, 0.42, 0.99), P(u, 0.30, 0.91), P(u, 0.30, 0.75), true, false);
            });
            dc.DrawGeometry(TINTA, null, cara);

            // orejeras
            dc.DrawRectangle(TINTA, null, new Rect(u * 0.18, u * 0.63, u * 0.08, u * 0.13));
            dc.DrawRectangle(TINTA, null, new Rect(u * 0.74, u * 0.63, u * 0.08, u * 0.13));
        }

        /// <summary>Yelmo nordico: cuenco con nasal y dos cuernos, separados del cuenco.</summary>
        static void PintarVikingo(DrawingContext dc, double u)
        {
            // cuerno izquierdo
            var izq = Figura(g =>
            {
                g.BeginFigure(P(u, 0.26, 0.56), true, true);
                g.BezierTo(P(u, 0.12, 0.50), P(u, 0.02, 0.34), P(u, 0.03, 0.14), true, false);
                g.BezierTo(P(u, 0.14, 0.30), P(u, 0.22, 0.40), P(u, 0.30, 0.46), true, false);
            });
            dc.DrawGeometry(TINTA, null, izq);

            // cuerno derecho
            var der = Figura(g =>
            {
                g.BeginFigure(P(u, 0.74, 0.56), true, true);
                g.BezierTo(P(u, 0.88, 0.50), P(u, 0.98, 0.34), P(u, 0.97, 0.14), true, false);
                g.BezierTo(P(u, 0.86, 0.30), P(u, 0.78, 0.40), P(u, 0.70, 0.46), true, false);
            });
            dc.DrawGeometry(TINTA, null, der);

            // cuenco
            var cuenco = Figura(g =>
            {
                g.BeginFigure(P(u, 0.28, 0.74), true, true);
                g.BezierTo(P(u, 0.28, 0.42), P(u, 0.72, 0.42), P(u, 0.72, 0.74), true, false);
            });
            dc.DrawGeometry(TINTA, null, cuenco);

            // nasal, separado del cuenco por una franja de aire
            dc.DrawRectangle(TINTA, null, new Rect(u * 0.45, u * 0.80, u * 0.10, u * 0.17));
        }

        /// <summary>Yelmo mongol: punta, cono, ala de piel y dos carrilleras.</summary>
        static void PintarMongol(DrawingContext dc, double u)
        {
            // punta con su bolita, arriba del todo
            dc.DrawEllipse(TINTA, null, new Point(u * 0.50, u * 0.05), u * 0.05, u * 0.05);
            dc.DrawRectangle(TINTA, null, new Rect(u * 0.47, u * 0.09, u * 0.06, u * 0.08));

            // cono, separado de la punta
            var cono = Figura(g =>
            {
                g.BeginFigure(P(u, 0.50, 0.22), true, true);
                g.BezierTo(P(u, 0.64, 0.34), P(u, 0.74, 0.48), P(u, 0.78, 0.62), true, false);
                g.LineTo(P(u, 0.22, 0.62), true, false);
                g.BezierTo(P(u, 0.26, 0.48), P(u, 0.36, 0.34), P(u, 0.50, 0.22), true, false);
            });
            dc.DrawGeometry(TINTA, null, cono);

            // ala de piel, con aire arriba y abajo
            var ala = Figura(g =>
            {
                g.BeginFigure(P(u, 0.14, 0.80), true, true);
                g.BezierTo(P(u, 0.12, 0.68), P(u, 0.88, 0.68), P(u, 0.86, 0.80), true, false);
                g.BezierTo(P(u, 0.70, 0.74), P(u, 0.30, 0.74), P(u, 0.14, 0.80), true, false);
            });
            dc.DrawGeometry(TINTA, null, ala);

            // carrilleras
            dc.DrawRectangle(TINTA, null, new Rect(u * 0.24, u * 0.86, u * 0.11, u * 0.13));
            dc.DrawRectangle(TINTA, null, new Rect(u * 0.65, u * 0.86, u * 0.11, u * 0.13));
        }

        /// <summary>
        /// Tocado de jefe apache: abanico de plumas, vincha y cabeza.
        ///
        /// Dos cosas que costaron intentos y explican como esta hecho:
        ///
        /// 1. Las plumas NO van sueltas. Dibujadas separadas, a 16 pixeles quedan como rayos
        ///    de sol. Van anchas y superpuestas, unidas en una sola masa, y despues se les
        ///    cortan las divisiones. Es la misma idea del ojo del espartano: lo que da la
        ///    lectura es el hueco, no la separacion.
        ///
        /// 2. Las de los costados van mas cortas. Con todas iguales el borde de arriba es un
        ///    arco liso y se lee como media luna o caracola; acortandolas el perfil queda
        ///    dentado y ahi si se entiende que son plumas.
        ///
        /// La cabeza de abajo tampoco es adorno: sin ella el tocado flota y parece un
        /// amanecer sobre un puente.
        ///
        /// Se distingue del Inca, que tambien lleva plumas, por la forma general: el Inca es
        /// angosto y cuadrado, tres plumas rectas sobre un rostro con orejeras; este es un
        /// abanico ancho. Del Azteca lo separa la simetria, aquel barre en diagonal.
        /// </summary>
        static void PintarApache(DrawingContext dc, double u)
        {
            const int PLUMAS = 7;
            const double ABRE = 74, ANCHO = 0.125, CORTE = 0.030, MERMA = 0.13;

            var centro = new Point(0.50, 0.86);
            double r0 = 0.16, r1 = 0.58;

            Func<int, double> largo = i =>
            {
                double t = (double)i / (PLUMAS - 1);
                return r1 - MERMA * Math.Abs(2 * t - 1);
            };

            Geometry corona = null;
            for (int i = 0; i < PLUMAS; i++)
            {
                var p = Pluma(centro, Angulo(i, PLUMAS, ABRE), r0, largo(i), ANCHO, u);
                corona = corona == null ? p : new CombinedGeometry(GeometryCombineMode.Union, corona, p);
            }

            Geometry cortes = null;
            for (int i = 0; i < PLUMAS - 1; i++)
            {
                double a = (Angulo(i, PLUMAS, ABRE) + Angulo(i + 1, PLUMAS, ABRE)) / 2;
                var c = Pluma(centro, a, r0 - 0.02, Math.Max(largo(i), largo(i + 1)) + 0.06, CORTE, u);
                cortes = cortes == null ? c : new CombinedGeometry(GeometryCombineMode.Union, cortes, c);
            }

            dc.DrawGeometry(TINTA, null,
                new CombinedGeometry(GeometryCombineMode.Exclude, corona, cortes));

            var vincha = new StreamGeometry();
            using (var g = vincha.Open())
            {
                g.BeginFigure(P(u, 0.23, 0.79), true, true);
                g.BezierTo(P(u, 0.34, 0.735), P(u, 0.66, 0.735), P(u, 0.77, 0.79), true, false);
                g.LineTo(P(u, 0.77, 0.885), true, false);
                g.BezierTo(P(u, 0.66, 0.83), P(u, 0.34, 0.83), P(u, 0.23, 0.885), true, false);
            }
            vincha.Freeze();
            dc.DrawGeometry(TINTA, null, vincha);

            dc.DrawGeometry(TINTA, null,
                new EllipseGeometry(P(u, 0.50, 0.95), u * 0.16, u * 0.11));
        }

        static double Angulo(int i, int n, double abre)
        {
            double t = n == 1 ? 0.5 : (double)i / (n - 1);
            return (-abre + t * 2 * abre) * Math.PI / 180.0;
        }

        /// <summary>Hoja alargada que sale del centro hacia el angulo dado.</summary>
        static Geometry Pluma(Point c, double a, double r0, double r1, double ancho, double u)
        {
            double s = Math.Sin(a), co = Math.Cos(a);
            var dir = new Point(s, -co);
            var perp = new Point(co, s);

            var b = new Point(c.X + dir.X * r0, c.Y + dir.Y * r0);
            var t = new Point(c.X + dir.X * r1, c.Y + dir.Y * r1);
            var m = new Point((b.X + t.X) / 2, (b.Y + t.Y) / 2);

            var g = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var x = g.Open())
            {
                x.BeginFigure(P(u, b.X, b.Y), true, true);
                x.QuadraticBezierTo(P(u, m.X + perp.X * ancho, m.Y + perp.Y * ancho),
                                    P(u, t.X, t.Y), true, false);
                x.QuadraticBezierTo(P(u, m.X - perp.X * ancho, m.Y - perp.Y * ancho),
                                    P(u, b.X, b.Y), true, false);
            }
            g.Freeze();
            return g;
        }

        // ---------------- motor ----------------

        static Point P(double u, double x, double y) { return new Point(u * x, u * y); }

        static Geometry Figura(Action<StreamGeometryContext> trazar)
        {
            var g = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var c = g.Open()) trazar(c);
            g.Freeze();
            return g;
        }

        static Brush Congelar(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static ImageSource Dibujar(int lado, Action<DrawingContext, double> pintar)
        {
            try
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen()) pintar(dc, lado);

                var bmp = new RenderTargetBitmap(lado, lado, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(visual);
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }      // sin icono es feo, pero no es motivo para no cargar
        }
    }
}
