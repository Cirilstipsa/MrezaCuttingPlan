using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace MrezaCuttingPlan.UI
{
    public partial class SettingsDialog : Window
    {
        public double SheetWidth { get; private set; } = 215;
        public double SheetLength { get; private set; } = 600;
        public string? PdfOutputPath { get; private set; }
        public bool Confirmed { get; private set; } = false;

        public SettingsDialog()
        {
            InitializeComponent();

            // Postavi defaultnu putanju za PDF
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            TxtPdfPath.Text = Path.Combine(desktop,
                $"PlanRezanja_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Spremi PDF plan rezanja",
                Filter = "PDF datoteke (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = Path.GetFileName(TxtPdfPath.Text),
                InitialDirectory = Path.GetDirectoryName(TxtPdfPath.Text)
                                   ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dlg.ShowDialog() == true)
                TxtPdfPath.Text = dlg.FileName;
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

            if (string.IsNullOrWhiteSpace(TxtPdfPath.Text))
            {
                MessageBox.Show("Odaberite putanju za PDF izvještaj.",
                    "Greška unosa", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SheetWidth = w;
            SheetLength = l;
            PdfOutputPath = TxtPdfPath.Text;
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
