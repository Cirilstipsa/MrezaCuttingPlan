using System.Windows;

namespace MrezaCuttingPlan.UI
{
    public partial class SettingsDialog : Window
    {
        public double SheetWidth { get; private set; } = 215;
        public double SheetLength { get; private set; } = 600;
        public string PdfOutputPath { get; private set; } = string.Empty;
        public bool Confirmed { get; private set; } = false;

        public SettingsDialog()
        {
            InitializeComponent();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(TxtSheetWidth.Text.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double w) || w <= 0)
            {
                MessageBox.Show("Unesite valjanu širinu lima (pozitivan broj).",
                    "Greška unosa", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtSheetWidth.Focus();
                return;
            }

            if (!double.TryParse(TxtSheetLength.Text.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double l) || l <= 0)
            {
                MessageBox.Show("Unesite valjanu dužinu lima (pozitivan broj).",
                    "Greška unosa", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtSheetLength.Focus();
                return;
            }

            SheetWidth = w;
            SheetLength = l;
            PdfOutputPath = TxtPdfPath.Text.Trim();
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF datoteke (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = "Plan_Rezanja_Mreza.pdf"
            };
            if (dlg.ShowDialog() == true)
                TxtPdfPath.Text = dlg.FileName;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
