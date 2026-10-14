using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Riga.ConcatenadorParametros.UI;

namespace Riga.ConcatenadorParametros.Comandos
{
    [Transaction(TransactionMode.Manual)]
    public class CmdConcatenadorParametros : IExternalCommand
    {
        private UIDocument _uidoc;
        private Document _doc;
        private ConcatenadorWindow _window;
        private ConcatenadorViewModel _vm;
        private List<ElementId> _elementosSeleccionados = new List<ElementId>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            _uidoc = commandData.Application.ActiveUIDocument;
            _doc = _uidoc.Document;

            _vm = new ConcatenadorViewModel();
            CargarCategoriasDisponibles();

            bool keepRunning = true;

            while (keepRunning)
            {
                _vm.AccionActual = ConcatenadorViewModel.AccionVentana.Ninguna;

                _window = new ConcatenadorWindow(_vm);
                _window.ShowDialog();

                if (_vm.AccionActual == ConcatenadorViewModel.AccionVentana.Ninguna)
                {
                    // User closed the window with the X button
                    keepRunning = false;
                }
                else if (_vm.AccionActual == ConcatenadorViewModel.AccionVentana.Seleccionar)
                {
                    try
                    {
                        var referencias = _uidoc.Selection.PickObjects(ObjectType.Element, "Seleccione elementos para concatenar parámetros (Esc para cancelar)");
                        _elementosSeleccionados = referencias.Select(r => r.ElementId).ToList();
                        ExtraerParametrosDeElementos(_elementosSeleccionados);
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        // User pressed Esc. Do nothing, just let the loop continue and reopen the window.
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Error", "Error en la selección: " + ex.Message);
                    }
                }
                else if (_vm.AccionActual == ConcatenadorViewModel.AccionVentana.Ejecutar)
                {
                    bool success = EjecutarConcatenacion();
                    if (success)
                    {
                        keepRunning = false;
                    }
                }
            }

            return Result.Succeeded;
        }

