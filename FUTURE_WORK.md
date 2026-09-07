# Bilinçli Kapsam Dışı Bırakılanlar

Mimari için `ARCHITECTURE.md`, kararların gerekçeleri için `DECISIONS.md`.

Bu dosya tek bir soruyu cevaplıyor: **bu oyun üretime çıksaydı ne eklenirdi, ve her biri ne zaman
kendini ödemeye başlardı?** Her madde bir eşikle birlikte yazılı — çünkü asıl bilgi listenin kendisi
değil, o eşik.

---

## 0. Önce bir varsayım düzeltmesi

> **Senior olmak daha fazla pattern kullanmak değildir.**

Bu projede verdiğimiz kararların çoğu **zaten senior kararlarıydı** — ama ekleme yönünde değil, eleme yönünde:

| Elenen | Nerede |
|---|---|
| Structure of Arrays (SoA) | Karar 2 |
| Bit packing | Karar 2 |
| Incremental grup hesabı | Karar 3 |
| Global event bus | Karar 4 |
| GPU instancing / tek mesh | Karar 10 |
| Collider + raycast | Karar 11 |
| Granüler assembly bölünmesi | Karar 12 |
| `visitedStamp` optimizasyonu | Karar 3 |
| Knuth selection sampling (Algoritma S) | Karar 18 |
| Dengeli renk destesi | Karar 19 |
| Box sayısı için sayaç field'ı | Karar 20 |

Hepsi **"yapabilirdim ama gerekmiyor"** kararlarıydı ve bu refleks deneyimle gelir.

- **Junior kodun tipik işareti** gereksiz basitlik değil, **gereksiz karmaşıklıktır** — öğrenilen her kalıbı kullanma isteği.
- **Senior kodun işareti** az sayıda, gerekçesi olan yapıdır.

Aşağıdakiler gerçekten değişecek şeyler.

---

## 1. Üretim ölçeğinde gerçekten eklenecekler

Bunlar "best practice olduğu için" değil, **gerçek bir ihtiyaç doğduğu için** eklenirdi.

### Explicit state machine
**Ne:** `GameState` enum'ı ve açık geçişler (`Idle → Resolving → Animating → Deadlock → Win/Lose`).
**Neden gerekir:** Menü, seviye seçimi, tutorial, booster seçimi, seviye başı/sonu animasyonları, reklam araları eklendiğinde akış örtük metot çağrılarıyla takip edilemez hale gelir.
**Neden bizde yok:** Tek ekran var. `GameController`'ın örtük akışı yeterli.
**Eşik:** İkinci bir ekran (menü veya tutorial) eklendiği an.

### Command pattern (hamle kaydı)
**Ne:** Her hamle bir komut nesnesi olarak kaydedilir.
**Neden gerekir:**
- **Replay** — analitik ve destek talepleri
- **Undo** — F2P oyunlarda booster olarak satılır
- **Sunucu tarafı doğrulama** — hile önleme için hamleler sunucuda tekrar oynatılır

**Bizde durum:** Core zaten deterministik ve tohumlu → buna **hazır**. Sadece kaydetme altyapısı yok.
**Neden yok:** Case'in değerlendirdiği bir şey değil.
**Eşik:** Para geçen bir özellik (booster satışı) veya sunucu doğrulaması gerektiği an.

### Level content pipeline
**Ne:** Seviye editörü, JSON/ScriptableObject seviye dosyaları, seviye başına spawner tanımları.
**Bağlam:** Karar 6'da elle tasarımı (B) elerken bu bedeli **açıkça kabul ettik.**
**Neden yok:** Case parametrik üretim istiyor (M, N, K, A, B, C değişken; 2–10 arası her boyut çalışmalı). Elle tasarım bununla çelişir.
**Eşik:** Progression/seviye sistemi eklendiği an.

### Ağırlıklı spawner (yönlendirilmiş refill)
**Ne:** Üstten düşen blokların düzgün rastgele değil, tahtanın durumuna göre ağırlıklandırılmış çekilmesi.
Sektörde standart: spawner en az bir yasal hamlenin kalacağını garanti eder, ve aynı mekanizma zorluk
ayarının da temel aracıdır (oyuncuya ne sıklıkta büyük grup düşeceği buradan ayarlanır).
**Bizde durum:** `GravityResolver` hücre başına düzgün rastgele çekiyor. Deadlock'u *oluşmadan* önlemek
yerine oluştuktan sonra çözüyoruz (Karar 37) — bu case için doğru takas, çünkü case açıkça deadlock
**tespiti ve çözümü** istiyor; önleyici bir spawner o gereksinimi görünmez kılardı.
**Eşik:** Zorluk eğrisi ürün gereksinimi olduğu an. Ölçtüğümüz bir veri bunu şimdiden gösteriyor:
varsayılan 10×10'da 2000 oyunda shuffle **hiç** tetiklenmiyor, yani deadlock zaten pratikte oluşmuyor —
ağırlıklı spawner'ın orada çözeceği bir sorun yok, ayarlayacağı bir zorluk var.

