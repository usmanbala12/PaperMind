using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PaperMind.Views
{
    public partial class JobViewForm : UserControl
    {
        public JobViewForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
