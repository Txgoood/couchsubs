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

## Build #133 — Oyun kütüphanesi yönetimi

- PS5 Remote Play bağlantısı hangi oyunun çalıştığını doğrudan bildirmez. Ekran OCR, yalnız açık GTA5/GTA6 oyun başlığıyla eşleşirse otomatik profil açar. Ad bulunamadığında manuel seçilen kayıt korunur; oyunun adı tahmin edilmez.
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
