using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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

        public Ventana(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            CargarParametros();

            _vistaParametros = CollectionViewSource.GetDefaultView(ParametrosDisponibles);
            _vistaParametros.Filter = FiltroBusqueda;
            listaParametros.ItemsSource = _vistaParametros;

            ActualizarUI();
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
                        Seleccionado = false // Por defecto desmarcados
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
            _vistaParametros.Refresh();

            if (_vistaParametros.IsEmpty)
            {
                txtSinResultados.Visibility = Visibility.Visible;
                listaParametros.Visibility = Visibility.Collapsed;
            }
            else
            {
                txtSinResultados.Visibility = Visibility.Collapsed;
                listaParametros.Visibility = Visibility.Visible;
            }

            ActualizarUI();
        }

        private void Fila_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item && item.DataContext is ParametroItem parametro)
            {
                parametro.Seleccionado = !parametro.Seleccionado;
                e.Handled = true; // Prevenir que la lista cambie la selección visual
            }
        }

        private void ToggleTodos_Click(object sender, RoutedEventArgs e)
        {
            bool nuevoEstado = !_todosSeleccionados;

            // Solo cambiamos los que están visibles en la búsqueda
            foreach (ParametroItem item in _vistaParametros)
            {
                item.Seleccionado = nuevoEstado;
            }
        }

        private void Ejecutar_Click(object sender, RoutedEventArgs e)
        {
            var seleccionados = ParametrosDisponibles.Where(p => p.Seleccionado).ToList();
            if (seleccionados.Count == 0) return;

            // Validar si el documento ha sido guardado al menos una vez
            if (string.IsNullOrEmpty(_doc.PathName))
            {
                MessageBox.Show("Este proyecto nunca ha sido guardado. Por favor, guárdalo al menos una vez antes de limpiar los nulos para evitar pérdidas de avance.",
                                "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Guardar local antes de limpiar
            try
            {
                _doc.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar el documento localmente:\n{ex.Message}\n\nLa limpieza continuará de todas formas.",
                                "Advertencia de guardado", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            int elementosModificados = 0;
            int elementosOmitidos = 0;

            using (var transaccion = new Transaction(_doc, "Limpiar Parámetros Nulos"))
            {
                transaccion.Start();

                foreach (var parametro in seleccionados)
                {
                    foreach (Category category in parametro.Binding.Categories)
                    {
                        var elementos = new FilteredElementCollector(_doc)
                            .OfCategoryId(category.Id)
                            .WhereElementIsNotElementType()
                            .ToElements();

                        foreach (var elem in elementos)
                        {
                            try
                            {
                                Parameter param = null;
                                foreach (Parameter p in elem.Parameters)
                                {
                                    if (p.Definition.Name == parametro.Definicion.Name)
                                    {
                                        param = p;
                                        break;
                                    }
                                }

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

                transaccion.Commit();
            }

            string mensaje = $"Se han limpiado nulos en {elementosModificados} elementos.\n";
            if (elementosOmitidos > 0)
            {
                mensaje += $"\nAdvertencia: Se omitieron {elementosOmitidos} elementos (Bloqueados, solo lectura o en uso por otro usuario).";
            }
            else
            {
                mensaje += "\nÉxito total: Todos los elementos nulos en los parámetros seleccionados fueron limpiados sin bloqueos.";
            }

            MessageBox.Show(mensaje, "Resultado", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
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
