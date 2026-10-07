using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Riga.InyectorDatos.Ui;
using System.Collections.Generic;
using System.Linq;

namespace Riga.InyectorDatos.Comandos
{
    [Transaction(TransactionMode.Manual)]
    public class DataInjectorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;

            // AUTO-SAVE FEATURE: Safety check before injecting data.
            // If the document has a valid path, save it to secure progress.
            // If it's a new unsaved document, skip saving silently.
            if (!string.IsNullOrEmpty(doc.PathName))
            {
                try
                {
                    doc.Save(new SaveOptions());
                }
                catch
                {
                    // If save fails for some reason (e.g. read-only), proceed anyway.
                }
            }

            // Collect available instance parameters using Representative Sampling
            var availableParameters = GetRepresentativeInstanceParameters(doc);

            // Show the WPF window
            var window = new DataInjectorWindow(doc, availableParameters);
            window.ShowDialog();

            return Result.Succeeded;
        }

        /// <summary>
        /// Representative Sampling Algorithm:
        /// Finds one instance element per Category to quickly collect all unique instance parameter names
        /// without iterating the entire document or freezing the UI.
        /// Time Complexity: O(C * P) where C is unique categories and P is parameters per element.
        /// </summary>
        private List<string> GetRepresentativeInstanceParameters(Document doc)
        {
            var parameterNames = new HashSet<string>();
            var visitedCategories = new HashSet<ElementId>();

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                var categoryId = element.Category?.Id;
                if (categoryId == null || categoryId == ElementId.InvalidElementId)
                    continue;

                if (visitedCategories.Add(categoryId))
                {
                    // This is the first element we've seen for this category.
                    // Extract all its instance parameter names.
                    foreach (Parameter param in element.Parameters)
                    {
                        if (param.Definition != null)
                        {
                            parameterNames.Add(param.Definition.Name);
                        }
                    }
                }
            }

            return parameterNames.OrderBy(n => n).ToList();
        }
    }
}
