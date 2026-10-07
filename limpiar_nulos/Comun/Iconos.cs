using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Riga.LimpiarNulos.Comun
{
    internal static class Iconos
    {
        static readonly Brush TINTA = Congelar(Color.FromRgb(0x18, 0x1C, 0x20));

        public static ImageSource Escoba(int lado) => Dibujar(lado, PintarEscobaNulo);

        static void PintarEscobaNulo(DrawingContext dc, double u)
        {
            // Símbolo de "Nulo" (círculo tachado)
            var nulo = new StreamGeometry();
            using (var g = nulo.Open())
            {
                // Círculo exterior e interior combinados (anillo)
                g.BeginFigure(P(u, 0.5, 0.1), true, true);
                g.ArcTo(P(u, 0.5, 0.9), new Size(u * 0.4, u * 0.4), 0, false, SweepDirection.Clockwise, true, false);
                g.ArcTo(P(u, 0.5, 0.1), new Size(u * 0.4, u * 0.4), 0, false, SweepDirection.Clockwise, true, false);

                g.BeginFigure(P(u, 0.5, 0.2), true, true);
                g.ArcTo(P(u, 0.5, 0.8), new Size(u * 0.3, u * 0.3), 0, false, SweepDirection.Counterclockwise, true, false);
                g.ArcTo(P(u, 0.5, 0.2), new Size(u * 0.3, u * 0.3), 0, false, SweepDirection.Counterclockwise, true, false);

                // Línea diagonal del "Nulo"
                g.BeginFigure(P(u, 0.2, 0.2), true, true);
                g.LineTo(P(u, 0.8, 0.8), true, false);
                g.LineTo(P(u, 0.75, 0.85), true, false);
                g.LineTo(P(u, 0.15, 0.25), true, false);
            }
            nulo.Freeze();
            dc.DrawGeometry(TINTA, null, nulo);

            // Escoba gorda superpuesta a la derecha
            var escoba = new StreamGeometry();
            using (var g = escoba.Open())
            {
                // Mango
                g.BeginFigure(P(u, 0.95, 0.05), true, true);
                g.LineTo(P(u, 0.85, -0.05), true, false);
                g.LineTo(P(u, 0.65, 0.35), true, false);
                g.LineTo(P(u, 0.75, 0.45), true, false);

                // Cepillo gordo
                g.BeginFigure(P(u, 0.65, 0.35), true, true);
                g.LineTo(P(u, 0.75, 0.45), true, false);
                g.LineTo(P(u, 0.9, 0.7), true, false);
                g.LineTo(P(u, 0.8, 0.8), true, false);
                g.LineTo(P(u, 0.7, 0.7), true, false); // recorte de cerda
                g.LineTo(P(u, 0.6, 0.8), true, false);
                g.LineTo(P(u, 0.5, 0.7), true, false); // recorte de cerda
                g.LineTo(P(u, 0.4, 0.8), true, false);
                g.LineTo(P(u, 0.4, 0.6), true, false);
            }
            escoba.Freeze();

            // Pintamos la escoba pero calando el fondo para que no se empaste con el nulo
            var fondoEscoba = new Pen(Brushes.White, u * 0.05);
            dc.DrawGeometry(Brushes.White, fondoEscoba, escoba); // Crea un borde blanco para separar visualmente
            dc.DrawGeometry(TINTA, null, escoba);
        }

        static Point P(double u, double x, double y) { return new Point(u * x, u * y); }

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
            catch { return null; }
        }
    }
}