### Otomatik seviye doğrulama (solver bot)
**Ne:** Bot'ların seviyeleri binlerce kez oynayıp çözülebilirliği doğrulaması.
**Bağlam:** Karar 8'de araştırdığımız sektör pratiği (bkz. arXiv 2409.06349).
**Bizde durum:** Üretim tarafında bunu **tek bir kısıta indirgedik** — "en üst satıra Box konmaz" —
ve doğrulama tarafında rastgele oynayan bir bot'u geçici olarak kullandık: Karar 36 ve 37'deki iki
hatanın ikisi de ölçümle bulundu, muhakemeyle değil. O bot repoda değil çünkü ölçtüğü özellikler artık
birer test.
**Eşik:** Elle tasarlanmış seviyeler + çeşitli engel tipleri geldiği an — o zaman bot geçici değil,
CI'da koşan kalıcı bir araç olur.

### Addressables ve platform bazlı asset yönetimi
**Ne:** ASTC/ETC2 sıkıştırma profilleri, bellek bütçeleri, lazy asset yükleme.
**Neden yok:** 26 sprite için anlamsız. Tek atlas zaten tek draw call veriyor.
**Eşik:** Yüzlerce asset / çoklu tema.

---

## 2. Süreç ve kalite altyapısı

Bunlar bir **takımın** ihtiyacı, tek kişilik bir case'in değil.

| Eksik | Neden gerekir | Neden bizde yok |
|---|---|---|
| **CI pipeline** (her PR'da build + test) | Regresyonları merge öncesi yakalar | Tek geliştirici, tek dal → değeri düşük |
| **Performans regresyon testleri** (Unity Performance Testing) | "Bu commit allocation getirdi mi" | Kurulumu case'in kendisinden uzun sürer |
| **Roslyn analyzer + `.editorconfig`** | Stil ve hata kurallarını derleyici seviyesinde zorlar | Takımda kritik, tek kişide gereksiz |
| **Analytics + crash SDK** | Ürün gereksinimi | Case gereksinimi değil |
| **Code coverage eşiği** | Test disiplini | 9 testimiz **bilinçli seçilmiş kritik davranışlar.** Yüzde hedefi, değerli testler yerine kolay testler yazmaya iter. |

---

## 3. Kod seviyesindeki farklar

### Uygulanabilir değil / kazancı düşük

**`#nullable enable`** — Null hataları derleme zamanında yakalanır. Unity'de hâlâ sürtünmeli (motor API'leri anotasyonlu değil) ve bizim Core'da referans tipi neredeyse yok. Kazanç düşük.

**`in` parametreleri / `readonly struct`** — `Cell` için uygulanabilir değil: küçük olduğu için kopyalama maliyeti önemsiz ve mutasyon gerektiği için `readonly` olamaz. *(Not: `BoardConfig` `readonly struct` — orada mutasyon yok ve amaç performans değil, değişmezlik.)*

### 💡 Ucuz ve değerli — eklenmeye değer

Bu üçü performans tarafıyla doğrudan örtüşüyor ve toplam maliyeti yarım gün.

#### 1. `ProfilerMarker` hot path'lerde
```csharp
static readonly ProfilerMarker s_Blast = new("Board.Blast");
static readonly ProfilerMarker s_Groups = new("Board.RecalculateGroups");
```
**Değeri:** Profiler'da `Board.Blast` ve `RecalculateGroups` isimlerinin görünmesi, performans iddiasını ölçüme bağlar.
**Maliyet:** ~yarım saat.

#### 2. Debug overlay (toggle'lanabilir)
Ekranda gösterilecekler: grup id'leri, grup boyutları, deadlock durumu, oturmuş/oturmamış blok durumu.
**Değeri:** Hem geliştirmeyi hızlandırır hem de video/screenshot ile "sistem gerçekten çalışıyor" göstermeni sağlar. Özellikle deadlock+shuffle'ı görsel olarak kanıtlamak için.
**Maliyet:** ~2 saat.

#### 3. `[Conditional]` invariant assert'leri — ✅ **kısmen uygulandı**
```csharp
[Conditional("UNITY_ASSERTIONS")]
static void AssertInBounds(int r, int c) { ... }
```
Faz 1'de `GroupFinder.AssertMatchesBoard` ile başladı: Karar 13/A2'nin bıraktığı tek açığı
(`GroupFinder` boyutları constructor'dan biliyor ama veriyi ayrı alıyor → ikisi ayrışabilir) kapatıyor.
Yani artık "eklenebilir bir cila" değil, bir mimari kararın tamamlayıcısı.

Kalan adaylar: satır/sütun aralıkta mı, grup boyutu toplamı hücre sayısıyla tutarlı mı, Box health 0–2 arasında mı.
**Değeri:** Release build'de **tamamen kaybolurlar** → sıfır maliyet. Geliştirmede erken hata yakalama.
**Maliyet:** ~1 saat.

#### 4. `LevelConfig` custom inspector + gizmo'lar
Tahta durumunu editörde gösteren gizmo'lar, config için okunabilir inspector.
**Maliyet:** ~1-2 saat. Opsiyonel, diğer üçünden sonra.

---

## 4. Oyunun kendisinde eksik olanlar

Bir senior **ürün** çıkarsaydı: booster'lar, özel bloklar (roket, bomba, disko topu), zincirleme kombo puanlaması, seviye ilerlemesi, kayıt/yükleme, bulut kaydı, lokalizasyon, ve "juice" (ekran sarsıntısı, blast'ta ölçek animasyonu, ses ducking, kamera zoom).

