using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        public Ventana(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            CargarParametros();

            _vistaParametros = CollectionViewSource.GetDefaultView(ParametrosDisponibles);
            _vistaParametros.Filter = FiltroBusqueda;
            listaParametros.ItemsSource = _vistaParametros;
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
                    ParametrosDisponibles.Add(new ParametroItem
                    {
                        Definicion = definition,
                        Nombre = definition.Name,
                        Binding = binding,
                        Seleccionado = true // Por defecto seleccionados
                    });
                }
            }

            ParametrosDisponibles = ParametrosDisponibles.OrderBy(p => p.Nombre).ToList();
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
        }

        private void Ejecutar_Click(object sender, RoutedEventArgs e)
        {
            var seleccionados = ParametrosDisponibles.Where(p => p.Seleccionado).ToList();
            if (seleccionados.Count == 0)
            {
                MessageBox.Show("Debes seleccionar al menos un parámetro.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
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
                                // Iterar sobre los parámetros del elemento para encontrar el que coincida con la Definicion.
                                // Ya que elem.get_Parameter(Definition) fue descontinuado.
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
                                // Elemento bloqueado o no editable
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

    public class ParametroItem
    {
        public Definition Definicion { get; set; }
        public InstanceBinding Binding { get; set; }
        public string Nombre { get; set; }
        public bool Seleccionado { get; set; }
    }
}
