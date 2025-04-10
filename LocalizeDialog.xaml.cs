using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Win32;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Input;

namespace LocalizeExtension
{
    public partial class LocalizeDialog : Window
    {
        private const string CollectionPath = "LocalizeExtension\\Options";
        private const string PrefixKey = "KeyPrefix";
        private const string ResxPathKey = "ResxPath";

        public string ResourceName { get; private set; }
        public string ResourceValue { get; private set; }
        public string KeyPrefix => txtPrefix.Text.Trim();
        public string ResxPath => txtResXPath.Text.Trim();

        private bool _isFirstLoad = true;

        // Модель для динамических полей
        private class LocaleField
        {
            public string Label { get; set; }
            public string Value { get; set; }
        }

        private readonly List<LocaleField> _localeFields = new();

        /// <summary>
        /// Словарь culture → введённое значение
        /// </summary>
        public IReadOnlyDictionary<string, string> LocaleValues =>
          _localeFields
              .GroupBy(f => f.Label.ExtractCulture())
              .ToDictionary(g => g.Key, g => g.First().Value.Trim());

        public LocalizeDialog(string defaultName)
        {
            InitializeComponent();
            txtName.Text = defaultName;
            LoadSettings();

            this.PreviewKeyDown += LocalizeDialog_PreviewKeyDown;
        }

        private void LocalizeDialog_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // Закрыть как Cancel
                this.DialogResult = false;
                this.Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                // Сработать как OK
                Ok_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void LoadSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var mgr = new ShellSettingsManager(ServiceProvider.GlobalProvider);
            var store = mgr.GetWritableSettingsStore(SettingsScope.UserSettings);

            if (store.CollectionExists(CollectionPath))
            {
                if (store.PropertyExists(CollectionPath, PrefixKey))
                    txtPrefix.Text = store.GetString(CollectionPath, PrefixKey, "Loc");
                if (store.PropertyExists(CollectionPath, ResxPathKey))
                    txtResXPath.Text = store.GetString(CollectionPath, ResxPathKey, "Resources/Localization.resx");
            }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (!_isFirstLoad) return;
            _isFirstLoad = false;

            if (string.IsNullOrWhiteSpace(txtResXPath.Text))
            {
                MessageBox.Show("Пожалуйста, выберите файл ресурсов (.resx)", "Выбор файла ресурсов",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                BrowseResx_Click(this, null);
            }

            // Загружаем базовое значение из основного resx в txtValue  ← добавлено
            PopulateBaseValue();                                            

            PopulateLocaleFields();

            // Ставим фокус в txtValue и выделяем весь текст            ← добавлено
            txtValue.Focus();                                             
            txtValue.SelectAll();                                        
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            // При каждом активации окна обновляем поля локалей
            RefreshLocaleFields();
        }

        private void SaveSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var mgr = new ShellSettingsManager(ServiceProvider.GlobalProvider);
            var store = mgr.GetWritableSettingsStore(SettingsScope.UserSettings);

            if (!store.CollectionExists(CollectionPath))
                store.CreateCollection(CollectionPath);

            store.SetString(CollectionPath, PrefixKey, txtPrefix.Text.Trim());
            store.SetString(CollectionPath, ResxPathKey, txtResXPath.Text.Trim());
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResourceName = txtName.Text.Trim();
            ResourceValue = txtValue.Text.Trim();

            if (string.IsNullOrEmpty(ResourceName) || string.IsNullOrEmpty(ResourceValue))
            {
                MessageBox.Show("Пожалуйста, заполните базовое значение (Value*)", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(ResxPath))
            {
                MessageBox.Show($"Файл ресурсов не найден:\n{ResxPath}", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveSettings();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BrowseResx_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите файл ресурсов (.resx)",
                Filter = "ResX files (*.resx)|*.resx",
                InitialDirectory = GetProjectRoot(),
                FileName = txtResXPath.Text
            };
            if (dlg.ShowDialog() == true)
            {
                var projRoot = GetProjectRoot();
                string path = dlg.FileName;
                if (projRoot != null &&
                    path.StartsWith(projRoot, StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Substring(projRoot.Length)
                               .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                txtResXPath.Text = path;
                RefreshLocaleFields();
            }
        }

        private void RefreshLocaleFields()
        {
            // Очищаем модель и ItemsControl
            _localeFields.Clear();
            LocalesPanel.ItemsSource = null;

            // Заполняем заново
            PopulateLocaleFields();
        }

        private string GetProjectRoot() => Directory.GetCurrentDirectory();

        private void PopulateBaseValue()  // ← добавлен
        {
            string fullResx = Path.Combine(GetProjectRoot(), txtResXPath.Text);
            if (!File.Exists(fullResx)) return;
            var doc = XDocument.Load(fullResx);
            var data = doc.Root.Elements("data")
                         .FirstOrDefault(d => (string)d.Attribute("name") == txtName.Text);
            if (data != null)
                txtValue.Text = (string)data.Element("value");
        }                                 // ← добавлен

        private void PopulateLocaleFields()
        {
            string fullResx = Path.Combine(GetProjectRoot(), txtResXPath.Text);
            if (!File.Exists(fullResx)) return;

            string dir = Path.GetDirectoryName(fullResx);
            string baseName = Path.GetFileNameWithoutExtension(fullResx);

            foreach (var file in Directory.GetFiles(dir, $"{baseName}.*.resx"))
            {
                string culture = Path.GetFileNameWithoutExtension(file)
                                      .Substring(baseName.Length + 1);
                var field = new LocaleField
                {
                    Label = $"Value ({culture.ToUpper()}):",
                    Value = ""
                };

                // Загрузка существующего
                var doc = XDocument.Load(file);
                var data = doc.Root.Elements("data")
                             .FirstOrDefault(d => (string)d.Attribute("name") == txtName.Text);
                if (data != null)
                    field.Value = (string)data.Element("value");

                _localeFields.Add(field);
            }

            LocalesPanel.ItemsSource = _localeFields;
        }
    }

    internal static class Extensions
    {
        public static string ExtractCulture(this string label)
        {
            int start = label.IndexOf('(');
            int end = label.IndexOf(')');
            if (start >= 0 && end > start)
                return label.Substring(start + 1, end - start - 1).ToLowerInvariant();
            return string.Empty;
        }
    }
}
