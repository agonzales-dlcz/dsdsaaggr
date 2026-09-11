// Panel "Vistas" de la pestana compartida.

using System;
using System.Reflection;
using Autodesk.Revit.UI;
using Riga.Vistas.Comun;

namespace Riga.Vistas
{
    public class App : IExternalApplication
    {
        const string TAB = "dsdsaaggr";
        const string PANEL = "Vistas";
        static readonly string SALTO = Environment.NewLine + Environment.NewLine;

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TAB); } catch { }   // ya existe si arranco otro addin antes

            RibbonPanel panel = null;
            foreach (var p in app.GetRibbonPanels(TAB))
                if (p.Name == PANEL) { panel = p; break; }
            if (panel == null) panel = app.CreateRibbonPanel(TAB, PANEL);

            string dll = Assembly.GetExecutingAssembly().Location;

            var datos = new PushButtonData(
                "RejillasVinculos", "Rejillas\nde vinculos", dll,
                "Riga.Vistas.Comandos.CmdRejillasVinculos")
            {
                ToolTip = "Oculta las rejillas de los vinculos en la vista actual y deja las del modelo.",
                LongDescription =
                    "Recorre los vinculos que se ven en la vista, incluidos los anidados, junta sus "
                    + "rejillas y las oculta con \"Ocultar en vista > Elementos\". Las rejillas propias "
                    + "del modelo no se tocan." + SALTO
                    + "Sirve en planta, seccion, alzado y 3D. En un plano hay que entrar a la vista." + SALTO
                    + "Para volver atras: Ctrl+Z, o la bombilla \"Mostrar elementos ocultos\" de la "
                    + "barra de abajo, seleccionar y \"Mostrar en vista > Elementos\"."
            };
            var boton = panel.AddItem(datos) as PushButton;
            if (boton != null)
            {
                boton.AvailabilityClassName = typeof(ConDocumento).FullName;
                boton.LargeImage = Iconos.Vikingo(32);
                boton.Image = Iconos.Vikingo(16);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) { return Result.Succeeded; }
    }
}
