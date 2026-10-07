using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Riga.LimpiarNulos.Ui
{
    public partial class Ventana : Window
    {
        private UIDocument _uidoc;
        private Document _doc;
        public List<ParametroItem> ParametrosDisponibles { get; set; }
        private ICollectionView _vistaParametros;
        private bool _todosSeleccionados = false;
        private bool _guardadoPrevio = false;
        private CancellationTokenSource _cts;

        public Ventana(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;

            GuardarLocalmente();
            CargarParametros();

            _vistaParametros = CollectionViewSource.GetDefaultView(ParametrosDisponibles);
            _vistaParametros.Filter = FiltroBusqueda;
            listaParametros.ItemsSource = _vistaParametros;

            ActualizarUI();
        }

        private void GuardarLocalmente()
        {
            if (string.IsNullOrEmpty(_doc.PathName))
            {
                txtGuardadoInfo.Text = "Aviso: Este proyecto es nuevo y nunca ha sido guardado.";
                txtGuardadoInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkOrange);
                _guardadoPrevio = false;
                return;
            }

            try
            {
                _doc.Save();
                _guardadoPrevio = true;
                txtGuardadoInfo.Text = "✓ Se ha guardado localmente el archivo por seguridad.";
            }
            catch (Exception ex)
            {
                txtGuardadoInfo.Text = "⚠ No se pudo guardar automáticamente el archivo local.";
                txtGuardadoInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            }
        }

        private void CargarParametros()
        {
            ParametrosDisponibles = new List<ParametroItem>();
            var iterator = _doc.ParameterBindings.ForwardIterator();

            while (iterator.MoveNext())
            {
                var binding = iterator.Current as InstanceBinding;
                var definition = iterator.Key;

                if (binding != null && definition.GetDataType() == SpecTypeId.Boolean.YesNo)
                {
                    // Determinar categorías de modelo a evaluar
                    var categoriasDeModelo = new List<ElementId>();
                    foreach (Category category in binding.Categories)
                    {
                        if (category.CategoryType == CategoryType.Model)
                        {
                            categoriasDeModelo.Add(category.Id);
                        }
                    }

                    string porcentajeTexto = "0% Nulls";
                    if (categoriasDeModelo.Count > 0)
                    {
                        // 1. Total elements in these categories
                        var catFilter = new ElementMulticategoryFilter(categoriasDeModelo);
                        int totalElementos = new FilteredElementCollector(_doc)
                            .WhereElementIsNotElementType()
                            .WherePasses(catFilter)
                            .GetElementCount();

                        if (totalElementos > 0)
                        {
                            // 2. Elementos que SÍ tienen valor
                            // En booleanos, los valores posibles son 0 o 1
                            InternalDefinition internalDef = definition as InternalDefinition;
                            ElementId paramId = internalDef != null ? internalDef.Id : null;

                            if (paramId != null)
                            {
                                // Obtener los que ya tienen valor 0 o 1
                                var rule0 = ParameterFilterRuleFactory.CreateEqualsRule(paramId, 0);
                                var rule1 = ParameterFilterRuleFactory.CreateEqualsRule(paramId, 1);

                                var filter0 = new ElementParameterFilter(rule0);
                                var filter1 = new ElementParameterFilter(rule1);
                                var hasValueFilter = new LogicalOrFilter(filter0, filter1);

                                int tienenValor = new FilteredElementCollector(_doc)
                                    .WhereElementIsNotElementType()
                                    .WherePasses(catFilter)
                                    .WherePasses(hasValueFilter)
                                    .GetElementCount();

                                int nulos = totalElementos - tienenValor;
                                int porcentaje = (int)Math.Round((double)nulos / totalElementos * 100);
                                porcentajeTexto = $"{porcentaje}% Nulls";
                            }
                        }
                    }

                    var item = new ParametroItem
                    {
                        Definicion = definition,
                        Nombre = definition.Name,
                        Binding = binding,
                        CategoriasValidas = categoriasDeModelo,
                        PorcentajeNullsTexto = porcentajeTexto,
                        Seleccionado = false
                    };
                    item.PropertyChanged += Item_PropertyChanged;
                    ParametrosDisponibles.Add(item);
                }
            }

            ParametrosDisponibles = ParametrosDisponibles.OrderBy(p => p.Nombre).ToList();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ParametroItem.Seleccionado))
            {
                ActualizarUI();
            }
        }

        private void ActualizarUI()
        {
            int seleccionados = ParametrosDisponibles.Count(p => p.Seleccionado);
            txtContador.Text = $"{seleccionados} Parámetro{(seleccionados == 1 ? "" : "s")} seleccionado{(seleccionados == 1 ? "" : "s")}";
            btnEjecutar.Content = $"Limpiar {seleccionados} parámetro{(seleccionados == 1 ? "" : "s")}";
            btnEjecutar.IsEnabled = seleccionados > 0;

            int visibles = _vistaParametros.Cast<ParametroItem>().Count();
            if (visibles > 0 && visibles == _vistaParametros.Cast<ParametroItem>().Count(p => p.Seleccionado))
            {
                _todosSeleccionados = true;
                btnToggleTodos.Content = "Deseleccionar todos";
            }
            else
            {
                _todosSeleccionados = false;
                btnToggleTodos.Content = "Seleccionar todos";
            }
        }

        private bool FiltroBusqueda(object item)
        {
            if (string.IsNullOrWhiteSpace(txtBuscar.Text))
                return true;

            var param = item as ParametroItem;
            return param.Nombre.IndexOf(txtBuscar.Text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void txtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            btnBorrarBusqueda.Visibility = string.IsNullOrEmpty(txtBuscar.Text) ?
                System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

            _vistaParametros.Refresh();

            if (_vistaParametros.IsEmpty)
            {
                txtSinResultados.Visibility = System.Windows.Visibility.Visible;
                listaParametros.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                txtSinResultados.Visibility = System.Windows.Visibility.Collapsed;
                listaParametros.Visibility = System.Windows.Visibility.Visible;
            }

            ActualizarUI();
        }

        private void BorrarBusqueda_Click(object sender, RoutedEventArgs e)
        {
            txtBuscar.Text = string.Empty;
            txtBuscar.Focus();
        }

        private void Fila_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is ParametroItem parametro)
            {
                parametro.Seleccionado = !parametro.Seleccionado;
                e.Handled = true;
            }
        }

        private void ToggleTodos_Click(object sender, RoutedEventArgs e)
        {
            bool nuevoEstado = !_todosSeleccionados;

            foreach (ParametroItem item in _vistaParametros)
            {
                item.Seleccionado = nuevoEstado;
            }
        }

        private async void Ejecutar_Click(object sender, RoutedEventArgs e)
        {
            var seleccionados = ParametrosDisponibles.Where(p => p.Seleccionado).ToList();
            if (seleccionados.Count == 0) return;

            // Preparar UI para ejecución
            btnEjecutar.IsEnabled = false;
            btnToggleTodos.IsEnabled = false;
            txtBuscar.IsEnabled = false;
            listaParametros.IsEnabled = false;
            btnBorrarBusqueda.IsEnabled = false;

            // Cambiar comportamiento del botón Cerrar a Cancelar
            btnCancelar.Content = "Cancelar";
            _cts = new CancellationTokenSource();

            barraProgreso.Visibility = System.Windows.Visibility.Visible;
            txtEstadoProceso.Visibility = System.Windows.Visibility.Visible;

            int elementosModificados = 0;
            int elementosOmitidos = 0;
            int totalParametros = seleccionados.Count;
            bool fueCancelado = false;

            using (var transaccion = new Transaction(_doc, "Limpiar Parámetros Nulos"))
            {
                transaccion.Start();

                for (int i = 0; i < totalParametros; i++)
                {
                    if (_cts.Token.IsCancellationRequested)
                    {
                        fueCancelado = true;
                        break;
                    }

                    var parametro = seleccionados[i];

                    double porcentaje = (double)i / totalParametros * 100;
                    barraProgreso.Value = porcentaje;
                    txtEstadoProceso.Text = $"Limpiando parámetros... ({i + 1}/{totalParametros}: {parametro.Nombre})";

                    // Permitir que la UI se actualice y procese el click de cancelar
                    await Dispatcher.Yield();

                    if (parametro.CategoriasValidas == null || parametro.CategoriasValidas.Count == 0) continue;

                    InternalDefinition internalDef = parametro.Definicion as InternalDefinition;
                    ElementId paramId = internalDef != null ? internalDef.Id : null;

                    if (paramId == null) continue;

                    // Usar un filtro invertido (no tiene los valores válidos) para obtener SOLO los elementos que tienen el parametro Nulo
                    var catFilter = new ElementMulticategoryFilter(parametro.CategoriasValidas);
                    var rule0 = ParameterFilterRuleFactory.CreateEqualsRule(paramId, 0);
                    var rule1 = ParameterFilterRuleFactory.CreateEqualsRule(paramId, 1);
                    var hasValueFilter = new LogicalOrFilter(new ElementParameterFilter(rule0), new ElementParameterFilter(rule1));

                    // Obtener los que ya tienen 0 o 1
                    var idsConValor = new FilteredElementCollector(_doc)
                        .WhereElementIsNotElementType()
                        .WherePasses(catFilter)
                        .WherePasses(hasValueFilter)
                        .ToElementIds();

                    // Exclusión: Obtener todos, menos los que ya tienen valor (Excluding falla si se le pasa lista vacía)
                    var collectorNulos = new FilteredElementCollector(_doc)
                        .WhereElementIsNotElementType()
                        .WherePasses(catFilter);

                    if (idsConValor.Count > 0)
                    {
                        collectorNulos.Excluding(idsConValor);
                    }

                    foreach (Element elem in collectorNulos)
                    {
                        if (_cts.Token.IsCancellationRequested) break;

                        try
                        {
                            Parameter param = elem.LookupParameter(parametro.Nombre);
                            if (param != null && !param.IsReadOnly && !param.HasValue)
                            {
                                param.Set(0); // 0 = No, 1 = Sí
                                elementosModificados++;
                            }
                        }
                        catch
                        {
                            elementosOmitidos++;
                        }
                    }
                }

                if (fueCancelado)
                {
                    txtEstadoProceso.Text = "Cancelando y guardando avance parcial...";
                    await Dispatcher.Yield();
                }
                else
                {
                    barraProgreso.Value = 100;
                    txtEstadoProceso.Text = "Aplicando cambios y regenerando modelo...";
                    await Dispatcher.Yield();
                }

                transaccion.Commit();
            }

            // Recargar datos
            CargarParametros();
            _vistaParametros = CollectionViewSource.GetDefaultView(ParametrosDisponibles);
            _vistaParametros.Filter = FiltroBusqueda;
            listaParametros.ItemsSource = _vistaParametros;

            // Restaurar UI
            btnCancelar.Content = "Cerrar";
            _cts?.Dispose();
            _cts = null;

            btnCancelar.IsEnabled = true;
            btnToggleTodos.IsEnabled = true;
            txtBuscar.IsEnabled = true;
            listaParametros.IsEnabled = true;
            btnBorrarBusqueda.IsEnabled = true;
            barraProgreso.Visibility = System.Windows.Visibility.Collapsed;
            txtEstadoProceso.Visibility = System.Windows.Visibility.Collapsed;
            ActualizarUI();

            string mensaje = fueCancelado ?
                $"El proceso fue cancelado. Se aplicaron cambios en {elementosModificados} elementos hasta ese momento.\n" :
                $"Se han limpiado nulos en {elementosModificados} elementos.\n";

            if (elementosOmitidos > 0)
            {
                mensaje += $"\nAdvertencia: Se omitieron {elementosOmitidos} elementos (Bloqueados, solo lectura o en uso por otro usuario).";
            }
            else if (!fueCancelado)
            {
                mensaje += "\nÉxito total: Todos los elementos nulos en los parámetros seleccionados fueron limpiados sin bloqueos.";
            }

            MessageBox.Show(mensaje, fueCancelado ? "Proceso Cancelado" : "Proceso Completado", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                btnCancelar.IsEnabled = false; // Evitar multiples clicks
            }
            else if (_cts == null)
            {
                Close();
            }
        }
    }

    public class ParametroItem : INotifyPropertyChanged
    {
        public Definition Definicion { get; set; }
        public InstanceBinding Binding { get; set; }
        public string Nombre { get; set; }
        public List<ElementId> CategoriasValidas { get; set; }
        public string PorcentajeNullsTexto { get; set; }

        private bool _seleccionado;
        public bool Seleccionado
        {
            get => _seleccionado;
            set
            {
                if (_seleccionado != value)
                {
                    _seleccionado = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Seleccionado)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
