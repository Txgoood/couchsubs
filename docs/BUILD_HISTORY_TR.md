# CouchSubs GTA Türkçe Dublaj — Sürüm ve Build geçmişi

Bu dosya **yalnız Txgoood/couchsubs** deposunun kullanıcıya teslim ettiği eşleştirilmiş Windows + LG webOS ikilisini takip eder. Orijinal upstream ve XIPTV projeleriyle ilgisi yoktur.

| Çift Build | Windows EXE sürümü | LG webOS IPK sürümü | İçerik | Durum |
| --- | --- | --- | --- | --- |
| #130 | v0.1.6 | v0.9.8 | Silero ONNX VAD, duygu hafızası, doğal ses koruma | Başarılı GitHub Run #186 |
| #131 | v0.1.7 | v0.9.9 | TV dublaj ses oynatma tanılaması, eski sesin temizlenmesi, webOS panelinde eşleşen Build no, IPK'nin bağımsız artifact olarak sunulması | GitHub Actions testi gerekli |

| **#132** | **v0.1.8** | **v0.9.10** | **Deneysel Sherpa ONNX konuşmacı ses izi + Silero konuşma algılama; isimli altyazı öncelikli, ses kimliği ikinci** | **CI ve gerçek cihaz doğrulaması bekleniyor** |

## Build #132 – dinamik GTA6 konuşmacı algılama

- Altyazıda karakter adı varsa bu isim kesinlikle önceliklidir.
- Ad yoksa sadece Silero'nun doğruladığı PS5 konuşmasından ONNX ses izi çıkarılır. Aynı konuşmacı sesiyle güvenli eşleşme bulunursa geçici `Ses 1` / `Ses 2` kimliği atanır.
- Ses modeli çalışmazsa, konuşmalar çakışırsa veya eşleşme belirsizse mevcut Türkçe dublaj ve çeviri devam eder; karakter ismi uydurulmaz.
- İlk ses kimliği için en az iki ayrı, yeterli uzunlukta gözlem gerekir. Kısa konuşmalar ve karışık oyun sesleri yanlış eşleşmeye yol açabilir.
- Ses izi kümeleri oturum içindedir; adlandırma/ses kimliği henüz oyunlar arasında biyometrik şablon olarak kalıcılaştırılmıyor.
- Windows: **v0.1.8, Build #132**; webOS: **v0.9.10, Build #132**. TV yalnız eş sürüme güncellenir; model bilgisayarda çalışır.

| **#133** | **v0.1.9** | **v0.9.11** | **Oyun başlığından otomatik GTA5/GTA6 profili, JSON dışarı/içe aktarma ve ayrı silme işlemleri** | **CI doğrulaması bekleniyor** |

| **#134** | **v0.2.0** | **v0.9.12** | **Yapay zekâ çalışma süresi ölçümü, otomatik Silero/metin tabanlı güvenli seçim, eski motorlara dönüş** | **GitHub CI kontrolünde** |

| **#135** | **v0.2.1** | **v0.9.13** | **F11 tam ekran çevirisi, F12 serbest alan; ayardan değiştirebilen kalıcı iki kısayol** | **CI testinde** |

| **#136** | **v0.2.2** | **v0.9.14** | **Num0/1/2/3/8, F11 ve F12 dahil 7 kısayolun tamamı tek panelden değiştirilebilir; çakışma kontrolü** | **GitHub CI doğrulaması bekleniyor** |

## Build #136 — Tam kısayol özelleştirme

- Windows ayarlarında **Klavye kısayolları - hepsini değiştir** panelinde 7 işlevin ayrı açılır listesi bulunur.
- F1..F12 ve NumPad0..9 kullanılabilir; bir tuş iki farklı işleve atanamaz.
- Varsayılanlar: F11 ekran çevir, F12 serbest alan, Num0 durdur/devam, Num1/2/3 ek bölge, Num8 önizleme.
- Değişiklikler kalıcı UserSettings içine yazılır, çeviri sürerken Windows hotkey kaydı yeniden yapılır.
- Eski Build #135 F11/F12 tercihleri korunur. Diğer eski kısayolların varsayılanı değişmez.
- **Windows v0.2.2 / Build #136**, **LG webOS v0.9.14 / Build #136**. TV'nin klavye ayarları değiştirilmez.
- Silero, Sherpa, GTA karakter hafızası, dışarı/içe aktar/sil, dublaj ve oyun atmosferi ile PS5 bağlantısı korunur.

## Build #135 — Kısayolları kayıpsız güncelle

