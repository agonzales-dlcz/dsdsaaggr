using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace Riga.ConcatenadorParametros
{
    public class App : IExternalApplication
    {
        const string TAB = "dsdsaaggr";
        const string PANEL = "Parámetros";

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                try { app.CreateRibbonTab(TAB); } catch { }

                RibbonPanel panel = null;
                foreach (var p in app.GetRibbonPanels(TAB))
                {
                    if (p.Name == PANEL) { panel = p; break; }
                }
                if (panel == null) panel = app.CreateRibbonPanel(TAB, PANEL);

                string dll = Assembly.GetExecutingAssembly().Location;

                var datos = new PushButtonData(
                    "ConcatenadorParametros", "Concatenador\nde Parámetros", dll,
                    "Riga.ConcatenadorParametros.Comandos.CmdConcatenadorParametros")
                {
                    ToolTip = "Concatena múltiples parámetros en un parámetro de destino.",
                    LongDescription = "Permite seleccionar elementos o filtrar por categoría para unir los valores de varios parámetros en un único parámetro destino, usando un separador personalizado."
                };

                var boton = panel.AddItem(datos) as PushButton;
                if (boton != null)
                {
                    boton.LargeImage = CuadradoNegro(32);
                    boton.Image = CuadradoNegro(16);
                }

                return Result.Succeeded;
            }
            catch (Exception)
            {
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) { return Result.Succeeded; }

        static ImageSource CuadradoNegro(int lado)
        {
            try
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    var brush = new SolidColorBrush(Color.FromRgb(0x18, 0x1C, 0x20));
                    brush.Freeze();
                    dc.DrawRectangle(brush, null, new Rect(0, 0, lado, lado));
                }

                var bmp = new RenderTargetBitmap(lado, lado, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(visual);
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
