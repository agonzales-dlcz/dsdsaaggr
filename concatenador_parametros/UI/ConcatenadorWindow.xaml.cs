using System.Windows;

namespace Riga.ConcatenadorParametros.UI
{
    public partial class ConcatenadorWindow : Window
    {
        public ConcatenadorWindow(object viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            if (viewModel is ConcatenadorViewModel vm)
            {
                vm.CloseWindowRequest += () => this.Close();
            }
        }
    }
}
