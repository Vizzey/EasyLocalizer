using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace LocalizeExtension
{
    public partial class LocalizeDialog : Window
    {
        public string ResourceName { get; private set; }
        public string ResourceValue { get; private set; }

        public LocalizeDialog(string defaultName)
        {
            InitializeComponent();

            txtName.Text = defaultName;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResourceName = txtName.Text.Trim();
            ResourceValue = txtValue.Text.Trim();

           
            if (string.IsNullOrEmpty(ResourceName) || string.IsNullOrEmpty(ResourceValue))
            {
                MessageBox.Show("Пожалуйста, заполните оба поля", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
