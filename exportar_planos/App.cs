// Agrega el panel "Exportar" a la pestana RIGA.

using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace Riga.ExportarPlanos
{
    public class App : IExternalApplication
    {
        const string TAB = "dsdsaaggr";

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TAB); } catch { }   // ya existe si hay otro addin en la misma pestana
            var panel = app.CreateRibbonPanel(TAB, "Exportar");
            string dll = Assembly.GetExecutingAssembly().Location;

            var datos = new PushButtonData(
                "ExportarPlanos", "Exportar\nplanos", dll, "Riga.ExportarPlanos.Comandos.CmdExportar")
            {
                ToolTip = "Exporta planos y vistas en lote a PDF, DWG, DXF, DGN e imagen, con nombre de archivo armado a partir de parametros.",
                LongDescription =
                    "Tres pasos: elegis que exportar, configuras cada formato y creas. " +
                    "La configuracion se guarda como perfil."
            };
            var boton = panel.AddItem(datos) as PushButton;
            if (boton != null)
            {
                boton.AvailabilityClassName = typeof(Comun.ConDocumento).FullName;
                boton.LargeImage = Comun.Iconos.Espartano(32);
                boton.Image = Comun.Iconos.Espartano(16);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            return Result.Succeeded;
        }
    }
}
