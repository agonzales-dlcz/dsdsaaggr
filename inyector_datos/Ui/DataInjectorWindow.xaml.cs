using Autodesk.Revit.DB;
using Microsoft.Win32;
using Riga.InyectorDatos.Helpers;
using Riga.InyectorDatos.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Riga.InyectorDatos.Ui
{
    public partial class DataInjectorWindow : Window
    {
        private Document _doc;
        private List<string> _availableParameters;
        private List<Dictionary<string, string>> _csvData;
        public ObservableCollection<CsvMappingRow> Mappings { get; set; }

        public DataInjectorWindow(Document doc, List<string> availableParameters)
        {
            InitializeComponent();
            _doc = doc;
            _availableParameters = availableParameters;
            Mappings = new ObservableCollection<CsvMappingRow>();
            MappingsItemsControl.ItemsSource = Mappings;

            // Populate Revit PK combo
            CmbRevitPrimaryKey.ItemsSource = _availableParameters;
        }

        private void BrowseCsv_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

            if (openFileDialog.ShowDialog() == true)
            {
                TxtCsvPath.Text = openFileDialog.FileName;
                LoadCsv(openFileDialog.FileName);
            }
        }

        private void LoadCsv(string filepath)
        {
            _csvData = CsvParserHelper.Parse(filepath);
            if (_csvData.Count == 0) return;

            var headers = _csvData[0].Keys.ToList();

            // Populate PK combo
            CmbCsvPrimaryKey.ItemsSource = headers;

            // Build mappings
            Mappings.Clear();
            foreach (var header in headers)
            {
                var row = new CsvMappingRow
                {
                    CsvColumnName = header,
                    AvailableRevitParameters = _availableParameters
                };

                // Auto-suggest logic: match header with Revit parameter case-insensitively
                var match = _availableParameters.FirstOrDefault(p => p.Equals(header, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    row.RevitParameterName = match;
                    ValidateMapping(row);
                }

                Mappings.Add(row);
            }

            // Populate DataGrid
            BuildDataGrid();
        }

        private void BuildDataGrid()
        {
            if (_csvData == null || _csvData.Count == 0) return;

            DataTable table = new DataTable();
            foreach (var header in _csvData[0].Keys)
            {
                table.Columns.Add(header);
            }

            // Preview first 50 rows max
            int count = Math.Min(50, _csvData.Count);
            for (int i = 0; i < count; i++)
            {
                var row = table.NewRow();
                foreach (var kvp in _csvData[i])
                {
                    row[kvp.Key] = kvp.Value;
                }
                table.Rows.Add(row);
            }

            CsvDataGrid.ItemsSource = table.DefaultView;
        }

        private void CmbCsvPrimaryKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCsvPrimaryKey.SelectedItem is string selectedCsvPk)
            {
                // Auto-suggest logic for Primary Key
                var match = _availableParameters.FirstOrDefault(p => p.Equals(selectedCsvPk, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    CmbRevitPrimaryKey.SelectedItem = match;
                }
            }
        }

        private void MappingRevitParameter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.DataContext is CsvMappingRow row)
            {
                ValidateMapping(row);
            }
        }

        private void ValidateMapping(CsvMappingRow row)
        {
            if (string.IsNullOrEmpty(row.RevitParameterName) || _csvData == null || _csvData.Count == 0)
            {
                row.IsValid = true;
                row.RequiredStorageType = "";
                return;
            }

            // Find an instance element to check the parameter's StorageType
            var collector = new FilteredElementCollector(_doc).WhereElementIsNotElementType();
            Parameter sampleParam = null;

            // Just scan until we find one element that actually has this parameter to determine type
            foreach (var elem in collector)
            {
                foreach (Parameter p in elem.Parameters)
                {
                    if (p.Definition != null && p.Definition.Name == row.RevitParameterName)
                    {
                        sampleParam = p;
                        break;
                    }
                }
                if (sampleParam != null) break;
            }

            if (sampleParam == null)
            {
                row.IsValid = true;
                row.RequiredStorageType = "Desconocido";
                return;
            }

            row.RequiredStorageType = sampleParam.StorageType.ToString();

            // Sample validation (check up to 10 rows)
            int sampleCount = Math.Min(10, _csvData.Count);
            bool isValid = true;

            for (int i = 0; i < sampleCount; i++)
            {
                if (_csvData[i].TryGetValue(row.CsvColumnName, out string csvVal))
                {
                    if (string.IsNullOrWhiteSpace(csvVal)) continue;

                    switch (sampleParam.StorageType)
                    {
                        case StorageType.Integer:
                            if (!int.TryParse(csvVal, out _)) isValid = false;
                            break;
                        case StorageType.Double:
                            if (!double.TryParse(csvVal, out _)) isValid = false;
                            break;
                        case StorageType.ElementId:
                            if (!long.TryParse(csvVal, out _)) isValid = false;
                            break;
                        case StorageType.String:
                        case StorageType.None:
                            break; // String accepts anything
                    }
                }
                if (!isValid) break;
            }

            row.IsValid = isValid;
        }

        private void RunInjection_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CmbCsvPrimaryKey.SelectedItem as string) ||
                string.IsNullOrEmpty(CmbRevitPrimaryKey.SelectedItem as string))
            {
                MessageBox.Show("Seleccione las claves primarias antes de inyectar.", "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Run cleanly inside a transaction on main thread
            try
            {
                ExecuteInjection();
                MessageBox.Show("Inyección completada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error durante la inyección:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteInjection()
        {
            string csvPkCol = CmbCsvPrimaryKey.SelectedItem.ToString();
            string revitPkParam = CmbRevitPrimaryKey.SelectedItem.ToString();

            // O(1) Dictionary: Build a fast dictionary from the CSV using the chosen Primary Key.
            var csvDict = new Dictionary<string, Dictionary<string, string>>();
            foreach (var row in _csvData)
            {
                if (row.TryGetValue(csvPkCol, out string pkVal) && !string.IsNullOrWhiteSpace(pkVal))
                {
                    csvDict[pkVal] = row;
                }
            }

            // Get valid mappings to inject
            var mappingsToInject = Mappings.Where(m => !string.IsNullOrEmpty(m.RevitParameterName) && m.IsValid).ToList();

            using (Transaction t = new Transaction(_doc, "Inyectar datos de CSV"))
            {
                t.Start();

                var collector = new FilteredElementCollector(_doc).WhereElementIsNotElementType();

                foreach (var elem in collector)
                {
                    // Worksharing Safety: skip elements owned by others
                    if (_doc.IsWorkshared &&
                        WorksharingUtils.GetCheckoutStatus(_doc, elem.Id) == CheckoutStatus.OwnedByOtherUser)
                    {
                        continue;
                    }

                    // Find Revit PK parameter
                    Parameter pkParameter = null;
                    foreach (Parameter p in elem.Parameters)
                    {
                        if (p.Definition != null && p.Definition.Name == revitPkParam)
                        {
                            pkParameter = p;
                            break;
                        }
                    }

                    if (pkParameter == null) continue;

                    string elemPkVal = null;
                    switch (pkParameter.StorageType)
                    {
                        case StorageType.String:
                            elemPkVal = pkParameter.AsString();
                            break;
                        case StorageType.Integer:
                            elemPkVal = pkParameter.AsInteger().ToString();
                            break;
                        case StorageType.Double:
                            elemPkVal = pkParameter.AsDouble().ToString();
                            break;
                        case StorageType.ElementId:
                            elemPkVal = pkParameter.AsElementId().Value.ToString();
                            break;
                        case StorageType.None:
                            elemPkVal = pkParameter.AsValueString();
                            break;
                    }

                    if (string.IsNullOrEmpty(elemPkVal))
                    {
                        elemPkVal = pkParameter.AsValueString() ?? pkParameter.AsString();
                    }

                    if (elemPkVal != null && csvDict.TryGetValue(elemPkVal, out var csvRow))
                    {
                        // Found a match in O(1) dictionary, inject mapped data
                        foreach (var mapping in mappingsToInject)
                        {
                            if (csvRow.TryGetValue(mapping.CsvColumnName, out string csvValue))
                            {
                                Parameter targetParam = null;
                                foreach (Parameter p in elem.Parameters)
                                {
                                    if (p.Definition != null && p.Definition.Name == mapping.RevitParameterName)
                                    {
                                        targetParam = p;
                                        break;
                                    }
                                }

                                if (targetParam != null && !targetParam.IsReadOnly)
                                {
                                    // StorageType conversion before Set()
                                    switch (targetParam.StorageType)
                                    {
                                        case StorageType.String:
                                            targetParam.Set(csvValue);
                                            break;
                                        case StorageType.Integer:
                                            if (int.TryParse(csvValue, out int intVal))
                                            {
                                                targetParam.Set(intVal);
                                            }
                                            break;
                                        case StorageType.Double:
                                            if (double.TryParse(csvValue, out double doubleVal))
                                            {
                                                targetParam.Set(doubleVal);
                                            }
                                            break;
                                        case StorageType.ElementId:
                                            if (long.TryParse(csvValue, out long idVal))
                                            {
                                                // The prompt specifies Revit 2025 API, which uses long for ElementId constructor
                                                targetParam.Set(new ElementId(idVal));
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                    }
                }

                t.Commit();
            }
        }
    }
}
