﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using Microsoft.Win32;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using MessageBox = System.Windows.MessageBox;

namespace LocalizeExtension
{
    public partial class LocalizeDialog : Window
    {
        private readonly string _projectRoot;
        private const string CollectionPath = "LocalizeExtension\\Options";
        private const string PrefixKey = "KeyPrefix";
        private const string ResxPathKey = "ResxPath";
        private const string UseAtSignKey = "UseAtSign";

        public string ResourceName { get; private set; }
        public string ResourceValue { get; private set; }
        public string KeyPrefix => txtPrefix.Text.Trim();
        public string ResxPath => txtResXPath.Text.Trim();

        // Поле для запоминания выбранной опции (по умолчанию true – то есть, вариант с '@')
        private bool _useAtSign = true;

        private bool _isFirstLoad = true;
        private readonly string _defaultValue;

        private class LocaleField
        {
            public string Label { get; set; }
            public string Value { get; set; }
        }

        private readonly List<LocaleField> _localeFields = new();

        public IReadOnlyDictionary<string, string> LocaleValues =>
            _localeFields
                .GroupBy(f => f.Label.ExtractCulture())
                .ToDictionary(g => g.Key, g => g.First().Value.Trim());

        // Если выбран пункт с собакой (первый пункт в ComboBox)
        public bool UseAtSign => cmbSelectOption.SelectedIndex == 0;

        public LocalizeDialog(string defaultName, string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            txtName.Text = defaultName;
            _defaultValue = defaultName;
            LoadSettings();
            PreviewKeyDown += LocalizeDialog_PreviewKeyDown;
        }

        private void txtPrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (cmbSelectOption == null)
                return;

            string prefix = txtPrefix.Text.Trim();
            if (string.IsNullOrWhiteSpace(prefix))
                prefix = "Loc";

            cmbSelectOption.Items.Clear();
            cmbSelectOption.Items.Add("@" + prefix + "[...]");
            cmbSelectOption.Items.Add(prefix + "[...]");

            // Устанавливаем выбранную опцию согласно сохранённому значению
            cmbSelectOption.SelectedIndex = _useAtSign ? 0 : 1;
        }

        private void LocalizeDialog_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
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
                if (store.PropertyExists(CollectionPath, UseAtSignKey))
                    _useAtSign = store.GetBoolean(CollectionPath, UseAtSignKey, true);
            }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (!_isFirstLoad)
                return;
            _isFirstLoad = false;

            if (string.IsNullOrWhiteSpace(txtResXPath.Text))
            {
                MessageBox.Show("Please select a resource file (.resx)", "Resource File Selection",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                BrowseResx_Click(this, null);
            }

            txtPrefix_TextChanged(null, null);
            PopulateBaseValue();
            PopulateLocaleFields();

            if (string.IsNullOrEmpty(txtValue.Text))
                txtValue.Text = _defaultValue;

            txtValue.Focus();
            txtValue.SelectAll();
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            // Не очищаем значения при активации окна
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
            // Сохраняем выбранную опцию (использование @)
            store.SetBoolean(CollectionPath, UseAtSignKey, cmbSelectOption.SelectedIndex == 0);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResourceName = txtName.Text.Trim();
            ResourceValue = txtValue.Text.Trim();
            if (string.IsNullOrEmpty(ResourceName) || string.IsNullOrEmpty(ResourceValue))
            {
                MessageBox.Show("Please fill in the base value (Value*)", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!File.Exists(ResxPath))
            {
                MessageBox.Show($"Resource file not found:\n{ResxPath}", "Error",
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
                Title = "Select a resource file (.resx)",
                Filter = "ResX files (*.resx)|*.resx",
                InitialDirectory = _projectRoot,
                FileName = txtResXPath.Text
            };
            if (dlg.ShowDialog() == true)
            {
                var root = _projectRoot;
                var path = dlg.FileName;
                if (root != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Substring(root.Length)
                               .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                txtResXPath.Text = path;
                RefreshLocaleFields();
            }
        }

        private void RefreshLocaleFields()
        {
            var savedValues = _localeFields.ToDictionary(f => f.Label, f => f.Value);
            _localeFields.Clear();
            PopulateLocaleFields();
            foreach (var field in _localeFields)
            {
                if (savedValues.TryGetValue(field.Label, out var value))
                    field.Value = value;
            }
            LocalesPanel.ItemsSource = null;
            LocalesPanel.ItemsSource = _localeFields;
        }

        private void PopulateBaseValue()
        {
            var fullResx = Path.Combine(_projectRoot, txtResXPath.Text);
            if (!File.Exists(fullResx))
                return;
            var doc = XDocument.Load(fullResx);
            var data = doc.Root.Elements("data")
                         .FirstOrDefault(d => (string)d.Attribute("name") == txtName.Text);
            if (data != null)
                txtValue.Text = (string)data.Element("value");
        }

        private void PopulateLocaleFields()
        {
            var fullResx = Path.Combine(_projectRoot, txtResXPath.Text);
            if (!File.Exists(fullResx))
                return;
            var dir = Path.GetDirectoryName(fullResx);
            var baseName = Path.GetFileNameWithoutExtension(fullResx);
            foreach (var file in Directory.GetFiles(dir, $"{baseName}.*.resx"))
            {
                var culture = Path.GetFileNameWithoutExtension(file).Substring(baseName.Length + 1);
                var field = new LocaleField
                {
                    Label = $"Value ({culture.ToUpper()}):",
                    Value = ""
                };
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
            var start = label.IndexOf('(');
            var end = label.IndexOf(')');
            if (start >= 0 && end > start)
                return label.Substring(start + 1, end - start - 1).ToLowerInvariant();
            return string.Empty;
        }
    }
}
