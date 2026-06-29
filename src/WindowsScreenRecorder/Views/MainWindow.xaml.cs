using System.Windows;
using System.Windows.Input;
using WindowsScreenRecorder.ViewModels;
using WindowsScreenRecorder.Views.Controls;

namespace WindowsScreenRecorder.Views
{
    /// <summary>
    /// Code-behind for MainWindow.xaml.
    /// Responsibilities limited to pure view concerns: window chrome drag/resize,
    /// minimize/maximize/close button clicks, and settings overlay dismissal.
    /// All application logic lives in MainViewModel.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SettingsViewModel _settingsViewModel;

        // ────────────────────────────────────────────────────────────────────────
        // Construction
        // ────────────────────────────────────────────────────────────────────────

        public MainWindow(MainViewModel viewModel, SettingsViewModel settingsViewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _settingsViewModel = settingsViewModel;

            // Assign the SettingsPanel DataContext after the visual tree is built
            Loaded += OnLoaded;

            // Wire window state changes so the maximize button icon can toggle
            StateChanged += (_, _) => UpdateMaximizeIcon();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Walk the visual tree to find the SettingsPanel UserControl and assign
            // its DataContext from the DI-resolved SettingsViewModel singleton.
            // This keeps the XAML clean and avoids ServiceLocator anti-patterns.
            var panel = FindSettingsPanel(this);
            if (panel is not null)
            {
                panel.DataContext = _settingsViewModel;
                _settingsViewModel.LoadFromCurrent();
            }
        }

        /// <summary>
        /// Recursively searches the visual tree for the first <see cref="SettingsPanel"/> child.
        /// </summary>
        private static SettingsPanel? FindSettingsPanel(DependencyObject parent)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is SettingsPanel panel)
                    return panel;

                var result = FindSettingsPanel(child);
                if (result is not null)
                    return result;
            }
            return null;
        }

        // ────────────────────────────────────────────────────────────────────────
        // Custom title bar
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Allows the user to drag the window by pressing the custom title bar area.
        /// Double-click toggles maximize/restore.
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            // Begin native window drag so Windows handles snapping, Aero Shake, etc.
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        // ────────────────────────────────────────────────────────────────────────
        // Window control buttons
        // ────────────────────────────────────────────────────────────────────────

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Give the ViewModel a chance to clean up (stop recording, flush logs)
            if (DataContext is MainViewModel vm)
                vm.OnWindowClosing();

            Close();
        }

        // ────────────────────────────────────────────────────────────────────────
        // Settings overlay
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Clicking the semi-transparent overlay backdrop closes the settings panel.
        /// </summary>
        private void SettingsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.CloseSettingsCommand.Execute(null);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────────

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        /// <summary>
        /// Called whenever WindowState changes. Notifies ViewModel so the maximize
        /// button icon (restore vs maximize) can update through binding if desired.
        /// </summary>
        private void UpdateMaximizeIcon()
        {
            // The maximize/restore icon swap is handled via XAML triggers bound to
            // WindowState, so no extra code is required here in most cases.
            // This hook is available for future icon animation or custom behavior.
        }
    }
}
