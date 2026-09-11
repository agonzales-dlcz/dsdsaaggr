// Panel "Sectorizacion" de la pestana compartida.
//
// La pestana la comparten los tres addins: el primero que arranca la crea y los otros se
// cuelgan de ella.

using System;
using System.Reflection;
using Autodesk.Revit.UI;
using Riga.Sectores.Comun;

namespace Riga.Sectores
{
    public class App : IExternalApplication
    {
        const string TAB = "dsdsaaggr";
        const string PANEL = "Sectorizacion";

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TAB); } catch { }   // ya existe si arranco otro addin antes

            RibbonPanel panel = null;
            foreach (var p in app.GetRibbonPanels(TAB))
                if (p.Name == PANEL) { panel = p; break; }
            if (panel == null) panel = app.CreateRibbonPanel(TAB, PANEL);

            string dll = Assembly.GetExecutingAssembly().Location;

            Boton(panel, dll, "LeerCoordenadas", "Leer\ncoordenadas",
                  "Riga.Sectores.Comandos.CmdExtraerSectores",
                  "Exporta a JSON los suelos de tipo sector_* con su contorno y sus cotas z_min / z_max.",
                  Iconos.Samurai);

            Boton(panel, dll, "ParametrizarSectores", "Parametrizar\nsectores",
                  "Riga.Sectores.Comandos.CmdParametrizarSectores",
                  "Lee el JSON de sectores y escribe Sector y Nivel del elemento en cada elemento, "
                  + "segun donde caiga su centro geometrico.",
                  Iconos.Azteca);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) { return Result.Succeeded; }

        static void Boton(RibbonPanel panel, string dll, string nombre, string texto, string clase,
                          string ayuda, Func<int, System.Windows.Media.ImageSource> icono)
        {
            var datos = new PushButtonData(nombre, texto, dll, clase) { ToolTip = ayuda };
            var boton = panel.AddItem(datos) as PushButton;
            if (boton == null) return;

            boton.AvailabilityClassName = typeof(ConDocumento).FullName;
            if (icono == null) return;
            boton.LargeImage = icono(32);
            boton.Image = icono(16);
        }
    }
}
