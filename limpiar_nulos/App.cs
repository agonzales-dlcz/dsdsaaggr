using System;
using System.Reflection;
using Autodesk.Revit.UI;
using System.Windows.Media.Imaging;

namespace Riga.LimpiarNulos
{
    public class App : IExternalApplication
    {
        const string TAB = "dsdsaaggr";

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TAB); } catch { }
            var panel = app.CreateRibbonPanel(TAB, "Parámetros");
            string dll = Assembly.GetExecutingAssembly().Location;

            var datos = new PushButtonData(
                "LimpiarNulos", "Limpiar\nnulos", dll, "Riga.LimpiarNulos.Comandos.CmdLimpiar")
            {
                ToolTip = "Limpia los parámetros booleanos de ejemplar que tengan valor nulo, asignándoles el valor 'No'.",
                LongDescription =
                    "Abre una ventana para seleccionar los parámetros booleanos. " +
                    "Convierte los valores nulos (vacíos) a 'No' en todos los elementos del proyecto."
            };
            var boton = panel.AddItem(datos) as PushButton;
            if (boton != null)
            {
                boton.AvailabilityClassName = typeof(Comun.ConDocumento).FullName;
                try {
                    var uri = new Uri("pack://application:,,,/LimpiarNulos;component/Resources/icon.jpg");
                    boton.LargeImage = new BitmapImage(uri);
                    boton.Image = new BitmapImage(uri);
                } catch { }
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            return Result.Succeeded;
        }
    }
}