        private void CargarCategoriasDisponibles()
        {
            var categoriasId = ParameterFilterUtilities.GetAllFilterableCategories();
            var categorias = categoriasId.Select(id => Category.GetCategory(_doc, id))
                                         .Where(c => c != null)
                                         .OrderBy(c => c.Name)
                                         .ToList();

            foreach (var c in categorias)
            {
                _vm.Categorias.Add(c);
            }
            if (_vm.Categorias.Any())
                _vm.CategoriaSeleccionada = _vm.Categorias.First();

            // As default mode is selection, load parameters based on initial selection if any
            var seleccionActual = _uidoc.Selection.GetElementIds();
            if (seleccionActual.Any())
            {
                _elementosSeleccionados = seleccionActual.ToList();
                ExtraerParametrosDeElementos(_elementosSeleccionados);
            }
            else
            {
                _vm.UsarSeleccion = false; // Switch to Category mode if nothing is selected initially
                ExtraerParametrosPorCategoria();
            }

            _vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ConcatenadorViewModel.CategoriaSeleccionada) && _vm.UsarCategoria)
                {
                    ExtraerParametrosPorCategoria();
                }
                else if (e.PropertyName == nameof(ConcatenadorViewModel.UsarCategoria) && _vm.UsarCategoria)
                {
                    ExtraerParametrosPorCategoria();
                }
            };
        }

        private void ExtraerParametrosPorCategoria()
        {
            if (_vm.CategoriaSeleccionada == null) return;

            var collector = new FilteredElementCollector(_doc)
                .OfCategoryId(_vm.CategoriaSeleccionada.Id)
                .WhereElementIsNotElementType();

            var elementos = collector.ToElementIds().ToList();
            ExtraerParametrosDeElementos(elementos);
        }

        private void ExtraerParametrosDeElementos(List<ElementId> ids)
        {
            // Preserve selected values to avoid resetting the ComboBoxes
            var valoresSeleccionados = _vm.ParametrosOrigenSeleccionados
                .Select(p => p.Valor)
                .ToList();
            var valorDestinoSeleccionado = _vm.ParametroDestino;

            _vm.ParametrosDisponibles.Clear();
            _vm.ParametrosDestinoDisponibles.Clear();

            var hashTodos = new HashSet<string>();
            var hashDestino = new HashSet<string>();

            // Parametros sinteticos
            hashTodos.Add("[Categoría]");
            hashTodos.Add("[Familia]");
            hashTodos.Add("[Tipo]");

            var elementosRepresentativos = new List<Element>();
            var visitados = new HashSet<ElementId>();

            foreach (var id in ids)
            {
                var el = _doc.GetElement(id);
                if (el == null) continue;

                var typeId = el.GetTypeId();
                // Si no tiene tipo, lo agrupamos por su categoria para tener representatividad
                var key = typeId != ElementId.InvalidElementId ? typeId : el.Category?.Id;

                if (key != null && !visitados.Contains(key))
                {
                    visitados.Add(key);
                    elementosRepresentativos.Add(el);
                }
            }

            foreach (var el in elementosRepresentativos)
            {
                // Instance parameters
                foreach (Parameter p in el.Parameters)
                {
                    if (p.Definition != null)
                    {
                        hashTodos.Add(p.Definition.Name);
                        if (!p.IsReadOnly && p.StorageType == StorageType.String)
                        {
                            hashDestino.Add(p.Definition.Name);
                        }
                    }
                }

                // Type parameters (for sources only)
                var typeId = el.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    var typeEl = _doc.GetElement(typeId) as ElementType;
                    if (typeEl != null)
                    {
                        foreach (Parameter p in typeEl.Parameters)
                        {
                            if (p.Definition != null)
                            {
                                hashTodos.Add(p.Definition.Name);
                            }
                        }
                    }
                }
            }

            foreach (var p in hashTodos.OrderBy(x => x)) _vm.ParametrosDisponibles.Add(p);
            foreach (var p in hashDestino.OrderBy(x => x)) _vm.ParametrosDestinoDisponibles.Add(p);

            // Restore selected values if they still exist
            for (int i = 0; i < _vm.ParametrosOrigenSeleccionados.Count && i < valoresSeleccionados.Count; i++)
            {
                if (valoresSeleccionados[i] != null && _vm.ParametrosDisponibles.Contains(valoresSeleccionados[i]))
                {
                    _vm.ParametrosOrigenSeleccionados[i].Valor = valoresSeleccionados[i];
                }
            }

            if (valorDestinoSeleccionado != null && _vm.ParametrosDestinoDisponibles.Contains(valorDestinoSeleccionado))
            {
                _vm.ParametroDestino = valorDestinoSeleccionado;
            }
        }

        private bool EjecutarConcatenacion()
        {
            try
            {
                if (string.IsNullOrEmpty(_vm.ParametroDestino))
                {
                    TaskDialog.Show("Error", "Debe seleccionar un parámetro destino.");
                    return false;
                }

                var origenes = _vm.ParametrosOrigenSeleccionados
                    .Where(p => !string.IsNullOrEmpty(p.Valor))
                    .Select(p => p.Valor)
                    .ToList();

                if (!origenes.Any())
                {
                    TaskDialog.Show("Error", "Debe seleccionar al menos un parámetro de origen.");
                    return false;
                }

                List<ElementId> elementosAProcesar;
                if (_vm.UsarSeleccion)
                {
                    elementosAProcesar = _elementosSeleccionados;
                }
                else
                {
                    if (_vm.CategoriaSeleccionada == null) return false;
                    elementosAProcesar = new FilteredElementCollector(_doc)
                        .OfCategoryId(_vm.CategoriaSeleccionada.Id)
                        .WhereElementIsNotElementType()
                        .ToElementIds()
                        .ToList();
                }

                if (!elementosAProcesar.Any())
                {
                    TaskDialog.Show("Info", "No hay elementos para procesar.");
                    return false;
                }

                int procesados = 0;

                using (var tx = new Transaction(_doc, "Concatenar Parámetros"))
                {
                    tx.Start();

                    foreach (var id in elementosAProcesar)
                    {
                        var el = _doc.GetElement(id);
                        if (el == null) continue;

                        // Worksharing check
                        if (_doc.IsWorkshared)
                        {
                            var status = WorksharingUtils.GetCheckoutStatus(_doc, el.Id);
                            if (status == CheckoutStatus.OwnedByOtherUser) continue;
                        }

                        var paramDestino = el.GetParameters(_vm.ParametroDestino)
                            .FirstOrDefault(p => !p.IsReadOnly && p.StorageType == StorageType.String);
                        if (paramDestino == null) continue;

                        var valores = new List<string>();

                        foreach (var nombre in origenes)
                        {
                            string val = null;
                            if (nombre == "[Categoría]") val = el.Category?.Name;
                            else if (nombre == "[Familia]") val = ObtenerNombreFamilia(el);
                            else if (nombre == "[Tipo]") val = el.Name;
                            else
                            {
                                var p = ObtenerParametroPorNombre(el, nombre);
                                if (p != null)
                                {
                                    val = ExtraerValorParametro(p);
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                valores.Add(val);
                            }
                        }

                        string resultado = string.Join(_vm.Separador ?? "", valores);
                        paramDestino.Set(resultado);
                        procesados++;
                    }

                    tx.Commit();
                }

                TaskDialog.Show("Resultado", $"Se actualizaron {procesados} elementos.");
                return true;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", "Ocurrió un error al ejecutar: " + ex.Message);
                return false;
            }
        }

        private string ObtenerNombreFamilia(Element el)
        {
            if (el is FamilyInstance fi && fi.Symbol != null)
                return fi.Symbol.FamilyName;

            var typeId = el.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                var typeEl = _doc.GetElement(typeId) as ElementType;
                if (typeEl != null) return typeEl.FamilyName;
            }
            return null;
        }

        private Parameter ObtenerParametroPorNombre(Element el, string nombre)
        {
            var pInstancia = el.GetParameters(nombre).FirstOrDefault(x => x.HasValue)
                             ?? el.GetParameters(nombre).FirstOrDefault();

            if (pInstancia != null)
                return pInstancia;

            var typeId = el.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                var typeEl = _doc.GetElement(typeId);
                return typeEl?.GetParameters(nombre).FirstOrDefault();
            }

            return null;
        }

        private string ExtraerValorParametro(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString();
                case StorageType.Double:
                case StorageType.Integer:
                    return p.AsValueString();
                case StorageType.ElementId:
                    var id = p.AsElementId();
                    if (id != ElementId.InvalidElementId)
                    {
                        var el = _doc.GetElement(id);
                        return el?.Name;
                    }
                    return null;
                default:
                    return null;
            }
        }
    }
}
