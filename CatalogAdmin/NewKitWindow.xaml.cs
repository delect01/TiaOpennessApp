using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using TiaOpennessApp.Models.Generation;
using TiaOpennessApp.Services;

namespace TiaCatalogAdmin
{
    /// <summary>Derives a kit from one (fixed) or several (modular) example devices of a project.</summary>
    public partial class NewKitWindow : Window
    {
        private readonly string _catalogSource;
        private readonly AdminSettings _settings;
        private GenerationDocument? _doc;

        public NewKitWindow(string catalogSource, AdminSettings settings)
        {
            InitializeComponent();
            _catalogSource = catalogSource;
            _settings = settings;
            if (!string.IsNullOrWhiteSpace(settings.KitSource) && (Directory.Exists(settings.KitSource) || File.Exists(settings.KitSource)))
                LoadSource(settings.KitSource!);
        }

        public string? CreatedKitId { get; private set; }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Export folder (Devices.csv, Modules.csv...)" };
            if (dialog.ShowDialog(this) == true)
                LoadSource(dialog.FolderName);
        }

        private void BrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Project database (*.db)|*.db", Title = "project.db" };
            if (dialog.ShowDialog(this) == true)
                LoadSource(dialog.FileName);
        }

        private void LoadSource(string path)
        {
            GenerationParseResult parsed = path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                ? new DbGenerationReader(path).Read()
                : CsvGenerationReader.ForFolder(path).Read();
            SqliteConnection.ClearAllPools();
            if (parsed.HasErrors)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, parsed.Issues.Take(10).Select(i => $"{i.Source}: {i.Message}")),
                    "Cannot read the project", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _doc = parsed.Document;
            SourceBox.Text = path;
            _settings.KitSource = path;
            _settings.Save();
            DeviceList.ItemsSource = _doc.Devices
                .OrderBy(d => d.TypeKey).ThenBy(d => d.DeviceName)
                .Select(d => new DeviceItem(d, _doc))
                .ToList();
        }

        private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            List<DeviceItem> selected = DeviceList.SelectedItems.Cast<DeviceItem>().ToList();
            if (selected.Count == 0)
                return;
            DeviceSpec first = selected[0].Device;
            if (KitIdBox.Text.Length == 0)
                KitIdBox.Text = first.TypeKey.Replace('-', '_').Replace(' ', '_');
            if (KitNameBox.Text.Length == 0)
                KitNameBox.Text = string.IsNullOrWhiteSpace(first.Comment) ? first.TypeKey : $"{first.Comment} ({first.TypeKey})";
            if (selected.Count > 1)
                ModularCheck.IsChecked = true;
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null)
                return;
            List<string> devices = DeviceList.SelectedItems.Cast<DeviceItem>().Select(i => i.Device.DeviceName).ToList();
            if (devices.Count == 0 || KitIdBox.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "Choose at least one device and give the kit an id.", Title);
                return;
            }

            try
            {
                KitFromDeviceResult derived = KitService.FromDevices(_doc, devices, KitIdBox.Text.Trim(), KitNameBox.Text.Trim(),
                    includeTags: TagsCheck.IsChecked == true, modular: ModularCheck.IsChecked == true);

                using (SqliteConnection db = CatalogDatabase.Open(_catalogSource))
                {
                    if (CatalogDatabase.LoadKits(db).Any(k => string.Equals(k.Id, derived.Kit.Id, StringComparison.OrdinalIgnoreCase)) &&
                        MessageBox.Show(this, $"Kit '{derived.Kit.Id}' already exists. Replace it?", Title,
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return;
                    CatalogDatabase.SaveKit(db, derived.Kit);
                }

                if (derived.Warnings.Count > 0)
                    MessageBox.Show(this, string.Join(Environment.NewLine + Environment.NewLine, derived.Warnings),
                        "Kit created with warnings", MessageBoxButton.OK, MessageBoxImage.Information);

                CreatedKitId = derived.Kit.Id;
                DialogResult = true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException)
            {
                MessageBox.Show(this, ex.Message, "Kit not created", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private sealed class DeviceItem
        {
            public DeviceItem(DeviceSpec device, GenerationDocument doc)
            {
                Device = device;
                int modules = doc.Modules.Count(m => string.Equals(m.Device, device.DeviceName, StringComparison.OrdinalIgnoreCase));
                int ranges = doc.ModuleAddresses.Count(a => string.Equals(a.Device, device.DeviceName, StringComparison.OrdinalIgnoreCase));
                Display = $"{device.DeviceName,-14} {device.TypeKey,-22} {modules} module(s), {ranges} I/Q range(s)" +
                          (string.IsNullOrWhiteSpace(device.KitId) ? string.Empty : $"  [kit {device.KitId}]");
            }

            public DeviceSpec Device { get; }
            public string Display { get; }
        }
    }
}