> ⚠️ **Bunların hiçbiri case'de istenmiyor ve eklemek aktif olarak ZARAR VERİR.**
> Değerlendiren kişi "istediklerimi yapmış mı" diye bakıyor. Roket blokları eklemek şu soruyu doğurur: *"İstenmeyen işe zaman harcamış — istenen şeye ne kadar harcamış acaba?"*

### Tek istisna: renk körlüğü / erişilebilirlik
Case zaten **her renge farklı ikon** vererek bunu kısmen çözmüş. README'de bunu fark ettiğini yazmak, tasarım okuma becerisini gösteren **bedava bir artı**. Kod eklemeye gerek yok, bir cümle yeter.

---

## 5. Neden bunları seçmedik — dört gerekçe

Bütün eleme kararlarımız şu dört sepetten birine düşüyor:

### Ölçek
100 hücre. Bir optimizasyonun kazancı **ölçülemiyorsa**, maliyeti (okunabilirlik, bug riski) net kayıptır.
→ SoA, bit packing, incremental hesap, GPU instancing buradan elendi.

### Kapsam
Bir problemin çevresindeki işe yatırım yapmak, problemin kendisinden zaman çalar.
→ Shuffle animasyonunda küçül-değiş-büyü'yü seçmemizin sebebi buydu: zor olan deadlock *algoritması*,
animasyonu değil.

### Okunabilirlik
Gereksiz bir event bus, kullanılmayan bir interface katmanı, tek implementasyonlu bir factory — bunlar
sonraki okuyucuya deneyim değil, **kalıp ezberi** olarak görünür. Az sayıda ve gerekçesi olan yapı,
çok sayıda ve "her ihtimale karşı" olan yapıdan her zaman daha kolay bakım alır.

### Bazıları gerçekten daha iyi değil
Karar 4'teki A/B tartışması: **event'ler bağımlılığı kaldırmaz, derleyicinin göremediği bir yere taşır.**
"Best practice" etiketi bir şeyi her bağlamda doğru yapmaz.

---

## 6. Hepsinin ortak kuralı

Yukarıdaki maddelerin hiçbiri "yapılmadı" diye yazılmadı; her biri **eşiğiyle** yazıldı. Aradaki fark
şudur:

> State machine yok — *ikinci bir ekran eklendiğinde ilk yapılacak şey o.*
>
> Level pipeline yok — *çünkü üretim parametrik. Elle tasarlanmış seviyeler istenseydi editör ve
> solver bot gerekirdi.*
>
> Command pattern yok — *ama Core deterministik ve tohumlu, yani sunucu doğrulaması gerektiğinde
> altyapı hazır.*

Bir kalıbı bilmek onu her yere koymayı gerektirmez. **Bir şeyi ne zaman yapmayacağını bilmek, nasıl
yapılacağını bilmekten daha zor öğrenilir.**

---

## Özet tablo

| Konu | Durum | Eşik / Gerekçe |
|---|---|---|
| Explicit state machine | Yok | İkinci ekran eklendiğinde |
| Command pattern | Yok (Core hazır) | Undo/replay/sunucu doğrulaması gerektiğinde |
| Level pipeline + editör | Yok | Elle tasarlanmış seviyeler gerektiğinde |
| Solver bot doğrulama | Tek üretim kısıtına indirgendi | Çeşitli engel tipleri geldiğinde |
| Addressables | Yok | Yüzlerce asset olduğunda |
| CI/CD | Yok | Takım çalışması başladığında |
| Perf regresyon testleri | Yok | Kurulum maliyeti > case değeri |
| Analyzer / editorconfig | Yok | Takımda |
| `#nullable enable` | Yok | Kazanç düşük (Core'da referans tipi yok) |
| **`ProfilerMarker`** | **Eklenebilir** | **Ucuz + case vurgusuyla örtüşüyor** |
| **Debug overlay** | **Eklenebilir** | **Deadlock/shuffle'ı görsel kanıtlar** |
| **`[Conditional]` assert** | **Başlandı (Faz 1)** | **Release'de sıfır maliyet; Karar 13/A2'nin açığını kapatıyor** |
| Booster / özel blok / juice | Yok | **Eklemek aktif zarar** — istenmeyen iş |
| Renk körlüğü notu | README'ye | Bedava artı, kod gerekmiyor |
