namespace OverlayCounter
{
    /// <summary>
    /// data.json dosyasının karşılık geldiği veri modeli.
    /// OBS overlay'inin okuduğu alanlarla birebir aynı isimlere sahiptir.
    /// </summary>
    public class CounterData
    {
        /// <summary>Şu anki başarı sayısı (F8 ile artar, F9 ile azalır, 0'ın altına inmez).</summary>
        public int current { get; set; }

        /// <summary>Toplam hedef sayısı. Bu program tarafından ASLA değiştirilmez.</summary>
        public int total { get; set; }
    }
}
