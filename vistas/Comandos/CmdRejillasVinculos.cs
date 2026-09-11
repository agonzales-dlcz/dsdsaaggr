// Oculta las rejillas de los vinculos en la vista activa, dejando las del anfitrion.
//
// COMO, SI LA API NO SABE OCULTAR ELEMENTOS DE UN VINCULO
//
// No sabe, es cierto: View.HideElements solo acepta ElementId del documento anfitrion, y
// de los 19 metodos de toda la API que reciben un LinkElementId ninguno es de visibilidad
// (son de habitaciones, etiquetas, piezas, armadura y escaleras). Verificado por reflexion
// sobre RevitAPI.dll 25.4.20.0.
//
// Pero el comando de Revit si sabe, y se le puede pedir que lo haga:
//
//   1. Reference(elemento del vinculo).CreateLinkReference(instancia del vinculo)
//      convierte una referencia interna del vinculo en una valida en el anfitrion.
//   2. Selection.SetReferences deja esas referencias seleccionadas. Selecciona elementos
//      de vinculo, que es justo lo que SetElementIds no puede hacer.
//   3. UIApplication.PostCommand(PostableCommand.HideElements) dispara el
//      "Ocultar en vista > Elementos" de Revit sobre esa seleccion.
//
// Es decir: no lo hacemos nosotros, lo hace Revit, con la misma orden que darias a mano.
// Las rejillas del anfitrion no entran en la seleccion, asi que se quedan.
//
// PostCommand es asincrono: corre cuando este comando termina. Por eso aca no se abre
// ninguna transaccion. El deshacer que queda es el de Revit, "Ocultar elementos".
//
// Para volver atras: Ctrl+Z, o la bombilla "Mostrar elementos ocultos" de la barra de
// abajo, seleccionar y "Mostrar en vista > Elementos".

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Riga.Vistas.Comandos
{
    [Transaction(TransactionMode.Manual)]
    public class CmdRejillasVinculos : IExternalCommand
    {
        const int PROFUNDIDAD = 8;      // corta un anidamiento circular

        public Result Execute(ExternalCommandData datos, ref string mensaje, ElementSet elementos)
        {
            var uiapp = datos.Application;
            var uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                mensaje = "No hay ningun proyecto abierto.";
                return Result.Failed;
            }

            var doc = uidoc.Document;
            var vista = uidoc.ActiveGraphicalView ?? doc.ActiveView;

            if (vista == null || vista.IsTemplate)
            {
                Avisar("Esta vista no admite ocultar elementos.", vista);
                return Result.Cancelled;
            }

            if (vista is ViewSheet)
            {
                Avisar("Estas en un plano. Metete en la vista (doble clic en el cuadro) y "
                     + "corre el boton ahi: lo que se oculta se oculta en la vista, no en el plano.", vista);
                return Result.Cancelled;
            }

            // Solo los vinculos que se ven en esta vista. El colector va acotado a la vista
            // activa, que ya esta dibujada, asi que no cuesta nada.
            List<RevitLinkInstance> vinculos;
            try
            {
                vinculos = new FilteredElementCollector(doc, vista.Id)
                               .OfClass(typeof(RevitLinkInstance))
                               .Cast<RevitLinkInstance>()
                               .ToList();
            }
            catch
            {
                vinculos = new FilteredElementCollector(doc)
                               .OfClass(typeof(RevitLinkInstance))
                               .Cast<RevitLinkInstance>()
                               .ToList();
            }

            if (vinculos.Count == 0)
            {
                Avisar("No hay vinculos de Revit en esta vista.", vista);
                return Result.Cancelled;
            }

            var refs = new List<Reference>();
            int descargados = 0, anidados = 0, fallidas = 0;

            foreach (var v in vinculos)
            {
                Document dv = null;
                try { dv = v.GetLinkDocument(); } catch { }
                if (dv == null) { descargados++; continue; }

                Recolectar(dv, new List<RevitLinkInstance> { v }, refs,
                           ref anidados, ref fallidas, 1);
            }

            if (refs.Count == 0)
            {
                Avisar(Diagnostico(vinculos.Count, descargados, fallidas), vista);
                return Result.Cancelled;
            }

            var idCmd = RevitCommandId.LookupPostableCommandId(PostableCommand.HideElements);
            if (idCmd == null || !uiapp.CanPostCommand(idCmd))
            {
                Avisar("Revit no acepta ahora mismo la orden de ocultar. Cierra cualquier "
                     + "dialogo o comando en curso y volve a intentar.", vista);
                return Result.Cancelled;
            }

            try
            {
                uidoc.Selection.SetReferences(refs);
            }
            catch (Exception ex)
            {
                Avisar("No se pudieron seleccionar las rejillas de los vinculos.\n\n"
                     + ex.Message, vista);
                return Result.Cancelled;
            }

            uiapp.PostCommand(idCmd);      // corre al salir de aca
            return Result.Succeeded;
        }

        /// <summary>
        /// Junta las rejillas de un documento vinculado y baja a sus propios vinculos.
        /// La cadena va del anfitrion hacia adentro; la referencia se arma al reves,
        /// del vinculo mas interno hacia afuera, que es como la entiende Revit.
        /// </summary>
        static void Recolectar(Document dv, List<RevitLinkInstance> cadena, List<Reference> refs,
                               ref int anidados, ref int fallidas, int nivel)
        {
            foreach (var g in new FilteredElementCollector(dv)
                                  .OfCategory(BuiltInCategory.OST_Grids)
                                  .WhereElementIsNotElementType())
            {
                try
                {
                    var r = new Reference(g);
                    for (int i = cadena.Count - 1; i >= 0; i--)
                        r = r.CreateLinkReference(cadena[i]);
                    if (r != null) refs.Add(r);
                    else fallidas++;
                }
                catch { fallidas++; }
            }

            if (nivel >= PROFUNDIDAD) return;

            foreach (var sub in new FilteredElementCollector(dv)
                                    .OfClass(typeof(RevitLinkInstance))
                                    .Cast<RevitLinkInstance>())
            {
                Document ds = null;
                try { ds = sub.GetLinkDocument(); } catch { }
                if (ds == null) continue;

                anidados++;
                cadena.Add(sub);
                Recolectar(ds, cadena, refs, ref anidados, ref fallidas, nivel + 1);
                cadena.RemoveAt(cadena.Count - 1);
            }
        }

        static string Diagnostico(int total, int descargados, int fallidas)
        {
            if (descargados == total)
                return "Los " + total + " vinculos de la vista estan descargados. "
                     + "Cargalos y volve a intentar.";

            string t = "Los vinculos de esta vista no tienen rejillas.";
            if (descargados > 0)
                t += "\n\nHay " + descargados + " vinculo" + (descargados == 1 ? "" : "s")
                   + " descargado" + (descargados == 1 ? "" : "s") + ", que no se pudo revisar.";
            if (fallidas > 0)
                t += "\n\n" + fallidas + " referencia" + (fallidas == 1 ? "" : "s")
                   + " no se pudo armar.";
            return t;
        }

        static void Avisar(string texto, View vista)
        {
            string donde = vista == null ? "" : "\n\nVista: " + vista.Name + " (" + vista.ViewType + ")";
            TaskDialog.Show("Rejillas de vinculos", texto + donde);
        }
    }
}
