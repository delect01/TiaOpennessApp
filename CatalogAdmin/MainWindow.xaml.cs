using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using TiaOpennessApp.Models.Catalog;
using TiaOpennessApp.Models.Generation;
using TiaOpennessApp.Services;

namespace TiaCatalogAdmin
{
    public partial class MainWindow : Window
    {
        private string? _sourcePath;
        private KitDefinition? _kit; // working copy of the selected kit
        private readonly ObservableCollection<AllowedRow> _allowedRows = new();
        private readonly ObservableCollection<KitTag> _tagRows = new();
        private readonly AdminSettings _settings = AdminSettings.Load();

        public MainWindow()
        {
            InitializeComponent();
            AllowedGrid.ItemsSource = _allowedRows;
            TagsGrid.ItemsSource = _tagRows;
            ShareRootBox.Text = _settings.ShareRoot ?? string.Empty;
            PrivateKeyBox.Text = _settings.PrivateKey ?? string.Empty;
            ExtraFilesBox.Text = _settings.ExtraFiles ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(_settings.SourcePath) && File.Exists(_settings.SourcePath))
                OpenSource(_settings.SourcePath!);
            SuggestVersion();
        }

        // ------------------------------------------------------------------ catalog source

        private void OpenSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Catalog source (*.db)|*.db", Title = "Open catalog source" };
            if (dialog.ShowDialog(this) == true)
                OpenSource(dialog.FileName);
        }

        private void NewSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "Catalog source (*.db)|*.db", FileName = "catalog-source.db", Title = "New catalog source" };
            if (dialog.ShowDialog(this) != true)
                return;
            if (File.Exists(dialog.FileName))
            {
                Status($"'{dialog.FileName}' already exists; use Open source.", error: true);
                return;
            }
            using (CatalogDatabase.Open(dialog.FileName, create: true)) { }
            OpenSource(dialog.FileName);
            Status("New catalog source created. Import the types from Catalog.csv next.");
        }

        private void ImportTypes_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Catalog CSV (*.csv)|*.csv", Title = "Import types from Catalog.csv" };
            if (dialog.ShowDialog(this) != true || _sourcePath == null)
                return;
            List<CatalogEntry> types = CatalogService.Load(dialog.FileName);
            using (SqliteConnection db = CatalogDatabase.Open(_sourcePath))
            {
                var (added, updated) = CatalogDatabase.UpsertTypes(db, types);
                Status($"Types imported: {added} added, {updated} updated.");
            }
            Reload();
        }

        private void OpenSource(string path)
        {
            try
            {
                using (CatalogDatabase.Open(path)) { } // creates / upgrades the schema
            }
            catch (Exception ex)
            {
                Status($"Cannot open '{path}': {ex.Message}", error: true);
                return;
            }
            _sourcePath = path;
            _settings.SourcePath = path;
            _settings.Save();
            SourcePathText.Text = path;
            Tabs.IsEnabled = true;
            ImportTypesButton.IsEnabled = true;
            Reload();
        }

        private void Reload(string? selectKitId = null)
        {
            if (_sourcePath == null)
                return;
            using SqliteConnection db = CatalogDatabase.Open(_sourcePath);
            List<CatalogEntry> types = CatalogDatabase.LoadTypes(db);
            List<KitDefinition> kits = CatalogDatabase.LoadKits(db);
            TypesGrid.ItemsSource = types;
            KitList.ItemsSource = kits.Select(k => new KitItem(k)).ToList();
            Status($"{types.Count} types, {kits.Count} kits.");

            string? id = selectKitId ?? _kit?.Id;
            KitItem? again = KitList.Items.Cast<KitItem>().FirstOrDefault(i => string.Equals(i.Kit.Id, id, StringComparison.OrdinalIgnoreCase));
            if (again != null)
                KitList.SelectedItem = again;
            else
                ShowKit(null);
        }

        // ------------------------------------------------------------------ kits

        private void KitList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            ShowKit((KitList.SelectedItem as KitItem)?.Kit);

        private void ShowKit(KitDefinition? kit)
        {
            // Work on a copy so "Discard changes" just reloads.
            _kit = kit == null ? null : JsonSerializer.Deserialize<KitDefinition>(JsonSerializer.Serialize(kit));
            KitPanel.IsEnabled = _kit != null;
            KitIdBox.Text = _kit?.Id ?? string.Empty;
            KitNameBox.Text = _kit?.Name ?? string.Empty;
            KitDescriptionBox.Text = _kit?.Description ?? string.Empty;
            KitTypeBox.Text = _kit?.DeviceTypeKey ?? string.Empty;
            KitFirmwareBox.Text = _kit?.Firmware ?? string.Empty;
            KitCommentBox.Text = _kit?.DeviceComment ?? string.Empty;
            FixedPartBox.Text = _kit == null ? string.Empty : DescribeFixedPart(_kit);

            _allowedRows.Clear();
            foreach (KitModuleOption option in _kit?.AllowedModules ?? new List<KitModuleOption>())
                _allowedRows.Add(new AllowedRow(option));
            AllowedHeader.Text = _kit?.IsModular == true
                ? "Allowed modules (modular kit): optional limits per module"
                : "Allowed modules: none (fixed layout, every copy is identical)";

            _tagRows.Clear();
            foreach (KitTag tag in _kit?.Tags ?? new List<KitTag>())
                _tagRows.Add(tag);
        }

        private static string DescribeFixedPart(KitDefinition kit)
        {
            var lines = new List<string>();
            lines.AddRange(kit.Modules.Select(m => $"module     {m.ModuleName,-26} slot {(m.Slot?.ToString() ?? "-"),-3} {m.TypeKey}"));
            lines.AddRange(kit.Interfaces.Select(i => $"interface  {i.InterfaceItem,-26} subnet {i.Subnet}, IO system '{i.MasterIoSystem}'"));
            lines.AddRange(kit.Addresses.Select(a => $"I/Q range  {a.ModuleName}/{a.SubModuleName} {a.IoType} {a.LengthBits} bits (start = next free)"));
            return lines.Count == 0 ? "(no fixed modules)" : string.Join(Environment.NewLine, lines);
        }

        private void SaveKit_Click(object sender, RoutedEventArgs e)
        {
            if (_kit == null || _sourcePath == null)
                return;
            AllowedGrid.CommitEdit(DataGridEditingUnit.Row, true);
            TagsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            _kit.Name = KitNameBox.Text.Trim();
            _kit.Description = KitDescriptionBox.Text.Trim();
            _kit.Firmware = KitFirmwareBox.Text.Trim();
            _kit.DeviceComment = KitCommentBox.Text.Trim();
            _kit.AllowedModules = _allowedRows.Select(r => r.ToOption()).ToList();
            _kit.Tags = _tagRows.ToList();

            try
            {
                using SqliteConnection db = CatalogDatabase.Open(_sourcePath);
                CatalogDatabase.SaveKit(db, _kit);
            }
            catch (InvalidDataException ex)
            {
                Status(ex.Message.Replace(Environment.NewLine, " "), error: true);
                return;
            }
            Reload(_kit.Id);
            Status($"Kit '{_kit.Id}' saved. Publish a new version to make it available to users.");
        }

        private void DiscardKit_Click(object sender, RoutedEventArgs e) => Reload(_kit?.Id);

        private void DeleteKit_Click(object sender, RoutedEventArgs e)
        {
            if (KitList.SelectedItem is not KitItem item || _sourcePath == null)
                return;
            if (MessageBox.Show(this, $"Delete kit '{item.Kit.Id}' from the catalog source?\n\nProjects that use it will stop " +
                                      "passing the approval check once a version without it is published.",
                    "Delete kit", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            using (SqliteConnection db = CatalogDatabase.Open(_sourcePath))
                CatalogDatabase.DeleteKit(db, item.Kit.Id);
            _kit = null;
            Reload();
            Status($"Kit '{item.Kit.Id}' deleted from the catalog source.");
        }

        private void NewKit_Click(object sender, RoutedEventArgs e)
        {
            if (_sourcePath == null)
                return;
            var window = new NewKitWindow(_sourcePath, _settings) { Owner = this };
            if (window.ShowDialog() == true)
            {
                Reload(window.CreatedKitId);
                Status($"Kit '{window.CreatedKitId}' created. Review it, set the limits and save; then publish.");
            }
        }

        // ------------------------------------------------------------------ publish

        private void BrowseShare_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Approved catalog folder (network share)" };
            if (dialog.ShowDialog(this) == true)
            {
                ShareRootBox.Text = dialog.FolderName;
                SuggestVersion();
            }
        }

        private void BrowseKey_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Private key (*.json)|*.json", Title = "Private catalog key" };
            if (dialog.ShowDialog(this) == true)
                PrivateKeyBox.Text = dialog.FileName;
        }

        private void BrowseExtra_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Folder with approved files (templates, block library)" };
            if (dialog.ShowDialog(this) == true)
                ExtraFilesBox.Text = dialog.FolderName;
        }

        private void Publish_Click(object sender, RoutedEventArgs e)
        {
            if (_sourcePath == null)
                return;
            string share = ShareRootBox.Text.Trim();
            string version = VersionBox.Text.Trim();
            string keyPath = PrivateKeyBox.Text.Trim();
            string? extra = string.IsNullOrWhiteSpace(ExtraFilesBox.Text) ? null : ExtraFilesBox.Text.Trim();
            if (share.Length == 0 || version.Length == 0 || keyPath.Length == 0)
            {
                Log("Fill in the catalog folder, the new version and the private key.");
                return;
            }

            _settings.ShareRoot = share;
            _settings.PrivateKey = keyPath;
            _settings.ExtraFiles = extra;
            _settings.Save();

            if (MessageBox.Show(this, $"Publish catalog version {version} to\n{share}\n\nIt becomes the current version for every user.",
                    "Publish", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            try
            {
                CatalogKey key = CatalogKey.Load(keyPath);
                string package = ApprovedCatalog.PublishFromSource(_sourcePath, version, share, key, Environment.UserName, extra);
                CatalogVerification check = ApprovedCatalog.Verify(package, key.PublicOnly());
                Log($"Published {package}");
                Log($"  key {key.KeyId}, {check.Manifest?.Files.Count ?? 0} file(s), verification {(check.IsValid ? "OK" : "FAILED: " + string.Join("; ", check.Errors))}");
                if (CatalogKey.Embedded is { } embedded && embedded.KeyId != key.KeyId)
                    Log($"  WARNING: this key ({key.KeyId}) is not the one built into the apps ({embedded.KeyId}); users' apps will reject this catalog.");
                SuggestVersion();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is InvalidOperationException ||
                                       ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                Log($"ERROR: {ex.Message}");
            }
        }

        private void Verify_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string package = ApprovedCatalog.ResolvePackageDir(ShareRootBox.Text.Trim());
                CatalogVerification check = ApprovedCatalog.Verify(package); // key built into the apps
                Log($"Verify {package} with the key built into the apps:");
                foreach (string error in check.Errors)
                    Log($"  ERROR: {error}");
                if (check.IsValid)
                {
                    ApprovedCatalog catalog = ApprovedCatalog.Load(package);
                    Log($"  VALID - version {catalog.Version}, {catalog.Types.Count} types, {catalog.Kits.Count} kits " +
                        $"(published {check.Manifest!.PublishedUtc} by {check.Manifest.Publisher})");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException)
            {
                Log($"ERROR: {ex.Message}");
            }
        }

        /// <summary>Next version = current "catalog-vN" + 1 when N is a number.</summary>
        private void SuggestVersion()
        {
            try
            {
                string current = Path.Combine(ShareRootBox.Text.Trim(), ApprovedCatalog.CurrentFileName);
                if (File.Exists(current) && int.TryParse(File.ReadAllText(current).Trim().Replace(ApprovedCatalog.PackagePrefix, string.Empty), out int n))
                    VersionBox.Text = (n + 1).ToString();
            }
            catch (Exception) { /* leave the box as it is */ }
        }

        // ------------------------------------------------------------------ helpers

        private void Status(string text, bool error = false)
        {
            StatusText.Text = text;
            StatusText.Foreground = error ? System.Windows.Media.Brushes.DarkRed : System.Windows.Media.Brushes.Black;
        }

        private void Log(string line)
        {
            PublishLog.AppendText(line + Environment.NewLine);
            PublishLog.ScrollToEnd();
        }

        private sealed class KitItem
        {
            public KitItem(KitDefinition kit) => Kit = kit;
            public KitDefinition Kit { get; }
            public string Display => $"{Kit.Id}  —  {Kit.Name}{(Kit.IsModular ? "  [modular]" : string.Empty)}";
        }
    }

    /// <summary>Editable row for an allowed module (limits only; type and ranges stay as derived).</summary>
    public sealed class AllowedRow
    {
        private readonly KitModuleOption _option;

        public AllowedRow(KitModuleOption option)
        {
            _option = option;
            MinSlot = option.MinSlot;
            MaxSlot = option.MaxSlot;
            MaxCount = option.MaxCount;
        }

        public string Name => _option.Name;
        public string TypeKey => string.IsNullOrEmpty(_option.TypeKey) ? "(GSD submodule)" : _option.TypeKey;
        public string Ranges => string.Join(", ", _option.Addresses.Select(a => a.IoType + a.LengthBits));
        public int? MinSlot { get; set; }
        public int? MaxSlot { get; set; }
        public int? MaxCount { get; set; }

        public KitModuleOption ToOption()
        {
            _option.MinSlot = MinSlot;
            _option.MaxSlot = MaxSlot;
            _option.MaxCount = MaxCount;
            return _option;
        }
    }

    /// <summary>Last used paths, in %AppData%\TiaCatalogAdmin\settings.json.</summary>
    public sealed class AdminSettings
    {
        public string? SourcePath { get; set; }
        public string? ShareRoot { get; set; }
        public string? PrivateKey { get; set; }
        public string? ExtraFiles { get; set; }
        public string? KitSource { get; set; }

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TiaCatalogAdmin", "settings.json");

        public static AdminSettings Load()
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<AdminSettings>(File.ReadAllText(FilePath)) ?? new AdminSettings()
                    : new AdminSettings();
            }
            catch (Exception)
            {
                return new AdminSettings();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception) { /* settings are a convenience */ }
        }
    }
}
