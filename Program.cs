using System;
using System.Windows.Forms;

namespace OverlayCounter
{
    /// <summary>
    /// Uygulamanın giriş noktası.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Beklenmeyen (yakalanmamış) hatalarda program çökmesin,
            // bunun yerine kullanıcıya bir mesaj gösterilsin.
            Application.ThreadException += (sender, e) =>
            {
                MessageBox.Show(
                    "Beklenmeyen bir hata oluştu:\n" + e.Exception.Message,
                    "OverlayCounter - Hata",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show(
                    "Beklenmeyen bir hata oluştu:\n" + (ex?.Message ?? "Bilinmeyen hata"),
                    "OverlayCounter - Hata",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            // Windows Forms genel ayarları (elle ayarlanıyor, ekstra kod üretimi gerektirmez)
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Ana form: hiçbir zaman görünür olmayan, sadece global kısayolları
            // ve sistem tepsisi (tray) simgesini yöneten form.
            Application.Run(new HotkeyForm());
        }
    }
}