- Ekranı çevir: **F11** (eski Num9). Tekrar basınca çeviriyi kaldırır.
- Serbest alan: **F12** (eski Num5). Esc ile kapanır.
- Windows Ayarlar'da iki ayrı ComboBox ile **F1..F12** veya **Num5/Num9** atanabilir. Diğer Num 0,1,2,3,8 kısayolları ayrılmıştır, değiştirilmez.
- Seçimler kalıcı `settings.json` içine yazılır; canlı çeviri açıksa Windows kısayolları yeniden kaydedilir. Aynı tuşu iki işleve atamak yasaktır.
- Windows v0.2.1 Build #135; LG webOS eş paket v0.9.13 Build #135. TV'nin kısayol girişi yoktur; değişiklik yalnız bilgisayara uygulanır.
- Önceki Silero, Sherpa, Türkçe dublaj, karakter hafızası ve oyun çeviri özelliklerine dokunulmadı.

## Build #134 — Kayıpsız yapay zekâ altyapısı

- **Önceki tüm özellikler korunur:** GTA oyun adı, JSON yedekleme, karakter sesleri, Sherpa ses izi, Silero VAD, oyun atmosferinin korunması, webOS ses çıktısı.
- **Yeni Windows sürümü:** v0.2.0 Build #134. **webOS:** v0.9.12 Build #134; paket kimliği değişmez.
- **Seçilebilir konuşma algılama:** Otomatik (mevcut yerel Silero), açık Silero ONNX, yapay zekâ kapalı (metin tabanlı).
- **Performans ölçümü:** Silero ve Sherpa işlem sayısı, ortalama/en yüksek çıkarım gecikmesi ve hata sayısı. Ham ses veya replik tutulmaz.
- **Varsayılan:** önceki kurulumların davranışını bozmadan Otomatik/Silero.
- **TextOnly:** yalnız neural konuşma analizi kapatılır; OCR, Marian/DeepL çeviri, mevcut ses motoru, oyun atmosferi ve çevrimdışı modeller silinmez.
- **TEN VAD, FireRedVAD, yeni ASR/TTS:** henüz pakete bağlı değil; kullanıcıya çalışıyor gibi gösterilmez.
- **Testler:** varsayılanların korunması, çok iş parçacıklı sayaçlar, metin gizliliği ve mevcut temel test paketi.

## Build #133 — Oyun kütüphanesi yönetimi

- PS5 yerel ağ DDP sorgusunda `running-app-name` alanı varsa iki ardışık tutarlı yanıtla oyun profili oluşturulur ve seçilir. Yanıt yoksa ekran OCR yalnız tam GTA5/GTA6 başlık metnini yakalarsa profil seçer. Kullanıcı o oturumda elle oyun seçmişse bu tercih korunur.
- Oyun seçici menüsünde **Dışarı aktar** karakter/ses eşleşmelerini JSON'a yazar.
- **İçe aktar** JSON yedeğini doğrular; aynı oyun varsa kullanıcı açıkça onay vermeden üzerine yazılmaz.
- **Oyunu sil** işlemi iki aşamalı onayla yapılır; yalnız seçilen oyunun eşleşmelerini siler. Ortak model ve diğer oyunlar korunur.
- Bu JSON dosyası ses modeli, PCM veya oturumluk konuşmacı embeddinglerini içermez. İleride ses modelini de yedeklemek için bağımsız onaylı bir özellik gerekir.
- Windows **v0.1.9 / Build #133**, webOS **v0.9.11 / Build #133**.

## Build #131 paketleri

- Windows: Actions `couchsubs-windows-installer` içindeki `CouchSubs-windows-Setup.exe`.
- LG webOS: Actions `couchsubs-webos-v0.9.9-build131-ipk` içindeki `com.couchsubs.tv_0.9.9_all.ipk`.
- Windows uygulama menüsünde `v0.1.7 · Build #131`, LG webOS panelinde `v0.9.9 · Build #131` görünür.
- Android TV APK veya Samsung Tizen sürümleri **bu çift sürüme dâhil değildir**; farklı ürün ve paket sürümleri korunur.

## Güvenli kullanım ve sınırlar

- Windows, Türkçe dublaj, OCR, Silero VAD ve karakter seslerini yönetir; LG webOS yardımcı uygulaması dublaj üretmez, yalnız gönderilen sesi çalar.
- webOS oynatıcısı gelen Türkçe WAV'ı ayrı HTML Audio öğesinde çalar; oyun/HDMI ses seviyesine müdahale etmez.
- AI konuşma tespiti, kaynak konuşma ayırma/susturma veya tam duygusal ses klonlamaya eşit değildir.
- GitHub CI yeşil olması PS5 + LG webOS 2.0 gerçek cihaz testinin yapıldığı anlamına gelmez.
