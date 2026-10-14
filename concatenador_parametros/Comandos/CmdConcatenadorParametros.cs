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
            _vm.SolicitarSeleccion += OnSolicitarSeleccion;
            _vm.SolicitarEjecucion += OnSolicitarEjecucion;

            CargarCategoriasDisponibles();

            _window = new ConcatenadorWindow(_vm);
            _window.ShowDialog();

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

        private void OnSolicitarSeleccion()
        {
            _window.Hide();
            try
            {
                var referencias = _uidoc.Selection.PickObjects(ObjectType.Element, "Seleccione elementos para concatenar parámetros");
                _elementosSeleccionados = referencias.Select(r => r.ElementId).ToList();
                ExtraerParametrosDeElementos(_elementosSeleccionados);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // Usuario canceló la selección
            }
            _window.ShowDialog();
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
                foreach (Parameter p in el.Parameters)
                {
                    if (p.Definition != null)
                    {
                        hashTodos.Add(p.Definition.Name);
                        if (!p.IsReadOnly)
                        {
                            hashDestino.Add(p.Definition.Name);
                        }
                    }
                }
            }

            foreach (var p in hashTodos.OrderBy(x => x)) _vm.ParametrosDisponibles.Add(p);
            foreach (var p in hashDestino.OrderBy(x => x)) _vm.ParametrosDestinoDisponibles.Add(p);
        }

        private void OnSolicitarEjecucion()
        {
            if (string.IsNullOrEmpty(_vm.ParametroDestino))
            {
                TaskDialog.Show("Error", "Debe seleccionar un parámetro destino.");
                return;
            }

            var origenes = _vm.ParametrosOrigenSeleccionados
                .Where(p => !string.IsNullOrEmpty(p.Valor))
                .Select(p => p.Valor)
                .ToList();

            if (!origenes.Any())
            {
                TaskDialog.Show("Error", "Debe seleccionar al menos un parámetro de origen.");
                return;
            }

            List<ElementId> elementosAProcesar;
            if (_vm.UsarSeleccion)
            {
                elementosAProcesar = _elementosSeleccionados;
            }
            else
            {
                if (_vm.CategoriaSeleccionada == null) return;
                elementosAProcesar = new FilteredElementCollector(_doc)
                    .OfCategoryId(_vm.CategoriaSeleccionada.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .ToList();
            }

            if (!elementosAProcesar.Any())
            {
                TaskDialog.Show("Info", "No hay elementos para procesar.");
                return;
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

                    var paramDestino = ObtenerParametroPorNombre(el, _vm.ParametroDestino);
                    if (paramDestino == null || paramDestino.IsReadOnly) continue;

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
            _window.Close();
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
            foreach (Parameter p in el.Parameters)
            {
                if (p.Definition != null && p.Definition.Name == nombre)
                    return p;
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
