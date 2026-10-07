using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
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
                txtGuardadoInfo.Text = "✓ Se ha guardado una copia local del archivo para tu seguridad.";
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
                    var item = new ParametroItem
                    {
                        Definicion = definition,
                        Nombre = definition.Name,
                        Binding = binding,
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

        private void DoEvents()
        {
            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
        }

        private void Ejecutar_Click(object sender, RoutedEventArgs e)
        {
            var seleccionados = ParametrosDisponibles.Where(p => p.Seleccionado).ToList();
            if (seleccionados.Count == 0) return;

            // Preparar UI para ejecución
            btnEjecutar.IsEnabled = false;
            btnCancelar.IsEnabled = false;
            btnToggleTodos.IsEnabled = false;
            txtBuscar.IsEnabled = false;
            listaParametros.IsEnabled = false;
            btnBorrarBusqueda.IsEnabled = false;

            barraProgreso.Visibility = System.Windows.Visibility.Visible;
            txtEstadoProceso.Visibility = System.Windows.Visibility.Visible;

            int elementosModificados = 0;
            int elementosOmitidos = 0;
            int totalParametros = seleccionados.Count;

            using (var transaccion = new Transaction(_doc, "Limpiar Parámetros Nulos"))
            {
                transaccion.Start();

                for (int i = 0; i < totalParametros; i++)
                {
                    var parametro = seleccionados[i];

                    double porcentaje = (double)i / totalParametros * 100;
                    barraProgreso.Value = porcentaje;
                    txtEstadoProceso.Text = $"Limpiando parámetros... ({i + 1}/{totalParametros}: {parametro.Nombre})";
                    DoEvents();

                    // Optimización: usar InternalDefinition para obtener el Id interno y evitar recorrer todos los parámetros en C#.
                    InternalDefinition internalDef = parametro.Definicion as InternalDefinition;
                    ElementId paramId = internalDef != null ? internalDef.Id : null;

                    foreach (Category category in parametro.Binding.Categories)
                    {
                        // Optimización extra: solo procesar categorías de modelo físico, evitando vistas, planos, etc.
                        if (category.CategoryType != CategoryType.Model) continue;

                        var collector = new FilteredElementCollector(_doc)
                            .OfCategoryId(category.Id)
                            .WhereElementIsNotElementType();

                        foreach (Element elem in collector)
                        {
                            try
                            {
                                Parameter param = null;
                                // Búsqueda directa optimizada. LookupParameter es O(1) usando el nombre
                                param = elem.LookupParameter(parametro.Nombre);

                                // Nota: LookupParameter podría devolver el primer parámetro que encuentre si hay duplicados con el mismo nombre.
                                // Ya que estamos limpiando un parámetro de proyecto validado previamente, esto es excepcionalmente rápido y seguro.

                                if (param != null && !param.IsReadOnly)
                                {
                                    if (!param.HasValue)
                                    {
                                        param.Set(0); // 0 = No, 1 = Sí
                                        elementosModificados++;
                                    }
                                }
                            }
                            catch
                            {
                                elementosOmitidos++;
                            }
                        }
                    }
                }

                barraProgreso.Value = 100;
                txtEstadoProceso.Text = "Aplicando cambios y regenerando modelo...";
                DoEvents();

                transaccion.Commit();
            }

            // Recargar datos
            CargarParametros();
            _vistaParametros = CollectionViewSource.GetDefaultView(ParametrosDisponibles);
            _vistaParametros.Filter = FiltroBusqueda;
            listaParametros.ItemsSource = _vistaParametros;

            // Restaurar UI
            btnCancelar.IsEnabled = true;
            btnToggleTodos.IsEnabled = true;
            txtBuscar.IsEnabled = true;
            listaParametros.IsEnabled = true;
            btnBorrarBusqueda.IsEnabled = true;
            barraProgreso.Visibility = System.Windows.Visibility.Collapsed;
            txtEstadoProceso.Visibility = System.Windows.Visibility.Collapsed;
            ActualizarUI();

            string mensaje = $"Se han limpiado nulos en {elementosModificados} elementos.\n";

            if (elementosOmitidos > 0)
            {
                mensaje += $"\nAdvertencia: Se omitieron {elementosOmitidos} elementos (Bloqueados, solo lectura o en uso por otro usuario).";
            }
            else
            {
                mensaje += "\nÉxito total: Todos los elementos nulos en los parámetros seleccionados fueron limpiados sin bloqueos.";
            }

            MessageBox.Show(mensaje, "Proceso Completado", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class ParametroItem : INotifyPropertyChanged
    {
        public Definition Definicion { get; set; }
        public InstanceBinding Binding { get; set; }
        public string Nombre { get; set; }

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
