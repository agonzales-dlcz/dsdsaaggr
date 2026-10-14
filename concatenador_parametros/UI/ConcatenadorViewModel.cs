using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Autodesk.Revit.DB;

namespace Riga.ConcatenadorParametros.UI
{
    public class ConcatenadorViewModel : INotifyPropertyChanged
    {
        private bool _usarSeleccion = true;
        public bool UsarSeleccion
        {
            get => _usarSeleccion;
            set { _usarSeleccion = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsarCategoria)); }
        }

        public bool UsarCategoria => !_usarSeleccion;

        private Category _categoriaSeleccionada;
        public Category CategoriaSeleccionada
        {
            get => _categoriaSeleccionada;
            set { _categoriaSeleccionada = value; OnPropertyChanged(); }
        }

        private ObservableCollection<Category> _categorias = new ObservableCollection<Category>();
        public ObservableCollection<Category> Categorias
        {
            get => _categorias;
            set { _categorias = value; OnPropertyChanged(); }
        }

        private ObservableCollection<string> _parametrosDisponibles = new ObservableCollection<string>();
        public ObservableCollection<string> ParametrosDisponibles
        {
            get => _parametrosDisponibles;
            set { _parametrosDisponibles = value; OnPropertyChanged(); }
        }

        private ObservableCollection<string> _parametrosDestinoDisponibles = new ObservableCollection<string>();
        public ObservableCollection<string> ParametrosDestinoDisponibles
        {
            get => _parametrosDestinoDisponibles;
            set { _parametrosDestinoDisponibles = value; OnPropertyChanged(); }
        }

        private ObservableCollection<ParametroItem> _parametrosOrigenSeleccionados = new ObservableCollection<ParametroItem>();
        public ObservableCollection<ParametroItem> ParametrosOrigenSeleccionados
        {
            get => _parametrosOrigenSeleccionados;
            set { _parametrosOrigenSeleccionados = value; OnPropertyChanged(); }
        }

        private string _separador = "-";
        public string Separador
        {
            get => _separador;
            set { _separador = value; OnPropertyChanged(); }
        }

        private string _parametroDestino;
        public string ParametroDestino
        {
            get => _parametroDestino;
            set { _parametroDestino = value; OnPropertyChanged(); }
        }

        public ICommand SeleccionarCommand { get; }
        public ICommand AñadirParametroCommand { get; }
        public ICommand QuitarParametroCommand { get; }
        public ICommand EjecutarCommand { get; }

        public enum DialogAction
        {
            None,
            Select,
            Execute
        }

        public DialogAction CurrentAction { get; set; } = DialogAction.None;

        public event Action CloseWindowRequest;

        public ConcatenadorViewModel()
        {
            SeleccionarCommand = new RelayCommand(_ =>
            {
                CurrentAction = DialogAction.Select;
                CloseWindowRequest?.Invoke();
            });

            AñadirParametroCommand = new RelayCommand(_ => ParametrosOrigenSeleccionados.Add(new ParametroItem()));

            QuitarParametroCommand = new RelayCommand(p =>
            {
                if (p is ParametroItem pi) ParametrosOrigenSeleccionados.Remove(pi);
            });

            EjecutarCommand = new RelayCommand(_ =>
            {
                CurrentAction = DialogAction.Execute;
                CloseWindowRequest?.Invoke();
            });

            // Agregar un parámetro inicial por defecto
            ParametrosOrigenSeleccionados.Add(new ParametroItem());
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class ParametroItem : INotifyPropertyChanged
    {
        private string _valor;
        public string Valor
        {
            get => _valor;
            set { _valor = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        public RelayCommand(Action<object> execute) => _execute = execute;
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute(parameter);
    }
}
