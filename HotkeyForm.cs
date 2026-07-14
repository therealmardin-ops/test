using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace OverlayCounter
{
    /// <summary>
    /// Görünmez (hiçbir zaman gösterilmeyen) ana form.
    /// Görevi:
    ///   1) F8 / F9 için Windows genelinde (global) kısayol kaydetmek,
    ///   2) Bu kısayollara basıldığında data.json dosyasını güvenli şekilde güncellemek,
    ///   3) Sistem tepsisinde (tray) küçük bir simge ve "Çıkış" menüsü göstermek.
    /// </summary>
    public class HotkeyForm : Form
    {
        // ---- Win32 API çağrıları (global kısayol için) ----

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Windows mesaj sabiti: bir global kısayola basıldığında bu mesaj gelir.
        private const int WM_HOTKEY = 0x0312;

        // Değiştirici tuş yok (Ctrl/Alt/Shift kullanmıyoruz, doğrudan F8/F9).
        private const uint MOD_NONE = 0x0000;

        // Sanal tuş kodları (Virtual-Key Codes)
        private const uint VK_F8 = 0x77;
        private const uint VK_F9 = 0x78;

        // Kayıtlı her kısayola verdiğimiz benzersiz kimlikler
        private const int HOTKEY_ID_INCREMENT = 1; // F8 -> current + 1
        private const int HOTKEY_ID_DECREMENT = 2; // F9 -> current - 1

        // ---- Uygulama durumu ----

        private readonly string _dataFilePath;
        private readonly string _logFilePath;
        private NotifyIcon? _trayIcon;
        private ContextMenuStrip? _trayMenu;

        // Aynı anda birden fazla dosya işlemi yapılmasını engellemek için basit bir kilit.
        private readonly object _fileLock = new();

        public HotkeyForm()
        {
            // data.json dosyasının EXE ile aynı klasörde olduğunu varsayıyoruz.
            // AppContext.BaseDirectory, çalışan EXE'nin bulunduğu klasörü verir.
            _dataFilePath = Path.Combine(AppContext.BaseDirectory, "data.json");
            _logFilePath = Path.Combine(AppContext.BaseDirectory, "log.txt");

            Log("=== Program başlatıldı ===");
            Log("EXE klasörü: " + AppContext.BaseDirectory);
            Log("data.json yolu: " + _dataFilePath);

            InitializeTrayIcon();

            // Formun boyutunu sıfırlayıp ekranın dışına koyuyoruz; ekstra güvence.
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
        }

        /// <summary>
        /// Formu asla görünür yapmayan yöntem.
        /// Böylece pencere tanıtıcısı (handle) oluşur ve mesajları alabiliriz,
        /// ama ekranda hiçbir pencere görünmez; uygulama tamamen arka planda çalışır.
        /// </summary>
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Form yüklendiğinde (pencere tanıtıcısı hazır olduğunda) kısayolları kaydet.
            RegisterHotkeys();

            // Başlangıçta data.json'un var olup olmadığını kontrol edip kullanıcıyı bilgilendir.
            var initialData = LoadData();
            if (initialData != null)
            {
                UpdateTrayTooltip(initialData);
                ShowBalloon("OverlayCounter çalışıyor", 
                    $"data.json bulundu.\nF8: +1  |  F9: -1\nMevcut: {initialData.current} / {initialData.total}");
            }
        }

        /// <summary>
        /// Sistem tepsisi simgesini ve sağ tık menüsünü hazırlar.
        /// </summary>
        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Çıkış", null, (s, e) => ExitApplication());

            _trayIcon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application, // Harici .ico dosyasına ihtiyaç yok
                Text = "OverlayCounter",
                Visible = true,
                ContextMenuStrip = _trayMenu
            };
        }

        /// <summary>
        /// F8 ve F9 tuşlarını Windows genelinde global kısayol olarak kaydeder.
        /// </summary>
        private void RegisterHotkeys()
        {
            bool ok1 = RegisterHotKey(this.Handle, HOTKEY_ID_INCREMENT, MOD_NONE, VK_F8);
            int err1 = Marshal.GetLastWin32Error();
            Log($"F8 kaydı: {(ok1 ? "BAŞARILI" : "BAŞARISIZ")} (Win32 hata kodu: {err1})");

            bool ok2 = RegisterHotKey(this.Handle, HOTKEY_ID_DECREMENT, MOD_NONE, VK_F9);
            int err2 = Marshal.GetLastWin32Error();
            Log($"F9 kaydı: {(ok2 ? "BAŞARILI" : "BAŞARISIZ")} (Win32 hata kodu: {err2})");

            if (!ok1 || !ok2)
            {
                // Kısayol başka bir uygulama tarafından zaten kullanılıyor olabilir.
                ShowBalloon("Uyarı",
                    "F8 veya F9 kısayolu kaydedilemedi.\nBaşka bir programla çakışıyor olabilir.");
            }
        }

        /// <summary>
        /// Windows'tan gelen mesajları dinler. WM_HOTKEY mesajı geldiğinde
        /// hangi kısayolun tetiklendiğini belirleyip ilgili işlemi çalıştırır.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                Log($"WM_HOTKEY alındı, id={hotkeyId}");

                try
                {
                    if (hotkeyId == HOTKEY_ID_INCREMENT)
                    {
                        Increment();
                    }
                    else if (hotkeyId == HOTKEY_ID_DECREMENT)
                    {
                        Decrement();
                    }
                }
                catch (Exception ex)
                {
                    // Hotkey işlenirken oluşabilecek her türlü hatayı burada da yakalıyoruz
                    // ki program asla çökmesin.
                    Log("HATA (WndProc): " + ex);
                    ShowBalloon("Hata", "İşlem sırasında hata oluştu:\n" + ex.Message);
                }
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// F8: current değerini 1 artırır ve dosyaya kaydeder.
        /// </summary>
        private void Increment()
        {
            Log("Increment() çağrıldı (F8)");
            lock (_fileLock)
            {
                var data = LoadData();
                if (data == null)
                {
                    Log("Increment: data null döndü, işlem iptal.");
                    return;
                }

                data.current += 1;
                Log($"Increment: yeni current = {data.current}");

                if (SaveData(data))
                {
                    UpdateTrayTooltip(data);
                }
            }
        }

        /// <summary>
        /// F9: current değerini 1 azaltır (0'ın altına inmez) ve dosyaya kaydeder.
        /// </summary>
        private void Decrement()
        {
            Log("Decrement() çağrıldı (F9)");
            lock (_fileLock)
            {
                var data = LoadData();
                if (data == null)
                {
                    Log("Decrement: data null döndü, işlem iptal.");
                    return;
                }

                // current asla negatif olamaz.
                data.current = Math.Max(0, data.current - 1);
                Log($"Decrement: yeni current = {data.current}");

                if (SaveData(data))
                {
                    UpdateTrayTooltip(data);
                }
            }
        }

        /// <summary>
        /// data.json dosyasını okur ve CounterData nesnesine dönüştürür.
        /// Herhangi bir hata durumunda kullanıcıya bildirim gösterir ve null döner
        /// (program asla bu yüzden çökmez).
        /// </summary>
        private CounterData? LoadData()
        {
            try
            {
                if (!File.Exists(_dataFilePath))
                {
                    Log("LoadData: data.json bulunamadı: " + _dataFilePath);
                    ShowBalloon("Hata", $"data.json bulunamadı:\n{_dataFilePath}");
                    return null;
                }

                string json = File.ReadAllText(_dataFilePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    ShowBalloon("Hata", "data.json dosyası boş.");
                    return null;
                }

                var data = JsonSerializer.Deserialize<CounterData>(json);

                if (data == null)
                {
                    ShowBalloon("Hata", "data.json ayrıştırılamadı (geçersiz JSON).");
                    return null;
                }

                return data;
            }
            catch (JsonException jsonEx)
            {
                ShowBalloon("Hata", "data.json geçerli bir JSON değil:\n" + jsonEx.Message);
                return null;
            }
            catch (IOException ioEx)
            {
                // Dosya başka bir program tarafından kilitlenmiş olabilir.
                ShowBalloon("Hata", "data.json okunamadı (dosya kilitli olabilir):\n" + ioEx.Message);
                return null;
            }
            catch (Exception ex)
            {
                ShowBalloon("Hata", "data.json okunurken beklenmeyen hata:\n" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// CounterData nesnesini data.json dosyasına güvenli (atomik) şekilde yazar.
        /// Önce geçici bir dosyaya yazılır, sonra asıl dosyanın üzerine taşınır.
        /// Bu sayede yazma sırasında bir kesinti olsa bile data.json bozulmaz;
        /// OBS overlay'i her zaman ya eski ya da yeni tam/geçerli veriyi okur.
        /// </summary>
        private bool SaveData(CounterData data)
        {
            string tempFilePath = _dataFilePath + ".tmp";

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, options);

                // 1) Geçici dosyaya yaz
                File.WriteAllText(tempFilePath, json);

                // 2) Geçici dosyayı asıl dosyanın üzerine atomik olarak taşı (overwrite: true)
                File.Move(tempFilePath, _dataFilePath, true);

                Log("SaveData: data.json başarıyla yazıldı.");
                return true;
            }
            catch (Exception ex)
            {
                Log("HATA (SaveData): " + ex);
                ShowBalloon("Hata", "data.json yazılırken hata oluştu:\n" + ex.Message);

                // Yarım kalan geçici dosyayı temizlemeye çalış.
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch
                {
                    // Temizlik başarısız olsa bile ana hata zaten kullanıcıya gösterildi.
                }

                return false;
            }
        }

        /// <summary>
        /// Sistem tepsisindeki simgenin üzerine gelindiğinde görünen ipucu (tooltip) metnini günceller.
        /// </summary>
        private void UpdateTrayTooltip(CounterData data)
        {
            if (_trayIcon == null) return;

            string text = $"OverlayCounter\nMevcut: {data.current} / {data.total}";

            // NotifyIcon.Text alanı en fazla 63 karaktere izin verir.
            if (text.Length > 63)
            {
                text = text.Substring(0, 63);
            }

            _trayIcon.Text = text;
        }

        /// <summary>
        /// Basit tanılama (debug) günlüğü: EXE ile aynı klasördeki log.txt dosyasına,
        /// zaman damgasıyla birlikte bir satır ekler. Hata olsa bile programı etkilemez.
        /// </summary>
        private void Log(string message)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line);
            }
            catch
            {
                // Loglama başarısız olsa bile programın çalışmaya devam etmesi önemli.
            }
        }

        /// <summary>
        /// Kullanıcıyı rahatsız etmeyen, kısa süreli bir bilgi balonu gösterir.
        /// (OBS ile canlı yayın yapılırken modal bir pencere açılmasın diye MessageBox yerine bu tercih edildi.)
        /// </summary>
        private void ShowBalloon(string title, string message)
        {
            if (_trayIcon == null) return;

            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = message;
            _trayIcon.ShowBalloonTip(3000);
        }

        /// <summary>
        /// Kısayolları geri alır, tepsi simgesini kaldırır ve uygulamayı sonlandırır.
        /// </summary>
        private void ExitApplication()
        {
            try
            {
                UnregisterHotKey(this.Handle, HOTKEY_ID_INCREMENT);
                UnregisterHotKey(this.Handle, HOTKEY_ID_DECREMENT);
            }
            catch
            {
                // Kapanış sırasında oluşabilecek hatalar göz ardı edilir.
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            Application.Exit();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Form her ne şekilde kapanırsa kapansın kısayolların ve tray simgesinin
            // temizlendiğinden emin ol.
            try
            {
                UnregisterHotKey(this.Handle, HOTKEY_ID_INCREMENT);
                UnregisterHotKey(this.Handle, HOTKEY_ID_DECREMENT);
            }
            catch
            {
                // yoksay
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            base.OnFormClosed(e);
        }
    }
}
