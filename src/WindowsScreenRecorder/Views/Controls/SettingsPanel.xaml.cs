using System.Windows.Controls;
using WindowsScreenRecorder.ViewModels;

namespace WindowsScreenRecorder.Views.Controls
{
    /// <summary>
    /// Code-behind for SettingsPanel.xaml.
    /// The DataContext is set by the parent window via DI; no logic lives here.
    /// </summary>
    public partial class SettingsPanel : UserControl
    {
        public SettingsPanel()
        {
            InitializeComponent();
        }
    }
}
