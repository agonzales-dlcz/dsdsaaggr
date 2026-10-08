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
                    var uri = new Uri("pack://application:,,,/LimpiarNulos;component/icono_nulos.png");
                    boton.LargeImage = new BitmapImage(uri);
                    boton.Image = new BitmapImage(uri);
                } catch { }
            }

            try
            {
                string dllInyector = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dll), "InyectorDatos.dll");
                var datosInyector = new PushButtonData(
                    "InyectarDatos", "Inyectar\ndatos", dllInyector, "Riga.InyectorDatos.Comandos.DataInjectorCommand")
                {
                    ToolTip = "Inyecta datos desde un archivo CSV a parámetros de elementos en Revit.",
                    LongDescription =
                        "Abre una ventana para seleccionar un archivo CSV y mapear sus columnas a parámetros de Revit. " +
                        "Inyecta los datos de acuerdo a la clave primaria seleccionada."
                };

                var botonInyector = panel.AddItem(datosInyector) as PushButton;
                if (botonInyector != null)
                {
                    // Commented out images to prevent Ribbon load crashes until icon files are added
                    // botonInyector.LargeImage = Comun.Iconos.Escoba(32);
                    // botonInyector.Image = Comun.Iconos.Escoba(16);
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Debug Ribbon Error", ex.ToString());
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            return Result.Succeeded;
        }
    }
}
