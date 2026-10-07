using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Riga.InyectorDatos.Models
{
    public class CsvMappingRow : INotifyPropertyChanged
    {
        private string _csvColumnName;
        private string _revitParameterName;
        private string _requiredStorageType;
        private bool _isValid = true;

        public string CsvColumnName
        {
            get => _csvColumnName;
            set
            {
                if (_csvColumnName != value)
                {
                    _csvColumnName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string RevitParameterName
        {
            get => _revitParameterName;
            set
            {
                if (_revitParameterName != value)
                {
                    _revitParameterName = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<string> AvailableRevitParameters { get; set; } = new List<string>();

        public string RequiredStorageType
        {
            get => _requiredStorageType;
            set
            {
                if (_requiredStorageType != value)
                {
                    _requiredStorageType = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsValid
        {
            get => _isValid;
            set
            {
                if (_isValid != value)
                {
                    _isValid = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
