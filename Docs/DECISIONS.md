# Karar Günlüğü — Good Job Games Blast Case

Bu dosya projedeki her tasarım kararının **alternatiflerini, trade-off'larını ve eleme gerekçelerini** kayıt altına alır. Amaç, ileride "bunu neden böyle yaptık" sorusuna kaynak olmak ve mülakatta kararları savunabilmek.

**Proje:** Collapse/Blast mekaniği, Unity, junior game developer case
**Odak:** Performans (bellek, CPU, GPU) + gereğinden fazla karmaşıklaştırmamak

---

## 0. Temel bağlam notu — ölçek gerçeği

Tahta en fazla **10×10 = 100 hücre**. Bu ölçekte optimizasyonların çoğu ölçülebilir fark yaratmaz.

Case "performans odaklıyız" dediği için performans kararları veriyoruz, ama **gerçek amaç hız kazanmak değil, doğru mühendislik refleksini göstermek.** Bu ikisini ayırabilmek önemli.

Mülakatta güçlü olan cevap "hızlı olsun diye yaptım" değil, **"bu ölçekte gerekmiyor ama şu ölçekte gerekirdi, maliyeti de sıfırdı"**. README bu ayrımı açıkça yapmalı.

Bu ilkeden türeyen bir kural: **aynı sonuca daha pahalı yoldan gitmek optimizasyon değildir.** Birçok seçeneği bu kuralla eledik.

---

## Karar 1 — Mimari: mantık/görsel ayrımı

### Seçenekler

| | Yaklaşım | Artı | Eksi |
|---|---|---|---|
| A | Her blok kendini yönetir (GameObject merkezli) | En hızlı yazılır, sezgisel, sahnede debug kolay | Tahtanın gerçeği sahnede; düşme sırasında tutarlı durum yok. Test edilemez. 100 ayrı `Update` (managed↔native geçişi). |
| B | Merkezi `BoardManager` + aptal bloklar | A'nın çoğu sorununu çözer, tek `Update`, sektörde en yaygın | Mantık hâlâ `UnityEngine`'e bağımlı, unit test zor, bellekte dağınık objeler |
| C | Saf C# Core + ince View katmanı | Unit test edilebilir, mantık anlık çözülür animasyon geriden gelir, sıkı veri paketleme, anlatılacak net hikâye | Daha fazla dosya, index'lerle çalışma, **aşırı soyutlama riski** |

### Eleme gerekçeleri

**A elendi.** Case'in en zor maddesiyle doğrudan çatışıyor: *"Players should be able to blast stationary blocks at all times while other blocks are falling."* Animasyon = tahtanın gerçeği ise, animasyon sürerken tahtanın tutarlı bir durumu yok demektir. Bu maddeyi A ile çözmek mümkün ama sürekli özel durum yamalarıyla.

**B elenmedi, savunulabilir.** Gerçek bir stüdyoda çoğu takım B ile gider ve haklıdır. Tek eleme sebebi: test edilebilirlik ve bunun case'de yarattığı ayırt edici fark.

### Seçilen: C — sıkı sadelik disipliniyle

Somut sınırlar (bunlar aşılmamalı):
- **Event bus yok, DI container yok, command pattern yok**
- Interface sadece gerçekten ikinci bir implementasyon olacaksa yazılır
- Yeni bir Core tipi ancak Karar 17'deki üç şartı birden geçerse açılır

> **Not (Karar 17):** Burada başlangıçta "Core en fazla 4-6 dosya" yazıyordu. Dosya sayısı,
> asıl kaygının (soyutlama şişkinliği) kaba bir vekiliydi ve yanlış şeyi ölçüyordu — `Grid` gibi
> bir ayrım karmaşıklığı azaltıyor, artırmıyor. Sayı yerine gerçek ölçüt Karar 17'de tanımlandı.

Amaç: C'nin faydasını (test + net veri akışı) alıp maliyetini (soyutlama şişkinliği) ödememek.

---

## Karar 2 — Veri modeli

### Seçenekler

| | Yaklaşım | Artı | Eksi |
|---|---|---|---|
| A | `Block[,]`, Block = class | En okunaklı, `null` = boş bedava sentinel, referans semantiği rahat | 100 heap objesi, pointer zıplaması, GC baskısı. `[,]` C#'ta ayrı CLR tipi, JIT bounds-check'i 1D kadar iyi optimize edemiyor |
| B | `Cell[]`, Cell = struct, 1D | ~400 byte tek bellek bloğunda, tamamı L1'e sığar, sıfır GC, C#'ın en iyi optimize ettiği erişim yolu, `Array.Copy` ile ucuz snapshot | `Index(r,c)` katmanı, **struct kopya tuzağı** |
| C | Paralel diziler (SoA) | Sadece renge bakan döngüler cache'e gereksiz veri çekmez, vectorization dostu | 3 diziyi elle senkron tutma, birini unutmak sessiz bug, okunabilirlik düşer |
| D | Bit packing (hücre = 1 byte) | En küçük bellek (100 byte) | Her okuma/yazmada mask+shift, debugger'da `cells[47] = 137` görürsün |

### Eleme gerekçeleri

**D elendi.** 300 byte kazanç için okunabilirlik ve debug edilebilirlik feda edilmez. "Performansı önemsemek" ile "performansı taklit etmek" arasındaki fark tam burada. Milyonlarca hücre olsaydı konuşulurdu.

**C elendi.** SoA'nın kazancı veri L1'e sığmadığında doğar. Bizim veri ~400 byte, tipik L1 ~32 KB. Kazanç **ölçülemez**, maliyet **gerçek**. README'de "SoA değerlendirildi, bu ölçekte gerekçesi yok" yazmak, SoA'yı gereksiz yazmaktan güçlü sinyal.

**A elenmedi, gerçekten savunulabilir.** B'ye geçişin maliyeti neredeyse sıfır olduğu ve GC'siz çekirdek case'in performans vurgusuna doğrudan cevap verdiği için B tercih edildi.

### Seçilen: B

```csharp
public enum CellType : byte { Empty = 0, Color = 1, Box = 2 }

public struct Cell {
    public CellType Type;
    public byte Color;    // sadece Type == Color iken anlamlı
    public byte Health;   // sadece Type == Box iken anlamlı
}

int Index(int r, int c) => r * Cols + c;   // AggressiveInlining
```

Detaylar:
- `enum : byte` — C#'ta enum varsayılanı `int`, boşuna 4 byte
- `Health` ayrı yapıya taşınmadı: 1 byte israf < ayrı dictionary karmaşıklığı
- **Struct kopya tuzağı** kodda yorumla belirtilecek:
  ```csharp
  var c = cells[i]; c.Color = 2;   // YANLIŞ — kopya üzerinde çalışır, diziye yazmaz
  cells[i].Color = 2;              // DOĞRU
  ```

### Alt karar 2a — Satır yönü: **satır 0 = ALT**

Sektörde tek konvansiyon yok, iki ihtiyaç çekişiyor:
- **Üstten aşağı** (satır 0 = üst): level dosyaları, Tiled, tilemap serileştirme. Bir level'ı metin olarak yazınca dosyayı okuduğun sırayla tahtayı görürsün — level designer için doğal.
- **Alttan yukarı** (satır 0 = alt): Unity runtime, çünkü world space'te Y yukarı artar. `worldY = origin.y + row * cellSize`, eksi işareti yok, ters çevirme yok. Unity'nin kendi Tilemap'i de sol-alt orijinli.

**Sektörün pratiği:** runtime'da motorla uyumlu olanı, dosyada insanla uyumlu olanı seçip **çevirmeyi tek bir fonksiyona hapsetmek.** Bug'lar hangi yönü seçtiğinden değil, çevirmenin birden fazla yerde yapılmasından çıkar.

**Seçim: satır 0 = alt.** Gravity "index azalan yöne düşer", yeni bloklar en yüksek index'ten girer. `Board.cs` başına ve README'ye açıkça yazılacak.

### Alt karar 2b — Boş hücre: ayrı `CellType` alanı (sentinel değil)

Sentinel (`Color = 255` gibi) elendi:
1. **Geçersiz durum geçerli veriyle aynı alanda yaşar.** `Color`'ı her okuduğunda "bu gerçekten renk mi?" kontrolü gerekir; tip sistemi yardım edemez, unutulan yerde sessizce yanlış çalışır.
2. **Asıl kırılma noktası: Box'ın rengi yok.** Sentinel modelinde tek `byte`'ta üç anlam taşımak gerekirdi (renk / boş / kutu) ve okuyan herkesin bu sözleşmeyi ezberlemesi gerekirdi.

Maliyet 100 byte, karşılığında bir sınıf hata imkânsız hale geliyor.

---

## Karar 3 — Grup hesabı: ne zaman, ne kadar?

### Seçenekler
- **A:** Sadece tıklanınca, tıklanan hücreden flood fill
- **B:** Her değişiklikten sonra **tam tarama**, sonucu cache'le (`groupIdOf`, `groupSizes`)
- **C:** Kirli bölge takibi (incremental)

### Eleme gerekçeleri

**C elendi.** "Hangi bölge etkilendi" sorusunun cevabı sanılandan geniş: bir sütun düştüğünde yan sütunlardaki gruplar birleşebilir veya bölünebilir, zincirleme yayılır. 100 hücrelik tam tarama mikrosaniyeler sürerken, ölçülemez kazanç için gerçek bir bug riski satın alınmaz. README notu: "değerlendirildi; 1000×1000 ölçekte düşünülürdü".

**A tek başına elendi — sebebi performans DEĞİL, oyun kuralı.** Case her bloğun ikonunun ait olduğu grubun boyutuna göre değişmesini istiyor. Yani **tıklama olmasa bile** tahtadaki tüm grup boyutları her an bilinmek zorunda. A bunu karşılayamaz.

> **Ders:** Mimariyi burada performans değil, gözden kaçması kolay bir gereksinim belirledi.

### Seçilen: B

Tek `RecalculateGroups()` üç işi birden görüyor:
1. Patlatılabilirlik
2. İkon seviyeleri (A/B/C eşikleri)
3. **Deadlock tespiti bedavaya geliyor** — en büyük grup < 2 ise deadlock. Ekstra tarama maliyeti sıfır.

### Uygulama kuralları
- Flood fill **iterative + DFS**, kendi `int[] stack` + `top` index'imizle
- `groupIdOf`, `groupSizes`, `stack` dizileri **sınıf field'ı, bir kez alloc** — her taramada yeniden ayrılmıyor
- **LINQ yok** (`Where`/`Select` her çağrıda enumerator allocation → GC spike)
- `groupIdOf` sıfırlaması: **`-1` ile doldurulur, `Array.Clear` ile değil.** `Array.Clear` sıfır yazar, ama **0 geçerli bir grup id'si** — "grup yok" ile "grup 0" aynı değere düşerse `GroupSizeAt` sessizce yanlış cevap verir. Alternatif (id'leri 1'den başlatıp 0'ı sentinel yapmak) `memset` kazandırırdı ama `+1/-1` bookkeeping'ini her erişime yayardı; Karar 2b'deki sentinel gerekçesinin aynısı.

### Alt konu — BFS vs DFS
Flood fill hangi sırayla gezerse gezsin **bağlı bileşenin tamamını gezmek zorunda.** Erken çıkış yok, çünkü ikon seviyeleri için tam boyut lazım. İkisi de O(grup boyutu).

Karar hıza değil **sadeliğe** düştü:
- DFS'in kabı `int[] stack` + `top`, üç satır. BFS'te `Queue<T>` allocation getirir, elle yazarsan head/tail'li ring buffer gerekir.
- Cache davranışı marjinal olarak DFS lehine: grid'de DFS bir yönde uzun koşular yapar, BFS elmas gibi yayılıp sürekli `±Cols` uzaklığa atlar.

### Alt konu — `visitedStamp` tekniği (bilinçli olarak KULLANILMIYOR)
Her taramada `Array.Clear` yerine artan bir sayaç kullanılabilir:
```csharp
int[] visitedStamp;  int currentStamp = 0;
currentStamp++;                                  // her tarama yeni damga
if (visitedStamp[i] == currentStamp) continue;   // bu taramada görüldü
visitedStamp[i] = currentStamp;
```
Eski değerlerin hepsi `currentStamp`'ten küçük olduğu için otomatik "bayat" olurlar, temizlik gerekmez.

**Not:** Tek/çift ile dönüşümlü damga **çalışmaz** — iki değer, "bu taramada" ile "iki tarama önce"yi ayırt edemez.

**Bu ölçekte yazmaya değmez** (100 elemanlık `Array.Clear` bir `memset`). README'de not düşülecek. *Ancak bu teknik Box hasarında gerçekten kullanılıyor — aşağıya bak.*

---

## Karar 4 — Core ↔ View iletişimi

### Seçenekler
- **A:** Doğrudan çağrı + dönüş değeri (`var r = board.Blast(i); view.Apply(r);`)
- **B:** C# `event` / Observer pattern
- **C:** Global event bus

### Eleme gerekçeleri

**C elendi.** 2 katman, ~6 dosya. Global mesaj altyapısı "ölçeği okuyamıyor" sinyali verir. Event bus'ın değeri çok sayıda birbirini tanımayan sistem varken doğar; bizde iki taraf var ve birbirlerini gayet iyi tanıyorlar. Derleyici hiçbir şeyi doğrulayamaz, okuyan kodu takip edemez.

### A vs B — önemli nüans (kayda değer)

**Event'ler bağımlılığı kaldırmaz, derleyicinin göremediği bir yere taşır.**

- A'da ok tek yönlü: `View → Core`. **Core, View'ın varlığından haberdar değil** — ne referans, ne interface, ne delegate. Core'u konsol uygulamasına, sunucuya, teste taşıyabilirsin.
- B'de Core hâlâ View'ı tanımıyor ama **kimin dinlediğini bilmediği bir sözleşme yayınlıyor.** `BlastResult`'ın şekli değişirse View yine kırılır.

**DIP'in tam ifadesi** "bağımlılık kurma" değil, *"üst seviye modül alt seviye modüle bağımlı olmasın, ikisi de soyutlamaya bağımlı olsun."* Burada üst seviye = Core (oyun kuralları), alt seviye = View (çizim detayı). Core zaten View'a bağımlı **değil** → DIP'in koruduğu şey A ile de sağlanıyor.

**Zorunlu vs kaza eseri bağımlılık:** BoardView'ın tek işi tahtayı çizmek; tahtayı tanımadan çizemez. Bu kaldırılabilir bir kusur değil, işin tanımı. Coupling'in kötü olduğu yer *çift yönlü* veya *döngüsel* olduğu yerdir.

**A'nın somut avantajları:**
- Tek call stack'te debug (F11 ile tıklamadan animasyona)
- Testte dönüş değeri doğrudan assert edilir (event'te sahne kurmak gerekir)
- Abonelik yaşam döngüsü riski yok
- Closure/delegate allocation riski yok

### Seçilen: B (Observer) — kullanıcı tercihi

**Gerekçe:** best practice bilgisini göstermek + planlanan çoklu dinleyici (BoardView, AudioController, ScoreController, HUD).

> **Kalibrasyon notu (README için önemli):** Object pooling ile observer bu projede **aynı ağırlıkta değil.** Pooling'in somut ve ölçülebilir gerekçesi var (`Instantiate`/`Destroy` pahalı, GC spike, case doğrudan bellek+CPU diyor). Observer savunulabilir bir **stil tercihi.** README'de ikisi aynı dille anlatılmamalı. Observer kararı **planlanan dinleyicilerle** desteklenmeli, yoksa boşta duran bir kalıp gibi görünür.
>
> Güçlü README cümlesi: *"Tek dinleyici için event gereksiz olurdu; ses, skor ve HUD dinleyicileri planladığım için observer'a geçtim."*

### Doğru kurulum kuralları
Kötü kurulmuş bir event sistemi hiç kurmamaktan kötüdür.

1. **`OnEnable` += / `OnDisable` -=** — `Start`/`OnDestroy` değil. Obje kapatılıp açılırsa çift abonelik olur ve event iki kez tetiklenir.
2. **Lambda ile abone olma.** `OnBlast += r => X(r);` sökülemez (referansı yok) + closure yakalarsa heap allocation. Her zaman isimli metot.
3. **Event argümanı allocation üretmesin.** Tek `BlastResult` örneği tutulur, içindeki listeler `Clear()` ile temizlenip yeniden doldurulur (`List.Clear()` kapasiteyi korur).
   **Tuzak:** dinleyici bu nesneyi saklayıp sonra okursa bayat veri görür. Kural: dinleyici veriyi *o an* tüketir. Kodda yorumla belirtilecek.
4. **Event yüzeyi küçük.** Sadece gerçek dinleyicisi olanlar. "Her ihtimale karşı 10 event" tam da eleştirilecek gereksiz altyapı görüntüsü verir.

### Event yüzeyi
```csharp
public event Action<BlastResult> OnBoardChanged;  // BoardView, AudioController, ScoreController
public event Action OnDeadlockResolved;           // BoardView (shuffle animasyonu), AudioController
```

---

## Karar 5 — Düşme modeli

**Kaynak gereksinim:** *"Players should be able to blast stationary blocks at all times while other blocks are falling."*

Merkezî soru: **düşme animasyonu sürerken tahtanın gerçeği nedir?**

### Seçenekler
- **A — Animasyon gerçektir:** blok bir hücreye oturunca tahta güncellenir; tahta adım adım son hâline yaklaşır
- **B — Mantık anında çözülür, görsel arkadan yetişir:** Blast anında Core her şeyi hesaplar (silinecekler, düşecekler, yeni bloklar, gruplar, deadlock), tahta o anda son hâline gelir. Animasyon tamamen kozmetik.

### Eleme gerekçesi

**A elendi — sebebi performans DEĞİL, oyun kuralı.**
- Tahta animasyon boyunca **geçici ve tutarsız**. Bu sırada grup hesabı yapılamaz → ikonlar yanlış görünür veya titreşir.
- Deadlock kontrolü için ayrı bir "işlem bitti mi" durum takibi gerekir.
- Zincirleme blast (patla → düş → tekrar tıkla) sırasında durum makinesi hızla karmaşıklaşır.

> Karar 3'teki desenin tekrarı: mimariyi gereksinim belirledi.

### Seçilen: B

### Alt karar 5a — B'nin ürettiği problem ve çözümü

**Problem:** görsel konum ≠ mantıksal konum. Input konumdan hesaplanıyor ama düşen blok iki hücre *arasında*.
- Oyuncu bloğun **ineceği** boş görünen hücreye tıklarsa → mantıksal olarak orada blok var, patlar. Ama oyuncu boşluğa tıkladığını sanır.
- Oyuncu bloğun **şu an göründüğü** yere tıklarsa → mantıksal olarak orası başka hücre.

Case'in cümlesi tam bunu adresliyor: "duran blokları her zaman patlatabilmeli" derken örtük olarak **duranlar ile düşenleri ayırmamızı** istiyor.

**Alt seçenekler:**
- **B1 — Saf mantık:** düşme durumu hiç dikkate alınmaz. En basit kod, case'i ihlal etmiyor, ama "tıkladım, alakasız yer patladı" hissi. **Elendi.**
- **B2 — Oturmuş blok filtresi:** tıklanan hücredeki blok hâlâ animasyondaysa tıklama yutulur. Case cümlesinin **birebir** karşılığı.
- **B3 — Grup bazlı filtre:** grubun *tamamı* oturmuşsa patlat. **Elendi:** bu tür oyunlarda oyuncu hızlı tıklar; reddedilen her tıklama "oyun bozuk" hissi verir (Toon Blast asla beklemeni istemez). Ayrıca case "stationary blocks" diyor, "grubun tamamı" demiyor.

### Seçilen: B + B2

- `BlastResult` her bloğun **eski ve yeni index'ini** taşır; View animasyonu bundan kurar
- View her blok görseli için `IsSettled` bayrağı tutar
- Filtre **View'da**, Core'da değil → Core "kim havada" bilgisini hiç taşımıyor, saf kalıyor
- Havadaki blok başkasının patlamasıyla silinirse havada yok olur — sorun değil, akıcı görünür

> **Bonus:** Tahtanın üstünde doğan blok hiçbir zaman `IsSettled = true` olmamıştır → B2 onu kendiliğinden yutar. "Bu blok tahta dışında mı" diye ekstra kontrol gerekmiyor.
> **Ders:** İyi konumlanmış bir kural, yazılması gereken özel durumları kendiliğinden emer. Bu, kuralın doğru seviyede konduğunun işaretidir.

### Alt karar 5b — Animasyon eğrisi: **SABİT HIZ**

Sabit hızın kod avantajı: konum tek bir sayıdan türetilir.
```csharp
float t = elapsed / duration;              // 0..1, tek durum
pos = Vector3.Lerp(start, target, t);
bool settled = t >= 1f;                    // IsSettled BEDAVA geliyor
```
Blok başına hız durumu yok, kare kare integrasyon yok, birikimli hata yok. **B2'nin ihtiyaç duyduğu bayrak, animasyonun doğal yan ürünü.**

Hızlanma isteseydik fizik simülasyonu gerekmezdi, easing yeterdi (`eased = t*t`, tam olarak `½gt²` eğrisi — his aynı, durum hâlâ tek `t`). Kullanıcı gereksiz buldu, sabit hızda kalındı.

**Kritik detay:** `duration` sabit **olmamalı**. Sabit süre verilirse 1 hücre düşen blokla 8 hücre düşen aynı anda varır, gözle yanlış görünür.
→ `duration = mesafe / hız` (sabit hız tanımı gereği).

---

## Karar 6 — Başlangıç tahtası üretimi

### Seçenekler
- **A:** Tamamen rastgele
- **B:** Elle tasarlanmış level dosyası
- **C:** Kısıtlı rastgele (config'ten parametreler + kurallar)

### Eleme gerekçeleri

**B elendi — gönülsüzce.** Gerçek bir üretim projesinde doğru cevap B'dir. Eleme sebebi: case M, N, K, A, B, C'yi **parametrik** istiyor (2–10 arası her boyut çalışmalı). Elle tasarım bununla çelişir, her boyut için ayrı dosya gerekir. Case'in ruhu prosedürel üretim tarafında.

**A elendi.** Kazanma koşulu eklendiği anda çözülemez seviye gerçek bir bug hâline geliyor.

### Seçilen: C

Kutu sayısı, hamle limiti ve tüm parametreler `LevelConfig`'ten gelir. Yerleştirilemezse (küçük tahtada çok Box) sayı sessizce düşürülür.

### ⚠️ İPTAL EDİLEN KISIT — "iki Box ortogonal komşu olamaz"

Bu kuralı ben önerdim, **kullanıcı haklı olarak itiraz etti ve kural kaldırıldı.**

Gerekçe: 3×3'lük bir Box kümesinde ortadakinin tüm komşuları Box olsa bile, dış halkadaki Box'ların dışa bakan renkli komşuları var. Onlar kırılınca orta açığa çıkar. Özyineleme her zaman tahtanın renkli bölgesinde dibe vuruyor.
→ **Başlangıç komşuluğu gerçek bir engel değil.**

*(Yerine gelen gerçek kısıt için Karar 8'e bak: "en üst satıra Box konmaz".)*

---

## Karar 7 — Hedef sistemi

### Kapsam kararı: hamle limiti + "tüm Box'ları kır"

**Gerekçe (kullanıcının çıkarımı):** Renkli bloklar sonsuz üretiliyor → artan sayaç anlamsız, oyun hiç bitmez. **Box'lar yeniden doğmuyor** → "tüm Box'ları kır" doğası gereği **sonlu ve bitirilebilir**.

**Bonus:** Case'te Box sadece bir engel; oyuncunun onunla ilgilenmek için hiçbir sebebi yok. Hedef yapınca Box **oyunun amacı** oluyor → case gereksinimi oyun tasarımıyla anlamlanıyor. README'de yazılmaya değer.

**Elenen alternatifler:**
- Artan hamle sayacı, limit yok → bilgi veriyor ama hiçbir şey bağlamıyor, oyun bitmiyor
- Sadece skor → oyun bitmiş görünmüyor

### Alt karar 7a — Box = 0 durumu

**Dokümanın iki örneğinde de Box yok** → bu meşru bir durum, kaçamayız.

- **A — Serbest mod:** Box yoksa hamle limiti yok, kazanma/kaybetme yok; sadece skor ve hamle sayısı gösterilir
- **B — Config'te Box zorunlu** → **elendi:** dokümanın kendi örneklerini reddeden bir oyun teslim etmek, hedef sisteminin case'in önüne geçtiğini gösterir. Sıralamayı ters çevirir.
- **C — Alternatif hedef (skor barajı)** → **elendi:** "adil baraj" açık uçlu bir denge işi, ayarlaması zaman alır, case hiç istemiyor. Kapsamı en çok büyüten seçenek.

**Seçilen: A**, ama şu formülasyonla: **hedef bir veri, kod dalı değil.**

`LevelConfig`'te opsiyonel hedef alanı. Box = 0 ise hedef `null`, hamle limiti 0 (= sınırsız). Oyun döngüsü hep aynı çalışır, tek bir `if`. UI hedef panelini yalnızca hedef varsa gösterir.
→ "İki mod" değil, **"tek mod + opsiyonel hedef"**. Kod farkı bir `if`, kavramsal olarak çok daha temiz.

### Alt karar 7b — Shuffle hamle sayılmaz
Oyuncunun sebep olmadığı bir olay ona fatura edilmez.

---

## Karar 8 — Kazanılamaz tahta

### ⚠️ İlk gerekçe HATALIYDI — düzeltme kaydı

**Hatalı iddiam:** "Box A (5,3) ve Box B (3,3) aynı sütunda → aradaki hücre kalıcı boş, B'nin altı da kalıcı boş, yan sütunlarda da Box varsa B dört tarafından ölü hücreyle çevrilir ve asla kırılamaz."

**Kullanıcı itiraz etti, haklı.** Hatam: A'nın kendisinin de kırılabilir bir Box olduğunu hesaba katmadım.

**Doğru tümevarım:**
> Bir sütundaki **en üstteki Box'ın üstünde onu tıkayan hiçbir şey yoktur** → üstü her zaman dolar → her zaman kırılabilir. Kırılınca sıradaki Box en üste geçer, sütun yeniden açılır, altındaki kalıcı boşluk yeniden dolar.

→ Box'lar bir sütunda yukarıdan aşağı **sırayla** kırılabilir hale gelir. Kalıcı boşluklar **geçici kilit** üretir, kalıcı değil.
→ En üst satır istisnası: üstü yok ama altındaki hücre başlangıçta renkli (oradan hasar alır), yan komşuları başka sütunların en üst satırı (onlar yukarıdan dolar).

**Sonuç: yapısal olarak ulaşılamaz Box pratikte yok.** "Her Box her an patlatılabilir olmak zorunda değil" — geçici erişilemezlik normal oynanışın parçası.

### Geriye kalan GERÇEK uç durum: hareketli blok tükenmesi

Box'lar kırılabilir, ama **kırılacak renkli blok kalmayabilir.**

En küçük örnek, M=2, N=2:
```
[Box] [Box]     <- üst satır
[ R ] [ R ]     <- alt satır
```
Alttaki iki kırmızıya tıkla → patlarlar, iki Box da hasar alır. Ama patladıkları hücreler Box'ların **altında** → yukarıdan yeni blok gelemez. Tahtada 0 renkli blok, 2 canlı Box.
→ Deadlock var, shuffle çözemez (karıştıracak blok yok), oyun kazanılamaz.

Küçük tahtalarda + yüksek Box yoğunluğunda gerçekleşir. 10×10'da neredeyse imkânsız, 2×2'de bir hamlede. **Case 2×2'yi açıkça destekliyor**, ele alınması gerek.

### Sektör araştırması — bu sınıf oyunlar problemi nasıl çözüyor?

**Runtime'da değil, content pipeline'da çözüyorlar:**

1. **Seviyeler elle tasarlanıyor**, prosedürel değil (Toon Blast, Lily's Garden, Pet Rescue Saga). "2×2, üst satır tamamen Box" gibi bir düzen kimse çizmediği için hiç oluşmuyor.
2. **Otomatik doğrulama:** seviyeler yayın öncesi bot'larla binlerce kez oynanıyor. Akademik literatür de bunu problem olarak ele alıyor (arXiv 2409.06349, "Improving Conditional Level Generation using Automated Validation in Match-3 Games"). Kritik nokta: match-3'te yeterli hamle verilirse hemen her düzen çözülebilir; asıl kısıt **hamle limiti**. Sektörün derdi "çözülemez tahta" değil, **"verilen hamlede çözülemez tahta"**.
3. **Spawner'lar açık bir tasarım öğesi:** hangi sütunun yeni blok üreteceği seviyenin özelliği, otomatik kural değil. Tasarımcı belirli sütunların tepesine "generator" koyar; bazı sütunlar hiç üretmez ve bu bilinçli bir zorluk aracıdır. *(Bizim doküman "ilgili sütunun üstünden düşer" diyerek her sütunu üretici kabul ediyor.)*
4. **Tahta dikdörtgen olmak zorunda değil**, hücreler maskelenebilir. Ölü bölge kaza değil, tasarım.
5. **Runtime'da tek fallback shuffle**; gerçek "hiç hamle yok" durumu neredeyse imkânsız çünkü üreticiler sürekli renkli blok pompalıyor.

**Bize çevirisi:** Level designer'ımız yok, **generator'ımız var.** Sektörde insanın yaptığı işi üretim kodumuz yapmak zorunda. (Karar 6'da B'yi elerken kabul ettiğimiz bedelin faturası.)

### 🔑 Türetilen kural: EN ÜST SATIRA BOX KONMAZ

Zincirleme sonuçlar:
- Her sütunun en üst hücresi Box değil → her sütun yukarıdan blok alabiliyor → **en üst satır her zaman tamamen dolu**
- En üst satır dolu → tahtada her zaman ≥ N (≥2) renkli blok var **ve bunlar yan yana** → shuffle her zaman bir grup üretebilir
- Her sütundaki **en üstteki** Box'ın üstü her zaman doluyor → her zaman hasar alabiliyor → kırılınca sıradaki açığa çıkıyor

→ **2×2 senaryosu oluşamaz hale geliyor** (orada Box'lar üst satırdaydı)

Maliyet: tasarım alanı çok az daralıyor (M=2'de Box'lar sadece alt satıra girebilir).

### Nihai seçenekler
- **A — Görmezden gel** → **elendi:** oyuncu tıklayamadığı bir tahtaya bakar, hamleleri boşa akar. Kaçınması bedava olan kötü deneyim.
- **B — Runtime yenilgi (tek başına)** → **elendi:** önlenebilecek bir durumu önlemeyip sadece "kaybettin" demek, hastalığı tedavi etmek yerine sonucunu kabullenmek. Oyuncu neden kaybettiğini anlamaz.
- **C — Üretimde engelle (tek başına)** → **elendi:** garanti benim akıl yürütmeme dayanıyor ve **akıl yürütmem bu projede bir kez zaten yanlış çıktı.** Kanıtlanmamış bir invariant'a tek dayanak olarak güvenmek kötü mühendislik.
- **D — C + B birlikte**

### Seçilen: D

> **Asıl ders: C bir tasarım garantisi, B bir savunma önlemi. Farklı işler görüyorlar.**
> - C kötü tahtanın oluşmasını engelliyor → normal oynanışta B hiç tetiklenmiyor
> - B, C'nin varsayımı bozulursa (yeni engel tipi, config değişikliği, gözden kaçan durum) oyunu kilitlenmekten koruyor
> - Toplam maliyet ~5 satır

**README cümleleri:**
> *"Üretim kısıtı ile durumu yapısal olarak imkânsız kıldım, ama invariant'a tek başına güvenmeyip runtime kontrolü de bıraktım."*
>
> *"Box'lar gravity'den etkilenmediği için altlarında kalıcı boşluklar oluşur. Bu boşluklar yapısal bir kilide yol açmaz, çünkü bir sütundaki en üstteki Box'ın üstü her zaman dolar ve kırılınca sütun yeniden açılır. Ancak küçük tahtalarda (örn. 2×2) tahtadaki tüm renkli bloklar tükenebilir; bu durumda deadlock çözülemez ve seviye kaybedilmiş sayılır."*

**"En üst satıra Box konmaz" dokümanda yok, biz ekliyoruz.** Savunması: sektörde spawner yerleşimi zaten bir tasarım kararıdır; prosedürel üretimde bu rolü generator üstlenir.

---

## Karar 9 — Deadlock shuffle algoritması

**Doküman kısıtı:** *"...implement a shuffling solution which doesn't rely on 'blindly shuffle N times until deadlock is resolved'."*

### Seçenekler
- **A — Kör shuffle + tekrar dene** → doküman açıkça dışlıyor. Sonlanma garantisi yok; tek geçerli düzenleme varsa milyonlarca deneme sürebilir, teorik olarak hiç bulamayabilir.
- **B — Karıştır + bir grubu zorla** (iki aşama)
- **C — Minimal müdahale:** hiç karıştırma, aynı renkten iki bloğu komşu yapacak **tek takas** bul → **elendi:** teknik olarak en zarif ama oyuncu deneyimi en kötüsü. Görünmez düzeltme, oyunun rastgele davrandığı hissini verir. Ayrıca tahta hâlâ tek hamlelik → ardışık deadlock zinciri üretir.
- **D — Renkleri baştan üret** → **elendi:** renk dağılımı korunmuyor. 15 kırmızı varken shuffle sonrası 3 kalabilir; oyuncunun biriktirdiği "büyük grup potansiyeli" buharlaşır, haksız hissettirir. **Shuffle kavramsal olarak yeniden düzenlemedir, yeniden üretme değil.**

### Seçilen: B

1. Tüm renkli hücrelerin renklerini topla → **Fisher-Yates** ile karıştır → geri yaz
2. **Garanti adımı:** komşu iki renkli hücre seç, ikisine de aynı rengi **takas ederek** yerleştir

Tek geçişte en az bir geçerli grup. Deneme yok, döngü yok, **sonlanma garantili.**

### Çözülebilirlik koşulu (kesin, tahmin değil)

Shuffle şu iki koşul **birden** sağlanırsa çalışır:
1. **En az bir renk, en az 2 kez bulunmalı** — 1 kırmızı + 1 mavi + 1 yeşil ile hiçbir düzenleme grup üretemez
2. **En az iki renkli hücre birbirine komşu olmalı** — Box'lar ve kalıcı boşluklarla tamamen ayrılmışlarsa aynı rengi yan yana koyacak yer yok

Sağlanmazsa → Karar 8'deki yenilgi dalı.
→ **"Shuffle çözemedi" belirsiz bir tahmin değil, kesin bir koşul.** README'de güçlü durur.

### Garanti adımının işleyişi

Komşu çift `(i, j)` ve en çok bulunan renk `X` seçildikten sonra:
- `i` zaten `X` değilse: başka bir `X` hücresi `k` bul (`k ≠ i, j`), `i` ile `k`'nın renklerini **takas et**
- Aynısını `j` için yap

Takas olduğu için toplam renk sayıları değişmiyor. `X`'in sayısı ≥ 2 olduğundan ikinci arama her zaman bir aday bulur; ya da `j` zaten `X` tutuyordur ve işlem gereksizdir.

### Fisher-Yates — naif shuffle neden bozuk?

```csharp
// YANLIŞ
for (int i = 0; i < n; i++) Swap(i, Random(0, n));
```
Bu algoritmanın `n^n` çalışma yolu var ama `n` elemanın `n!` permütasyonu var. `n^n`, `n!`'e tam bölünmediği için bazı permütasyonlar **daha sık** çıkar → yanlı dağılım.
*(3 elemanda: 27 yol / 6 permütasyon = 4.5, tam sayı değil. Bazı sıralamalar 5 yoldan, bazıları 4 yoldan üretiliyor.)*

```csharp
// DOĞRU
for (int i = n - 1; i > 0; i--) {
    int j = rng.Next(0, i + 1);   // 0..i, i dahil
    Swap(i, j);
}
```
Çalışma yolu sayısı tam `n!` → her permütasyon eşit olasılıkla. Fark tek yerde: rastgele sayının üst sınırı sabit değil, döngüyle birlikte küçülüyor.

Bizim ölçekte yanlılık oyunu bozmaz ama doğrusunun maliyeti sıfır → README'ye bedava artı.

### Alt karar 9a — Shuffle animasyonu

**Kafa karışıklığının kaynağı:** biz **blokları taşımıyoruz**, hücrelerin renk değerlerini yer değiştiriyoruz. Mantıksal olarak hiçbir şey hareket etmiyor. Oyuncunun modeli ise "bloklar karıştı, yerleri değişti". Animasyon bu boşluğu kapatıyor.

- **A — Yerinde sprite değişimi** → **elendi:** ekran bir anda "zıplar", oyuncu bug sanabilir. Görsel geri bildirimsiz durum değişikliği = oyunun rastgele davrandığı hissi.
- **B — Bloklar uçarak yer değiştirsin:** Fisher-Yates zaten bir permütasyon; hangi rengin hangi hücreye gittiğini **kaydedersek** görselleri uçurabiliriz. Oyuncunun zihinsel modeliyle birebir, Toon Blast'a en yakın his. Maliyet: permütasyon kaydı + görsel-hücre bağlantısını yeniden kurmak + bloklar birbirinin içinden geçiyor.
- **C — Küçül, değiş, büyü:** tüm bloklar ölçekle küçülür, sprite'lar değişir, tekrar büyür.

**Seçilen: C.** Gerekçe: B görsel olarak daha iyi ama **case'in değerlendirdiği hiçbir şeyde puan kazandırmıyor.** Case deadlock *algoritmasını* soruyor, animasyonunu değil. C aynı bilgiyi oyuncuya iletiyor, maliyeti onda biri (~15 satır, mevcut tween altyapısıyla birebir).
B'ye geçiş sonradan ~yarım saatlik iş (permütasyon kaydı `BlastResult`'takine benzer yapı).

### Alt karar 9b — Garanti adımı her zaman mı çalışsın?

Detay: shuffle sonrası `RecalculateGroups()` **zaten çağrılmak zorunda** (ikonlar için). Yani "önce kontrol et" bedava görünüyor. Ama kontrol "grup yok" derse garanti adımı + **tekrar** hesaplama gerekir.

- **Her zaman zorla:** her seferinde tam 1 hesaplama, tek kod yolu, dallanma yok
- **Önce kontrol et:** genelde 1, bazen 2 hesaplama

**Seçilen: her zaman zorla.**
1. **Determinizm** — tek kod yolu, test kolay, "her shuffle tam bir geçişte biter" diyebiliyoruz. Case'in yasakladığı şey belirsiz süreli döngüydü; bunun en net zıddı.
2. **Kontrolün kazancı hayali** — kaybedilen tek şey biraz rastgelelik (garanti adımı bir yere kesin bir çift koyuyor). Çift rastgele bir komşu konumdan seçilirse fark gözle görülmez.

---

## Karar 10 — Render yöntemi

**Anlamlı metrik:** draw call sayısı. Her blok ayrı çizilirse 100, hepsi birlikte çizilirse 1.

### Seçenekler
- **A — Unity UI (Canvas + Image)** → **elendi:** **Canvas rebuild.** Bir `Image`'ın sprite'ı veya konumu değişince canvas o frame'de yeniden batch'lenir. Bizde her hamlede onlarca blok değişiyor, düşerken her frame konum değişiyor → sürekli rebuild. Case'in doğrudan sorduğu maliyetin ta kendisi. Ayrıca "UI ile oyun tahtası çizmek" junior seviyede en sık yapılan hata; bunu yapmamak tek başına ayırt edici.
- **B — Blok başına `SpriteRenderer` (world space) + Sprite Atlas**
- **C — Tek mesh, elle üretilmiş UV** → garantili 1 draw call ama düşme animasyonu için her frame vertex dizisi yeniden yazılmalı, blok başına transform kolaylığı gidiyor, editörde hiçbir şey görünmüyor
- **D — GPU Instancing (`DrawMeshInstanced`)** → 1 draw call, "modern" görünüyor ama GameObject yok: pooling, animasyon, tıklama hepsi elle; editörde görünmüyor

### C ve D'nin ortak eleme gerekçesi
İkisi de zaten B'nin atlas ile ulaştığı yere (**1 draw call**) ulaşıyor, karşılığında editör görünürlüğünü, transform kolaylığını ve okunabilirliği alıyorlar.
→ **Aynı sonuca daha pahalı yoldan gitmek optimizasyon değildir.** README'de bunu yazmak, D'yi yazmaktan güçlü sinyal.

### Seçilen: B + Sprite Atlas

26 PNG tek texture'a paketlenir → Frame Debugger'da tahtanın tamamı tek draw call.

Ek ayarlar (README maddesi):
- Hepsi aynı sorting layer, aynı order → batch bölünmez
- `sharedMaterial`'a dokunma (materyal örneği oluşturmamak için)

### Alt konu — Batch'i ne böler?

**`sortingOrder` tek başına batch'i BÖLMEZ.** Unity çizim sırasını sorting layer → order in layer → mesafe diye kurar; bu sıralı listede **ardışık** olan ve aynı materyali/texture'ı paylaşan renderer'lar tek çağrıda birleşir. 100 blok farklı order'a sahip olsa bile aynı atlas'ı kullandıkça ardışık kalır → tek draw call.

**Bölen şey: araya farklı materyalli bir şeyin girmesi.** Somut risk: Box kırılma partikülü. Order'ı blokların arasına düşerse:
```
bloklar(atlas) → partikül(başka materyal) → bloklar(atlas)   = 3 draw call
```

→ **Kural: farklı materyaller katman katman ayrılmalı, iç içe geçmemeli.** Partikül blokların tamamının üstünde (bloklar 0, partikül 10), arka plan tamamının altında.

### Alt konu — Ekrana sığdırma: KAMERA (tahta ölçekleme değil)

Tahtayı ölçeklersen hücre boyutu 1 dünya birimi olmaktan çıkar (`0.73` gibi) ve bu sayı **input matematiğine, konum hesabına, animasyon mesafelerine sızar.** Hiyerarşide biriken ölçek her hesabın içine giriyor.

Kamerayı ayarlarsan 1 birim = 1 hücre kalır → konumlar tam sayı, input `(int)(worldPos - origin)`, animasyon mesafesi doğrudan hücre sayısı.
→ **Ölçek tek bir yerde yaşar ve hiçbir hesaba karışmaz.**

> **Genel prensip:** bir dönüşümü mümkün olduğunca dışarıya, tek bir noktaya it. İçeri yayılmasına izin verirsen her formülde onu hatırlamak zorunda kalırsın.

```csharp
float half  = 0.5f;
float vNeed = rows * half;
float hNeed = cols * half / camera.aspect;   // orthographicSize = dikey YARI yükseklik
camera.orthographicSize = Mathf.Max(vNeed, hNeed) + padding;
```
`padding`: kenar boşluğu + üstteki skor/hedef UI'ı için yer. 10×2 gibi geniş tahtada `hNeed` baskın, 2×10'da `vNeed`.

---

## Karar 11 — Input: tıklamayı hücreye çevirme

### Seçenekler
- **A — Collider + `Physics2D.Raycast`**
- **B — Doğrudan matematik**
- **C — Blok başına `OnMouseDown`** → **elendi:** A'nın tüm sorunları + Unity'nin bunu her frame tüm sahneyi tarayarak yapması + mobilde güvenilmez davranış

### A'nın eleme gerekçesi — performans DEĞİL, doğruluk

Raycast **görsel** dünyayı sorguluyor; biz **mantıksal** hücreyi istiyoruz. Karar 5'te bu iki dünyayı bilerek ayırdık. A onları geri birleştirir ve düşen blokta yanlış hücre döndürerek B2 filtresini bozar.

Sıfır collider olması (fizik motoru hiç uyanmıyor, broadphase yapısı tutulmuyor) güzel bir **yan etki**, ana gerekçe değil.

### Seçilen: B

```csharp
Vector3 w = camera.ScreenToWorldPoint(Input.mousePosition);
int col = Mathf.FloorToInt((w.x - origin.x) / 1f);
int row = Mathf.FloorToInt((w.y - origin.y) / 1f);
```

**Akış:**
1. Ekran noktası → dünya noktası
2. Dünya noktası → `(row, col)`
3. Tahta sınırları dışındaysa yut
4. O hücredeki **görsel blok** oturmamışsa yut (B2 filtresi) ← tek "görsel" temas noktası, bilinçli olarak View'da
5. `board.Blast(Index(row, col))`

> **Not:** Bu karar Karar 5'in doğrudan sonucu. Mantığı görselden ayırdığımız an, input'un da mantıksal tarafa bakması gerektiği kendiliğinden çıkıyor. **İyi bir mimari kararı sonraki kararları kolaylaştırır.**

### Platform ve API
- **Hedef: masaüstü.** Çoklu dokunuş kapsam dışı (gereksiz karmaşıklık). Bonus: `Input.GetMouseButtonDown(0)` mobilde tek dokunuşu da yakalar, ekstra kod yok.
- **Eski `Input` API'si** kullanılacak. Yeni Input System paket kurulumu + action asset + `PlayerInput` bileşeni gerektiriyor; ihtiyacımız tek satır ("tıklandı mı, nerede"). Bir çivi için kompresörlü tabanca almamak. README'de bir cümleyle gerekçelendirilecek.

---

## Karar 12 — Proje yapısı ve assembly'ler

### Seçenekler
- **A — Tek assembly (Unity varsayılanı)**
- **B — Üç assembly: Core, Game, Tests**
- **C — Granüler (Core/View/Input/Audio/UI ayrı)** → **elendi:** 5-6 asmdef + aralarında referans grafiği + her yeni sınıfta "bu hangi assembly'ye ait" sorusu. View, Input ve Audio arasında engellemek istediğimiz bir bağımlılık yok → ayırmak sadece sürtünme üretir.

### A'nın eleme gerekçesi — derleme hızı değil, niyetin uygulanabilir olmaması

Karar 1'de "Core saf kalacak" dedik. A'da bu sadece bir **niyet**: yorgun bir anda `Debug.Log` yazarsın, derlenir, kimse fark etmez, kural erozyona uğrar.

Ayrıca test assembly'si zaten gerekiyordu → ikinci asmdef'in ek maliyeti sıfıra yakın.

### 🔑 Kilit mekanizma: "No Engine References"

`.asmdef` dosyasındaki bu seçenek işaretlendiğinde Core'da `using UnityEngine` yazmak **derleme hatası** verir.
→ Mimari karar bir konvansiyon olmaktan çıkıp **derleyicinin zorladığı bir kural** haline geliyor.

### Seçilen: B

```
Assets/
  Scripts/
    Core/                    <- asmdef: No Engine References ✔
      Cell.cs
      Grid.cs
      BoardConfig.cs
      Board.cs
      GroupFinder.cs
      GravityResolver.cs
      DeadlockResolver.cs
      BlastResult.cs
    Game/                    <- asmdef: Core'a bağımlı
      LevelConfig.cs
      GameController.cs
      BoardView.cs
      BlockView.cs
      BlockPool.cs
      InputHandler.cs
      AudioController.cs
      UI/
  Tests/
    EditMode/                <- asmdef: Core'a bağımlı
  Art/                       <- 26 PNG + Sprite Atlas
  Audio/
  Prefabs/
  Scenes/
```

Core 8 dosya. Karar 1'de başlangıçta "en fazla 4-6" yazıyordu; `Grid` ve `BoardConfig` eklenirken
o sınırın yanlış şeyi ölçtüğü ortaya çıktı ve yerine Karar 17'deki üç şartlı ölçüt kondu.

**Uyarı:** Core'da `UnityEngine.Random` da yasak. Rastgelelik `System.Random` ile **enjekte** edilecek → testte sabit tohumla shuffle'ın deterministik testi yazılabiliyor.

### Alt konu — DI vs Singleton (kavram düzeltmesi)

- **Singleton:** sınıf bağımlılığını kendisi **alır** (`Random.Instance.Next()`). Testte global'e müdahale gerekir; testler aynı örneği paylaşır → biri diğerinin durumunu bozar, sıraya duyarlı hale gelirler. **DI'ın zıddıdır.**
- **DI:** bağımlılık **verilir** (`new Board(config, new Random(42))`). Test kendi örneğini kurar, kimseyle paylaşmaz.
- `Board` içinde `new Random()` yazmak singleton değil ama yine de kötü: tohumu dışarıdan kontrol edemezsin.

> **Kural: bir sınıf kendi rastgeleliğini, zamanını veya dosya erişimini kendisi yaratmasın.** Bunlar dışarıdan gelmeli, yoksa test edilemezler.

---

## Karar 13 — `GroupFinder` veriye nasıl erişir?

Flood fill'in üç geçici diziye ihtiyacı var (`groupIdOf`, `groupSizes`, `stack`) ve bunlar Karar 3 gereği **bir kez alloc edilip yeniden kullanılacak.** Yani geçici durumun kalıcı bir eve ihtiyacı var. Soru, o evin nerede olduğu ve `cells` dizisine nasıl ulaştığı.

### Seçenekler

| | Yaklaşım | Artı | Eksi |
|---|---|---|---|
| A1 | `GroupFinder` constructor'da `Cell[] cells, int rows, int cols` alır — Board ile **aynı diziyi paylaşır** | Board şişmez, scratch dizileri sınıfın kendi field'ı, çağrı yüzeyi temiz | İki nesne aynı diziye referans tutar; "Board bu diziyi asla yeniden atamaz" yazısız bir sözleşme |
| A2 | Constructor **sadece boyutları** alır (scratch'i ayırır), veri her çağrıda `ReadOnlySpan<Cell>` olarak geçer | Tutulan referans yok → bayatlayamaz. `ReadOnlySpan` "okurum, yazmam"ı **tip seviyesinde** söyler. Test için `Board` kurmaya gerek yok | `rows`/`cols` iki yerde yaşıyor, tutarsız kalabilir |
| B | `static` metotlar, her çağrıda dizi parametre | En "saf" görünen | **Çalışmaz.** Scratch dizilerinin yaşayacak yeri yok; static field yapılırsa global durum olur, testler birbirinin sonucunu bozar |
| C | Grup bulma `Board.cs` içine gömülür | Dosya sayısı 6 → 5 | Board hem veri sahibi hem algoritma olur; tek sorumluluk erozyona uğrar |

### Eleme gerekçeleri

**B elendi.** Karar 12'deki "bir sınıf kendi rastgeleliğini/zamanını kendisi yaratmasın" kuralının aynısı, tersten okunuşu: paylaşılan geçici durum global'e taşınırsa testler sıraya duyarlı hale gelir.

**C elendi.** `GroupFinder.cs` zaten Karar 1'in "en fazla 4-6 dosya" sınırının içinde. Dosya kazanmak için sorumluluk birleştirmek yanlış takas.

**A1 elenmedi, sektörde daha yaygın olan bu.** Tek eleme sebebi, C/C++ dünyasının elinde olmayan bir aracın bizde olması (aşağıya bak).

### Seçilen: A2

```csharp
// Owns only its scratch buffers; the board data arrives per call. Nothing is retained,
// so nothing can go stale.
public GroupFinder(BoardConfig config)
public void Recalculate(ReadOnlySpan<Cell> cells)
```

**Uygulama sırasında düzeltildi:** ilk taslakta eşikler `Recalculate`'e parametre olarak geçiyordu.
Yanlıştı — `TierAt` taramadan **sonra** çağrılıyor, yani eşiklerin zaten saklanmış olması gerekiyor.
Parametre yapmak onları hem her çağrıda geçirmek hem de field'da tutmak demek olurdu. Eşikler
`rows`/`cols` ile aynı kategoride: seviye boyunca sabit → constructor'a ait. Karar 21'de bunların
hepsi `BoardConfig`'e toplandı.

`rows`/`cols` tutarsızlığı `[Conditional("UNITY_ASSERTIONS")]` bir assert ile kilitlenir (`cells.Length == rows * cols`) → release build'de tamamen kaybolur.

### Desenin sektördeki adı: caller-owned workspace

Seçtiğimiz şey **"veriyi sahibi tutar, algoritma kendi çalışma alanını tutar"** deseni. Algoritma nesnesi kalıcıdır, tek seferlik değildir ve çalışma anında **hiç allocate etmez** çünkü ihtiyacı olan belleği kurulumda bir kez almıştır.

| Sistem | Karşılığı |
|---|---|
| **Recast/Detour** — `dtNavMeshQuery` | NavMesh verisini `dtNavMesh` sahiplenir. Sorgu nesnesi `init(navMesh, maxNodes)` ile bir kez kurulur; open list ve node pool onun içinde yaşar, sonraki binlerce `findPath` sıfır allocation yapar. **Bizim `stack` + `groupIdOf`'umuzun birebir karşılığı.** |
| **Box2D** | `b2World` gövdeleri sahiplenir, çözücüler veriye pointer alır, geçici belleği `b2StackAllocator`'dan çeker. Açık tasarım hedefi: step sırasında `malloc` yok. |
| **LAPACK / BLAS** | Rutinler `WORK` dizisi + `LWORK` alır; kütüphane asla kendi belleğini ayırmaz. "Caller-owned workspace" terimi buradan. |
| **Unity DOTS** | Sistemler veri tutmaz; `NativeArray`'i `Allocator.Persistent` ile system field'ı yapıp her frame yeniden kullanmak standart pratik. |

Bu örneklerin hepsi A1 şeklinde, çünkü C/C++'ta `ReadOnlySpan` gibi bir araç yok — ödünç referansı ifade edecek tek yol yorum yazmak. C#'ta varken kullanmamak için sebep yok.

> **Ders:** Geçici durumun bir eve ihtiyacı var. Fonksiyon yerelinde tutarsan her çağrıda allocate edersin, global yaparsan testler birbirini bozar, nesne field'ı yaparsan ikisinden de kaçarsın.

---

## Karar 14 — İkon seviyesi: saklanır mı, türetilir mi?

`RecalculateGroups()` bittiğinde elimizde iki dizi var:

```
groupIdOf   : hücre index'i -> grup id'si (-1 = renkli bileşene ait değil)
groupSizes  : grup id'si    -> boyut
```

İkon seviyesi bunların fonksiyonu. Soru, üçüncü bir dizi olarak saklanıp saklanmayacağı.

### Seçenekler
- **A — `byte[] tierOf`**, `Recalculate()` sırasında hücre başına doldurulur
- **B — `TierAt(int index)` metodu**, okuma anında `groupSizes[groupIdOf[i]]` üzerinden hesaplar; ekstra dizi yok
- **C — Eşikleri View'a ver**, karşılaştırmayı View yapsın

### Eleme gerekçeleri

**C elendi.** İkon eşikleri bir **oyun kuralı** (case'in açık gereksinimi), render detayı değil. View'a taşımak Karar 1'in sınırını deler.

**A elendi.** Performans farkı yok — ikisi de aynı üç karşılaştırmayı yapıyor, sadece *ne zaman* yaptıkları farklı. Fark **senkron tutulması gereken durum sayısında**: A'da `tierOf`, diğer ikisinin türevi olarak var olur ve bir gün birinin onu güncellemeyi unutması mümkün (shuffle sonrası, Box kırıldığında). Ortaya çıkan bug **sessiz** — tahta doğru oynanır, sadece bazı bloklar yanlış ikonu gösterir. Ekrana bakan kimse fark etmez.

### Seçilen: B

```csharp
public int TierAt(int cellIndex)
{
    int gid = groupIdOf[cellIndex];
    if (gid < 0) return 0;                  // Empty or Box: no group, no tier

    int size = groupSizes[gid];

    // Tested C -> B -> A so the highest match wins. LevelConfig guarantees the
    // thresholds are strictly ascending, which is what makes this order correct.
    if (size > thresholdC) return 3;
    if (size > thresholdB) return 2;
    if (size > thresholdA) return 1;
    return 0;
}
```

Maliyet: yeniden çizimde hücre başına 1 dizi okuma + 3 karşılaştırma. 100 hücrede ~300 işlem, hamlede bir kez. Ölçülemez.

> **Ders: cache sınırını maliyetin gerçekten olduğu yere çiz.** `groupIdOf` cache'lenir çünkü üretmek tam bir flood fill gerektirir. Tier cache'lenmez çünkü üretmek üç `if`. İkisine aynı muameleyi yapmak, "cache iyidir" refleksinin düşünmenin yerine geçmesi olurdu.

**README cümlesi:** *"Grup verisi cache'lenir çünkü hesaplanması pahalı; ikon seviyesi cache'lenmez çünkü grup verisinden üç karşılaştırmayla türer. İkincisini de saklamak, senkron tutulması gereken üçüncü bir durum yaratmaktan başka bir şey yapmazdı."*

### Alt karar 14a — Tek hücrelik bileşenler de grup alır

Yalnız kalan renkli bir hücreye de grup id'si verilir (boyut 1). Alternatif (`-1` vermek) `TierAt`'e ikinci bir kod yolu eklerdi.
→ **`-1` tek anlam taşır: "renkli bileşene ait değil"** (Empty veya Box). Patlatılabilirlik ayrı bir sorudur:

```csharp
public bool IsBlastable(int cellIndex)
{
    int gid = groupIdOf[cellIndex];
    return gid >= 0 && groupSizes[gid] >= 2;
}
```

### Alt karar 14b — Metot `GroupFinder`'da, `Board` forward eder

`TierAt`'in ihtiyacı olan her şey (`groupIdOf`, `groupSizes`, eşikler) `GroupFinder`'da. Ama View'ın muhatabı `Board`.
→ Metot `GroupFinder`'da yaşar, `Board` tek satır forward yazar (`public int TierAt(int i) => groupFinder.TierAt(i);`). View Core'un iç yapısını bilmez, `Board` tek giriş noktası kalır.

---

## Karar 15 — Komşu gezinme

`CLAUDE.md`'deki sözde kod `foreach (int nb in Neighbors(cell))` diyor. Bu satırın nasıl yazıldığı, "oyun sırasında sıfır allocation" hedefinin tutup tutmayacağını tek başına belirliyor.

### Seçenekler
- **A — `IEnumerable<int>` + `yield return`**
- **B — 4 yönü elle açık yazmak** (döngü yok, dört ayrı blok)
- **C — `static readonly int[] dr/dc` ile 4'lük döngü**, sınır kontrolü `(r, c)` üzerinden
- **D — Çağıranın verdiği `int[] buffer`'ı doldur, `count` döndür**

### Eleme gerekçeleri

**A elendi — bu bir tuzak.** `yield return` derleyicinin ürettiği bir state machine **nesnesi** demek; her çağrıda heap'e gider. Flood fill'de hücre başına bir tane → tarama başına ~100 allocation. `foreach` sözdizimi bu maliyeti tamamen gizler; kodun görüntüsü temiz, Profiler'daki hâli değil.

**B elendi.** Alloc açısından C ile aynı, tek kazancı bir dizi okuması. Karşılığında dört kez kopyala-yapıştır → dördünden birinde sınır kontrolünü düzeltmeyi unutmak klasik hata.

**D elendi.** C ile aynı sonucu veriyor, karşılığında çağırana buffer yönetimi yüklüyor. Karar 10'daki kural: *aynı sonuca daha pahalı yoldan gitmek optimizasyon değildir.*

### Seçilen: C

```csharp
// Row-major 1D indexing means +-1 crosses into the neighbouring row at the edges,
// so bounds are checked on (r, c) and only then folded back into an index.
private static readonly int[] dr = { 1, -1, 0, 0 };
private static readonly int[] dc = { 0, 0, 1, -1 };
```

### ⚠️ Kritik detay — satır sarması

Sınır kontrolü **1D index üzerinde `±1` ile yapılamaz.** `index + 1`, satır sonundaki bir hücreyi bir sonraki satırın başına bağlar; komşu olmadıkları hâlde flood fill onları birleştirir.

Bu hatanın kötü yanı, **mevcut test planındaki hiçbir testin onu yakalamaması**: Test 4 çapraz komşuluğu kontrol ediyor, oysa bu hata çapraz değil **yatay sarma** üretiyor. Ayrıca yalnızca satır kenarlarında tetikleniyor, yani tahtanın ortasında her şey doğru görünüyor.

→ **Test planına 9. test:** kenar sarması yok (satır sonundaki hücre, bir sonraki satırın başındaki aynı renkli hücreyle aynı gruba girmemeli).

---

## Karar 16 — Başlangıç tahtası deadlock ile doğarsa?

Kısıtlı rastgele üretim, düşük ihtimalle de olsa hiç grubu olmayan bir tahta üretebilir. K=6 ve küçük tahtalarda ihtimal ihmal edilebilir değil.

### Seçenekler
- **A — Üretimde garanti et:** üretim sırasında en az bir komşu çifti aynı renge zorla
- **B — Mevcut mekanizmayı kullan:** `GameController` ilk karede de `RecalculateGroups()` + deadlock kontrolü çalıştırsın; deadlock varsa `DeadlockResolver` zaten devreye girer

### Eleme gerekçesi

**A elendi.** Karar 9'daki garanti adımı zaten tam olarak bu işi yapıyor. İkinci bir mekanizma yazmak, Karar 10'un kuralının tekrarı: *aynı sonuca daha pahalı yoldan gitmek optimizasyon değildir.* Üstelik iki mekanizma iki bakım noktası demek — kuralları zamanla ayrışabilir.

### Seçilen: B

Bedava gelen bir yan fayda var: shuffle yolu artık oyunun **ilk karesinde** de çalışabiliyor, yani "sadece nadir durumda tetiklenen, o yüzden hiç sınanmamış kod yolu" olmaktan çıkıyor.

> **Ders:** Yeni bir uç durumla karşılaşınca ilk soru "bunu nasıl önlerim" değil, **"bunu zaten çözen bir yolum var mı"** olmalı. Karar 8'de (C + B birlikte) tersini yapmıştık — orada iki mekanizma *farklı işler* görüyordu (biri tasarım garantisi, diğeri savunma önlemi). Burada ikisi de aynı işi görürdü.

---

## Karar 17 — Yeni bir Core tipi ne zaman açılır?

Karar 15'i uygularken ortaya çıktı: komşuluk matematiğinin **iki müşterisi** var — `Board` (Box hasarı)
ve `GroupFinder` (flood fill). Ama Karar 13/A2 gereği `GroupFinder`, `Board`'a referans tutmuyor.

### Seçenekler

| | Yaklaşım | Sorun |
|---|---|---|
| A | Her ikisi kendi 4'lük döngüsünü yazsın | Aynı off-by-one iki kopyada. Karar 15'te **tam da bu formülü** "sessiz bug üretir, mevcut testler yakalamaz" diye işaretledik |
| B | `GroupFinder`, `Board`'a referans tutsun | Karar 13'ü geri alır |
| C | `Board`'a `public static` metotlar | Çalışır, ama `GroupFinder` yine `Board` tipini tanır — A2'nin "birbirlerini hiç tanımıyorlar" kazancı kısmen gider |
| D | Ayrı `Grid.cs` — durumsuz saf fonksiyonlar | Karar 1'deki "en fazla 4-6 dosya" sınırı aşılır |

### Seçilen: D — ve sınırın kendisi değiştirildi

Karar 1'deki dosya sayısı sınırının amacı **soyutlama şişkinliğini** önlemekti: gereksiz interface,
tek implementasyonlu factory, katman katman indirection. `Grid` bunların hiçbiri değil — 25 satır,
durumsuz, dört saf fonksiyon, tek bir formülün tek evi. Ayrıca saf fonksiyon olduğu için **doğrudan
test edilebiliyor**: Test 9 (satır sarması) artık flood fill üzerinden dolaylı kurulmak zorunda değil.

Sayı yanlış şeyi ölçüyordu. Yerine geçen ölçüt:

> **Bir tipi ayırmak için üç şart birden gerekir:**
> 1. Durumu yok, ya da kendi durumunun tek sahibi
> 2. Birden fazla çağıranı var **ve** bunlar yapısal olarak paylaşamıyor
> 3. Adı gerçek bir kavram — `Grid`, `GroupFinder`, `BoardConfig` gibi.
>    `Helpers`, `Utils`, `Manager`, `Extensions` gibi bir ad koymak zorunda kalıyorsan ortada kavram
>    yok, sadece kod taşınmış demektir.

Üçüncü madde en işe yarayanı: adın kendisi testtir.

**İkinci şart mekanik değil.** Faz 3'te `DeadlockResolver` de Fisher-Yates kullanacak, yani ikinci
çağıran var — ama ortak bir `Shuffle<T>` çıkarmıyoruz. Fark: `Grid`'de paylaşılan şey **hata üretmesi
kolay** bir formüldü ve iki çağıran aynı fazdaydı; Fisher-Yates dört satır ve doğru hâli bu dosyada
zaten yazılı. "İki çağıranı var" tek başına yetmiyor, **"paylaşmamak risk üretiyor mu"** da sorulmalı.

Core'un nihai listesi 8 dosya:
`Cell.cs`, `Grid.cs`, `BoardConfig.cs`, `Board.cs`, `GroupFinder.cs`, `GravityResolver.cs`,
`DeadlockResolver.cs`, `BlastResult.cs`

---

## Karar 18 — Box'lar nasıl yerleştirilir?

`boxCount` adet Box'ı, en üst satır hariç, tekrarsız ve rastgele yerleştirmek gerekiyor.

### Seçenekler

| | Yaklaşım | Artı | Eksi |
|---|---|---|---|
| A | **Rastgele dene-tut:** hücre seç, doluysa veya üst satırsa tekrar dene | Üç satır | Sonlanma garantisi yok; `boxCount` kapasiteye yaklaştıkça deneme sayısı patlar |
| B | **Kısmi Fisher-Yates:** uygun index listesini kur, ilk `boxCount` pozisyonu karıştır | Tam `boxCount` adım, tekrar imkânsız, sonlanma garantili | Geçici `int[]` |
| C | **Düzenli aralıklı** (stride) | Allocation yok | Rastgele değil, her tahtada aynı desen |

### Eleme gerekçeleri

**A elendi — sebebi performans değil, tutarlılık.** Karar 9'da "kör deneme" desenini gerekçeli olarak
eledik; üretimde geri getirmek README'deki iddiayı zayıflatır. Bir mülakatçının yakalayacağı cinsten
bir iç çelişki.

**C elendi.** Aynı boyutta her tahtada Box'lar aynı yerlerde çıkar.

### Seçilen: B

```csharp
for (int i = 0; i < toPlace; i++)
{
    int j = i + rng.Next(boxCapacity - i);   // sadece dokunulmamış kuyruk
    Swap(candidates, i, j);
    cells[candidates[i]] = Cell.MakeBox();
}
```

**Anlatılacak asıl şey "kısmi" olması.** Tam shuffle her pozisyonu yerine oturtur; bize sadece ilk
`toPlace` tanesi lazım, o yüzden döngü erken duruyor — 90 uygun hücreden 8 Box seçerken 90 değil
**8 adım.** Bu, seçim (selection) ile permütasyon arasındaki farkı görmek demek.

Bu döngü **öne doğru**, Karar 9'daki kanonik hâl **arkaya doğru** çalışıyor. Aynı algoritma, ters
uçlardan. Yansızlığı sağlayan şey yön değil, rastgele index'in **yalnızca henüz sabitlenmemiş
bölgeden** çekilmesi. Bozuk versiyon (`rng.Next(0, n)` her adımda) tam olarak bu invariant'ı ihlal
ediyor.

### Satır yönü kararının bedava hediyesi

Satır 0 alt ve dizi row-major olduğu için **en üst satır dizinin son `Cols` elemanı** → uygun hücreler
`[0, (Rows-1)*Cols)` aralığı, yani bitişik bir önek. Filtreleme döngüsü ve `if (row == Rows-1)`
kontrolü hiç gerekmiyor. *(Karar 5'teki dersin tekrarı: doğru konumlanmış bir konvansiyon, yazılması
gereken kodu kendiliğinden emiyor.)*

### Değerlendirilip elenen: Knuth selection sampling (Algoritma S)

Uygun hücreleri tek geçişte gezip her birini *(hâlâ gereken / hâlâ kalan)* olasılığıyla almak aynı
garantileri **geçici dizi olmadan** veriyor, adım sayısı da önceden sabit. Uygulanıp χ² ile doğrulandı,
sonra B lehine geri alındı.

**Gerekçe:** Fisher-Yates'in doğruluğu bakışta görülür, Algoritma S'inki bir olasılık argümanına
dayanır. 360 byte'lık, seviye başına bir kez ödenen bir tasarruf için okunabilirlik ve tanıdıklık
feda edilmez. *(Karar 2'de bit packing'i elerken kullandığımız akıl yürütmenin aynısı.)*

---

## Karar 19 — Renkler nasıl dağıtılır?

- **A — Hücre başına düzgün rastgele** (`rng.Next(colorCount)`)
- **B — Deste yöntemi:** her renkten eşit sayıda üret, karıştır, dağıt
- **C — Kısıtlı rastgele:** büyük başlangıç grubu oluşmasın diye komşuyu kontrol et

**Seçilen: A.**

**B elendi** çünkü verdiği garanti **bir hamle sürüyor.** İlk blast'tan sonra boşalan hücreler yine
`rng.Next(colorCount)` ile doluyor (case "yeni bloklar tahtanın dışında üretilir" diyor, dengeli bir
havuzdan değil). B, yalnızca ilk karede doğru olan bir invariant kurar ve sonra sessizce bozulur.
**Tutulmayan bir garanti, hiç verilmemiş garantiden kötüdür.**

**C elendi** çünkü büyük başlangıç grubu bir problem değil — case zaten büyük grupları **ödüllendiriyor**
(ikon seviyeleri tam olarak bunun için var).

**K=1 uç durumu:** case 1–6 diyor, yani geçerli. Tahtanın tamamı tek dev grup olur. Kod bunu özel durum
olarak ele almıyor, doğal olarak çalışıyor.

---

## Karar 20 — `boxCount` sığmazsa, hedef hangi sayıyı okur?

`Math.Min(boxCount, kapasite)` ile kırpma bedava geliyor. Asıl soru gizli olan: **hedef ("tüm Box'ları
kır") hangi sayıyı kullanacak?** `LevelConfig.BoxCount` = 8 iken tahtaya 5 Box sığdıysa ve hedef 8
beklerse oyun **asla kazanılamaz.**

- **A — `Board` yerleştirilen sayıyı bir field'da tutsun**
- **B — Hiç sayma, tahtadan türet:** `RemainingBoxes()` hücreleri gezip saysın

### Seçilen: B

Karar 14'ün aynısı. Sayaç, azaltmayı unutabileceğin her kod yolunu (Box kırılması, `Generate()` ile
yeniden başlatma, ileride eklenecek başka bir silme yolu) bir bug adayına çevirir. Ve o bug **sessiz**:
ekranda hiçbir şey yanlış görünmez, oyun sadece bitmez ya da erken biter.

**Metot, property değil.** Property "alan erişimi kadar ucuz" vaat eder; O(n) bir iş metot olmalı,
yoksa döngü içinde çağrılması masum görünür ve O(n²) üretir. .NET'te `Array.Length` property,
`Enumerable.Count()` metottur — biri saklanmış, diğeri sayıyor.

**Eşik:** her frame çağrılsaydı, ya da tahta 1000×1000 olsaydı sayaç doğru tercih olurdu. Bizde hamlede
bir kez, 100 hücre.

---

## Karar 21 — `BoardConfig`

Eşikler `GroupFinder`'a taşınınca constructor `Board(int, int, int, int, int, Random)` oldu — arka
arkaya beş `int`. Bu imzada `thresholdB` ile `thresholdC`'yi ters vermek derleme hatası değil,
**sessiz bir bug.** `Generate(colorCount, boxCount)` de aynı sorunun küçük hâliydi.

### Seçenekler
- **A — Parametreleri ekle:** pozisyonel `int` yığını, derleyici yardım edemez
- **B — `readonly struct BoardConfig`**

### Seçilen: B

Karar 17'nin üç şartını geçiyor: immutable değer, birden fazla çağıran (`Board`, `GroupFinder`,
ileride `GameController`), ve gerçek bir kavram adı.

```csharp
public readonly struct BoardConfig {
    Rows, Cols, ColorCount, ThresholdA, ThresholdB, ThresholdC, BoxCount
    public void Validate();
}
```

Üç şey bedava geldi:

1. **Core ↔ Game sınırı tek noktada.** `LevelConfig` bir `ScriptableObject`, Core'a giremiyor;
   değerlerini `BoardConfig`'e kopyalıyor. İleride bir alan eklenirse geçirilecek tek yer orası.
2. **`Validate()` Core'a taşındı.** `OnValidate` sadece inspector'ı koruyor; **testler config'i
   doğrudan kuruyor ve inspector'a hiç uğramıyor.** Kuralın asıl yeri Core.
3. **`Generate()` parametresiz.**

**`MoveLimit` ve `Seed` bilinçli olarak yok:** ikisi de tahtanın şeklini belirlemiyor. Hamle limiti oyun
döngüsünün, tohum ise enjekte edilecek `Random`'ı kurmanın konusu.

**Eşik sırası kuralı iki yerde, farklı davranışla:** `LevelConfig.OnValidate` sessizce düzeltiyor
(authoring), `BoardConfig.Validate()` gürültülü şekilde reddediyor (kod). İkisi de doğru — kullanıcıya
yardım et, programcıya hata ver.


---

## Karar 22 — Board'u kim yaratır? (composition root)

View ilk kez yazılırken şu soru kaçınılmaz oldu: `Board` nesnesi nerede doğuyor, `LevelConfig` →
`BoardConfig` dönüşümünü kim yapıyor?

### Seçenekler
- **A — `BoardView` kendi kurar** (LevelConfig'i okur, Board'u yaratır, çizer)
- **B — Ayrı `GameController` (composition root)**
- **C — DI container** (Zenject/Extenject, VContainer, Reflex) → Board bir binding
- **D — ScriptableObject-based architecture** (Ryan Hipple, Unite 2017) → Board bir SO'da yaşar

### Eleme gerekçeleri

**A — sorumluluk sızıntısı.** View'ın işi bir tahtayı çizmek; *hangi* tahtanın var olduğuna karar
vermek değil. Bunu view'a verirsen Faz 6'da hamle akışı da doğal olarak oraya sızar — "God View"
tam olarak böyle doğar. Ayrıca tahtayı kurmak için sahne açmak gerekir hâle gelir.

**C — container'ın çözdüğü problem bizde yok.** VContainer bugün Zenject'in yerini alıyor (compile-time
codegen, ölçülebilir şekilde daha hızlı) ve **büyük takımlarda gerçekten doğru araç.** Ama container'ın
kazandırdığı şey *çok sayıda, değişken, dallanan* bir bağımlılık grafiğini bildirimsel yönetmek.
Bizim grafik tek yönlü ve üç düğümlü: `GameController → Board`, `GameController → BoardView`.
Container burada yalnızca bir `Installer` dosyası ve yeni bir öğrenme yüzeyi ekler.

**D — SO architecture'ın faydası başka bir eksende.** Asıl kazancı *sahneler arası* paylaşılan durum
ve designer'ın kod yazmadan bağlaması. Tek sahne, tek tahta var. Dahası runtime state'i bir SO'ya
koymak editörde kalıcılaşma tuzağı getirir — `LevelConfig`'de alanları bilerek yazılamaz property
yapmamızın sebebi de bu.

### Seçilen: B

```csharp
// GameController.Start()
var config = new BoardConfig(level.Rows, level.Cols, ...);   // Core ↔ Game tek dönüşüm noktası
var rng    = level.Seed == 0 ? new System.Random() : new System.Random(level.Seed);
board = new Board(config, rng);
board.Generate();
if (board.IsDeadlocked) board.TryResolveDeadlock();          // Karar 16
boardView.Bind(board); boardView.Redraw();
```

Faz 4'te sadece bu kadar; Faz 6'da hamle akışı, skor ve kazanma/kaybetme **aynı sınıfın içine**
ekleniyor. Atılacak geçici kod yok.

> **Kayda değer nüans:** B, C'nin container'sız hâli — reddettiğimiz bir kalıp değil, **aynı kalıbın
> bu ölçekteki formu.** "Kim kimi yaratır" sorusunun tek bir cevabı olması, DI container kullanılsa
> da kullanılmasa da aynı ilkedir; container o kökü sadece daha bildirimsel yazar. README'de bunu
> böyle ifade etmek, container'ı "gereksiz" diye elemekten daha güçlü.

---

## Karar 23 — Renk + ikon seviyesi → Sprite eşlemesi nerede yaşar?

### Seçenekler
- **A — `BoardView`'da serialize edilmiş `ColorSprites[]`** (renk indeksine göre; her eleman Default/A/B/C)
- **B — Ayrı `BlockSprites` ScriptableObject** (sprite veritabanı)
- **C — İsimden yükleme** (`Resources.Load($"{color}_{tier}")` veya Addressables)
- **D — Serialize edilebilir dictionary** (Odin, AYellowpaper vb. bir paketle)

### Eleme gerekçeleri

**C — yanlış araç, ve sessiz başarısızlık.** `Resources` Unity'nin resmî olarak kaçınılmasını
söylediği mekanizma (build'e her şeyi katar, startup'ta manifest maliyeti). Addressables doğru araçtır
ama sorunu *content update* ve *bellek bütçesi* olan projeler için — bizim 26 sprite'ımız zaten tek
atlas. Asıl eleme gerekçesi performans değil: **isimden yükleme eksik asset'i runtime'da sessiz `null`
yapar.** Serialize referans ise editörde görünür, eksikse Play'de adıyla hata verir.

**D — dictionary'nin dejenere hâli.** Unity dictionary serialize etmiyor; bunu istemek üçüncü parti
paket demek. Bizde anahtar zaten `[0, K)` aralığında **yoğun bir tamsayı** → doğru veri yapısı dizi.
Hash tablosu kurmak burada aynı sonuca daha pahalı yoldan gitmek.

**B — yanlış değil, erken.** Tek tema ve tek tüketici var.

### Seçilen: A

```csharp
[Serializable] private sealed class ColorSprites {
    public Sprite defaultIcon, iconA, iconB, iconC;
    public Sprite ForTier(int tier) => ...;   // 3→C, 2→B, 1→A, aksi→default
}
```

Box sprite'ları ayrı bir dizi, **alınan hasara göre indeksli** (`boxSprites[BoxMaxHealth - health]`),
çünkü asset'ler `Box0` = hasarsız, `Box1` = 1 hasar almış olarak adlandırılmış.

### Ne zaman B'ye geçilir

Gerçek gerekçesi **skin/tema sistemi**: aynı tahta, farklı sprite seti. O gün `BoardView` tek bir
`[SerializeField] BlockSprites theme` alanına iner — 5 dakikalık refactor, bugünden ödenmesi gereken
bir bedel değil.

> **Genel kural:** *asset tablosunu, onu değiştirecek kişinin açacağı yere koy.* Sprite setini yalnızca
> programcı değiştiriyorsa view'da serialize alan; designer varyant üretiyorsa SO veritabanı.
> Content-heavy mobil yapımlarda (King, Playrix tarzı) SO + Addressables kombinasyonu standart —
> orada tablo *içerik*, bizde *kurulum*.

---

## Karar 24 — Pool: kendi sınıfımız mı, `UnityEngine.Pool` mu?

### Seçenekler
- **A — Kendi `BlockPool`'umuz:** sabit kapasite, başta hepsi üretilir, tükenirse **fırlatır**
- **B — `UnityEngine.Pool.ObjectPool<T>`** (Unity 2021.1+ ile gelen resmî generic pool)
- **C — MonoBehaviour `PoolManager` singleton'ı**
- **D — Büyüyen pool** (tükenince `Instantiate` eder)

### B'nin eleme gerekçesi — API'yi bilmemek değil, semantiğini bilmek

`ObjectPool<T>` **lazy**'dir: `Get()` çağrıldığında elde yoksa `createFunc` ile yaratır.
`defaultCapacity` iç `Stack`'in kapasitesidir, "şu kadar obje hazır dursun" demek değil. Yani
**"oyun sırasında sıfır `Instantiate`" garantisini kendisi vermiyor** — prewarm'ı yine sen yazıyorsun
(`Get` → listeye at → hepsini `Release`).

Case'in doğrudan sorduğu şey bu garanti olduğu için, garantiyi **kodun şeklinden okunur** yapmak
burada ~40 satıra değiyor. README'de *"resmî API'yi biliyorum, prewarm semantiği bu vaadi vermediği
için kullanmadım"* demek, kullanıp aynı vaadi ayrıca yazmaktan daha iyi bir sinyal.

### C'nin eleme gerekçesi
`Update`'i olmayan, sahnede görünmesi gereken hiçbir sebebi olmayan bir MonoBehaviour + singleton.
`BoardView` pool'un sahibi; sahnede ayrıca durması "kim sahibi" sorusunu bulanıklaştırır.
→ **Plain C# sınıfı** (Karar 17'nin üç şartını geçiyor: kendi durumunun tek sahibi, gerçek bir kavram adı).

### D'nin eleme gerekçesi — sektörün varsayılanı, ama farklı bir soruya cevap

Büyüyen pool sektörde **doğru varsayılan**, çünkü tipik senaryoda yük tahmin edilemez ve spike
absorbe edilmek istenir. Bizde kapasite tahtadan türetiliyor: ekranda `M*N` hücreden fazla blok
gösterilemez. O yüzden tükenme bir yük artışı değil, **iade edilmemiş blok = sızıntı** demek.
Büyüyen pool bunu sessizce yamalar; fırlatan pool, var olma sebebi olan bug'ı görünür kılar.

### Seçilen: A

```csharp
pool = new BlockPool(blockPrefab, transform, board.CellCount + board.Cols);
```

Kapasite `M*N + bir satır`: `Redraw` **önce tüm blokları iade eder, sonra kiralar**, dolayısıyla tepe
kullanım tam olarak `M*N`. Fazladan satır ileride bir efektin kısa süre elinde tuttuğu blok için pay —
off-by-one'ı "olası değil" olmaktan çıkarıp "imkânsız" yapıyor.

Çift iade de fırlatıyor: aynı bloğu iki kez iade etmek onu iki hücreye birden kiralatır ve hata
**bambaşka bir yerde eksik blok** olarak görünür.

> **Not:** sabit kapasite ile büyüyen pool rakip değil, farklı sorulara verilen cevaplar. Sınırı bilinen
> tahta objelerinde prewarm + sabit kapasite; sınırı bilinmeyen partikül/popup/ses kaynaklarında
> büyüyen pool. Faz 7'deki Box kırılma partikülü ikinci gruba girerse orada başka karar verilir.

---

## Karar 25 — `Redraw()` tam yeniden çizim mi, diff mi?

**Seçilen: şimdilik tam rebuild.** Tahta Faz 4'te statik; `Redraw` tüm blokları iade edip yeniden
kiralıyor.

Faz 5'te `BlastResult` okunacak ve **yalnızca değişen** bloklara dokunulacak. `Redraw` o zaman
kaybolmuyor: "tahtayı ilk gösterme" ve "shuffle sonrası yeniden çizme" yolu olarak kalıyor.

**Kayda değer olan gerekçe:** diff'i **performans için değil, animasyonun ihtiyacı olduğu için**
yapacağız. 100 `SpriteRenderer`'a sprite atamak zaten mikrosaniyeler; ama düşen bir bloğu
"eski yerinden yeni yerine taşımak" ancak hangi bloğun hangisi olduğunu bilirsek mümkün.
→ Aynı optimizasyonu yanlış gerekçeyle savunmak, README'de en kolay yakalanan zayıflık.

---

## Karar 26 — Deadlock'u kim çözer: `Board` mı, oyun akışı mı?

### Seçenekler
- **A)** `TryBlast` deadlock görürse kendi içinde shuffle eder
- **B)** `Board` durumu bildirir (`IsDeadlocked`) ve çareyi sunar (`TryResolveDeadlock`); çağıran karar verir

### A'nın eleme gerekçesi
Basit görünür ama **kazanılmış tahtayı da karıştırır.** Hamle akışında deadlock kontrolü kazanma ve
kaybetme kontrollerinden **sonra** gelmek zorunda — bu bir oyun kuralı, tahta kuralı değil. A seçilirse
o sıra `Board`'un içine gömülür ve `GameSession` onu değiştiremez.

### Seçilen: B
`Board` durumu söyler ve çareyi sunar; sırayı `GameSession.Play` kurar.

**Yan fayda:** `GameSession.Begin()` aynı iki çağrıyı kullanıyor → doğuştan deadlock'lu tahta için ayrı
bir garanti yoluna gerek kalmıyor ve shuffle kodu nadir bir tahtada değil, normal oyunda ısınıyor (Karar 16).

---

## Karar 27 — Bir hamlenin sonucu view'a nasıl taşınır?

### Seçenekler
- **A)** Her hamlede yeni bir sonuç nesnesi, içinde `List<int>`'ler
- **B)** Tek örnek, her hamlede yeniden doldurulan sabit diziler

### A'nın eleme gerekçesi
Hamle başına 5-6 allocation demek; "oyun sırasında sıfır allocation" hedefiyle doğrudan çelişir.
`List` ayrıca kapasitesi oturana kadar ilk hamlelerde büyümeye devam eder.

### Seçilen: B
Diziler `M*N` ile sınırlı, çünkü bir hamlede hiçbir liste hücre sayısını aşamaz: her hamle **indiği
hücreyle** tekilleşir ve iki blok aynı hücreye inmez. Yani `List`'in sunduğu tek şey — büyüme — hiç
gerçekleşmeyen şeydir.

**Bedeli ve kuralı:** dinleyici veriyi çağrı sırasında tüketir, **saklamaz.** Saklanan bir referans bir
sonraki hamlenin verisini gösterir.

`Clear()` sadece sayaçları sıfırlar. Altta kalan bayat değerlere span'ler üzerinden erişilemez;
kimsenin okuyamayacağı veriyi gizlemek için her hamlede binlerce int silmek boş iş olurdu.

### Alt karar 27a — Yeni bloklar: ayrı liste mi, tahta dışı index mi?
Yeni blok, kaynağı **tahtanın üstünde** bir index olan sıradan bir hamle olarak kaydediliyor:
`SpawnSource(col, order) = (rows + order) * cols + col`.

Aynı satır/sütun aritmetiği bu index'i çözdüğünde tahtanın üstünde, doğru sütunda bir konum verir —
yani view'ın **iki değil tek** kuralı olur: "her bloğu kaynağından hedefine taşı".

İkinci fayda: tahtanın üstünde doğan blok hiç oturmamıştır, dolayısıyla B2 filtresi (Karar 5) onun
üstündeki tıklamayı ayrı bir özel duruma gerek kalmadan zaten reddeder.

---

## Karar 28 — Shuffle'ın garanti adımı komşu çifti nasıl seçer?

Garanti adımı (Karar 9) bir komşu renkli hücre çiftine ihtiyaç duyuyor.

### Seçenekler
- **A)** Bulunan ilk çift
- **B)** Tüm çiftleri topla, aralarından birini seç
- **C)** Reservoir sampling (k=1): n. aday `1/n` olasılıkla tutulur

### Eleme gerekçeleri
- **A** — grubu her shuffle'da tahtanın **aynı köşesine** koyar. Oyuncu bu örüntüyü iki üç shuffle'da
  görür ve tahtaya inanmayı bırakır. Doğruluk sorunu değil, güven sorunu.
- **B** — boyutu önceden bilinmeyen bir liste ayırır.

### Seçilen: C
Aynı anda tek aday tutulur → allocation yok, dağılım düzgün, tarama tek geçiş.

Tarama sadece **Up ve Right** yönlerine bakıyor: böylece her sırasız komşu çifti sampler'a tam olarak
bir kez sunulur. Dört yön de aynı düzgünlükte örneklerdi (her çift iki kez sunulurdu) ama işi ikiye
katlar ve okuyanı bunu ispatlamak için durdurur.

### Alt karar 28a — Renk histogramı neden `ColorCount` değil 256?
`ColorCount` boyutunda bir histogram, palet dışı bir renk taşıyan tahtada (elle yazılmış seviye, test
tahtası) index taşırır — yani **savunma amaçlı yolun kendisi** crash eder. `byte` renginin tüm alanı
1 KB tutuyor; sabit 256 alınıyor.

---

## Karar 29 — Sunum katmanı: sessizce toparlanmak yerine fırlatmak

`BlockPool.Rent` havuz tükenince büyümek yerine `InvalidOperationException` fırlatıyor.

**Gerekçe:** kapasite tahtadan türüyor (`CellCount + Cols`), dolayısıyla tükenmesi absorbe edilecek bir
yük tepesi **değil** — view'ın bir bloğu iade etmediği anlamına gelir. Sessizce allocate eden bir havuz,
var olmak için kurulduğu bug'ı gizler.

Aynı refleks üç yerde daha:
- `BlockPool.Return` çift iadeyi yakalar. Aynı blok iki hücreye verilirse hata bambaşka bir yerde
  "kayıp blok" olarak görünür.
- `FallAnimator.Begin` bir hücreye zaten uçan blok varken ikincisini reddeder.
- `BoardView.ValidateSprites` `Bind` anında bir kez koşar. Atanmamış bir sprite aksi halde çizim
  döngüsünün ortasında, hangi rengin bağlanmadığını söylemeyen bir null reference olarak çıkar.

Bunların ortak noktası: hepsi **sessiz bozulmayı gürültülü hataya çeviriyor.** Sessiz olanların üçü de
ekranda yanlış bir görüntü olarak belirir, ki bakarak fark edilmez.

---

## Karar 30 — Efekt katmanı: `ParticleSystem` mi, havuzlanmış `SpriteRenderer` mı?

Patlama pop'u, kırılma parçaları ve iniş squash'ı için.

### Seçenekler
- **A.** Unity `ParticleSystem` (blok başına bir burst, ya da havuzlanmış birkaç sistem)
- **B.** Kendi atlas'ımızdan kiralanan `SpriteRenderer`'lar, tek bir `Tick` ile sürülen struct dizisi

### A'nın eleme gerekçesi
Bir particle system **kendi material'i ile çizer.** Tahta, efektler, arka plan ve çerçeve şu an tek bir
material paylaşıyor (hepsi `BlockAtlas`'ta) — araya farklı bir material giren an batch ikiye bölünür.
Case'in açık odağı GPU dahil performans; cilayı tam da ölçülen şeyi bozarak eklemek ters bir takas olurdu.

İkinci gerekçe: `ParticleSystem` blok sprite'ını kullanamaz. Patlayan bloğun **kendi görüntüsünün**
şişip dağılması, jenerik bir toz bulutundan hem daha okunur hem bedava — sprite zaten elimizde.

### Seçilen: B
`EffectRunner` bilinçli olarak `FallAnimator` ile aynı şekle sahip: önceden ayrılmış `Effect[]`, `count`,
geriye doğru dönen tek `Tick`, silinen slota son elemanı çekme. Bir okuyucu ikisinden birini anladığında
diğerini de anlamış olur.

**Havuz ayrımı — bu kısım tercih değil, zorunluluk.** Efekt sprite'ları **kendi** havuzundan kiralanır,
tahtanınkinden değil. `ApplyBlast` patlayanları serbest bırakmayı, yerlerine gelecekleri kiralamadan
**önce** yapıyor; ölen bloğu animasyon süresince tutmak ana havuzu tam da ihtiyaç duyduğu anda aç
bırakırdı. Ölen blok anında iade edilir, efekt onun `Sprite` ve `Position` değerlerini kopyalar.

**Tavan aşıldığında efekt sessizce düşer.** `BlockPool.Rent`'in fırlatma davranışının (Karar 29) aksine:
orada tükenme bir bug'dır, burada 100 bloklu bir tahtanın tek hamlede patlaması. Efekt kozmetik —
düşürülen bir kıvılcım eksik bir parıltıdır, yanlış bir tahta değil.

---

## Karar 31 — Düşüş: sabit hız mı, yerçekimi mi?

Başlangıçta sabit hızdı (`mesafe / hız`), yerçekimine çevrildi (`sqrt(2·mesafe / g)`, konum `t²` ile).

**Sabit hızın orijinal gerekçesi ayakta kalıyor:** süre mesafeden türemeli, yoksa uzun düşüşler kısa
olanlardan gözle görülür şekilde hızlı olur. Yerçekimi de bu kısıtı sağlıyor — değişen sadece
mesafe→süre eğrisi. Yani bu bir kararın iptali değil, aynı kararın daha iyi bir uygulaması.

**Neden değişti:** hızlanmayan bir düşen blok düşmüyor, **kayıyor.** Ağırlık hissi tamamen ivmeden geliyor.

`t²` bilinçli olarak `Easing.InQuad`'a bağlanmadı (Karar 35): o bir easing değil, `d = gt²/2`'nin konum
yarısı. Formüllerin aynı olması tesadüf; oraya easing adı koymak fiziksel gerekçeyi silerdi.

---

## Karar 32 — HUD: legacy `Text` mi, TextMeshPro mu?

`UnityEngine.UI.Text` + built-in Arial ile başlandı, TMP + Baloo 2 ExtraBold'a (OFL) geçildi.

**Gerekçe:** legacy `Text` bitmap font atlas'ı kullanır ve ölçeklendiğinde bulanıklaşır; TMP'nin SDF'i
her boyutta keskin ve outline/gölge bedava gelir. Case "3rd party serbest" diyor, TMP zaten
`com.unity.ugui` içinde kurulu — kullanmamak için sebep yoktu.

**Font asset statik, dynamic değil.** Dynamic bir font asset eksik glyph'i oyun sırasında render eder,
yani allocate eder — projenin kendini ölçtüğü tek şey. 95 karakterlik yazdırılabilir ASCII önceden
pişirilir. Font asset'i elle değil `PolishSetup.CreateFontAsset` ile üretiliyor: elle yeniden kurulan
bir atlas farklı padding veya sampling ile gözle görülür şekilde başka render eder.

**Etiketler string üretmiyor.** `label.text = $"Score {x}"` yerine `label.SetText("{0:0}", x)` — TMP
kendi tamponuna formatlar. Skor artık sayarak yükseldiği için etiket her karede değişiyor;
interpolasyon olsaydı saniyede birkaç yüz bayt heap'e giderdi.

---

## Karar 33 — Klasör düzeni: ince taksonomi mi, asmdef sınırları mı?

Referans olarak bakılan bir başka case projesi (`DreamCase`) `Animation/`, `Board/`, `Core/`, `Data/`,
`Effects/`, `Mechanics/`, `UI/`, `Utils/` diye dokuz klasöre ayrılmış. İlk bakışta daha derli toplu.

### Eleme gerekçesi
O dokuz klasörün tamamı **tek bir asmdef altında.** Yani hiçbir sınır derleyici tarafından zorlanmıyor,
klasörler yalnızca bir isimlendirme geleneği. Sonucu somut: oradaki `MatchFinder` `Cube` MonoBehaviour'ları
üzerinde çalışıyor, dolayısıyla grup bulmayı sınayan bir test bile sahnede gerçek bir `GameObject` kurmak
zorunda.

Bu projede klasörler zaten asmdef sınırlarıyla örtüşüyor: `Core` (`noEngineReferences: true`), `Game`,
`Tests`. Yani `Core/` bir konvansiyon değil, `using UnityEngine` yazınca derlemeyen bir duvar.

### Seçilen
`Game/` altında **üç** alt klasör — `Board/`, `Effects/`, `UI/` — akış ve Unity kabuğu (`GameController`,
`InputHandler`, `LevelConfig`) kökte. Yeni asmdef yok: alt klasörler zaten `BlastGame.Game`'in içinde.

On yedi dosyayı dokuz klasöre bölmek klasör başına iki dosya demek olurdu; bu, asıl güçlü sinyali —
derleyici tarafından zorlanan `Core` sınırını — görsel gürültüyle seyreltir. **Klasör sayısı mimari
ölçmez.**

---

## Karar 34 — `BoardCamera` neden ayrıldı, shuffle neden ayrılmadı

Cila eklendikçe `BoardView` 399 → 554 satıra çıktı ve sekiz iş yapmaya başladı.

**Ayrılan: kamera.** `FitCamera`, sarsıntı ve sarsıntının döndüğü taban konum `BoardCamera`'ya taşındı.
Üç şartı da geçiyor (Karar 17): kendi durumunun tek sahibi (taban konumu başka kimse yazamaz),
gerçek bir kavram, ve adı `Manager`/`Helper` değil. Bir tahtayı **çerçevelemek** ile **çizmek** yalnızca
tarihsel olarak aynı sınıfta bulunuyordu.

**Ayrılmayan: shuffle animasyonu.** `blockAt` dizisine ve `Redraw()`'a doğrudan bağlı. Ayırmak diziyi ve
bir redraw callback'ini geçirmeyi gerektirirdi — yani **kaldırdığından fazla bağ kurardı.** Satır sayısı
düşürmek uğruna yapılan bir ayrım, ayırdığı iki parçayı birbirine daha sıkı bağlıyorsa kayıptır.

`BoardCamera` da `MonoBehaviour` değil, `FallAnimator` ve `EffectRunner` gibi: `BoardView`'un zaten tek
bir `Update`'i var ve sıralama orada belli.

---

## Karar 35 — `Easing` nerede yaşıyor

Eğriler `HudView` içinde private bir `EaseOutBack` ve `EffectRunner`/`BoardView` içine gömülü doğrusal
`Lerp`'ler olarak dağılmıştı. Tek bir statik `Easing` sınıfında toplandı.

**`Core`'da değil, `Game/Effects/`'te.** `Core` tahta kurallarının yeri; easing sunum katmanına ait.
Motor bağımsız olması onu `Core`'a ait yapmaz — `Core`'un kapsamı "motorsuz" değil, "oyunun kuralları".

**Sadece çağıranı olan eğriler var** — `InQuad`, `SmoothStep`, `OutBack`. Easing tabloları bir düzine
kullanılmayan fonksiyonun biriktiği yerdir ve okuyucu kullanılmayan bir eğriyi, henüz okumadığı bir
yerde kullanılan bir eğriden ayırt edemez.

**Sınıf yorumundaki `LerpUnclamped` kuralı asıl değerli kısım.** `OutBack` kasten 0..1 aralığını aşar —
overshoot eğrinin bütün amacı. `Mathf.Lerp` kendi `t`'sini clamp'lediği için overshoot'u **sessizce**
yutar ve geriye yalnızca doğrusal görünen bir animasyon kalır. Bu kuralı yazmayan bir easing tablosu,
kullanıcısını fark edilmeyen bir hataya sokar.

---

## Karar 36 — Oynanabilirlik garantisi üretimde başlar

Elle test sırasında 2x2 bir tahta kilitli açıldı: hamle yok, shuffle yok, kayıp yok. Sebep iki ayrı
kusurdu ve **58 testin hiçbiri ikisini de görmüyordu** — çünkü `Board.Generate()` testlerde hiç
çağrılmıyordu. Oyuncunun gördüğü her tahtayı üreten metot sıfır kapsamdaydı.

### Kusur 1 — `boxCount == 0` grup taramasını atlıyordu

`Generate()`, yerleştirilecek Box yoksa `RecalculateGroups()`'a varmadan erken dönüyordu. Taranmamış
grup verisiyle `groupIdOf` sıfırlarla dolu kalıyor, `LargestGroupSize` 0 oluyor: **her hücre
patlatılamaz, bütün tahta deadlock.** Ölçüldü — 8x8, K=4, Box=0 için 200 seed'in 200'ü.

Oyun bunu kazara toparlıyordu: `GameSession.Begin()` deadlock görüp shuffle çağırıyordu. Yani Box'sız
her seviye gereksiz bir shuffle ile başlıyor ve ekrandaki tahta üretilen tahta olmuyordu. Box'sız,
case dökümanındaki **iki örneğin de şekli.**

### Kusur 2 — çözümsüz tahta sessizce "oynanıyor" kalıyordu

`Begin()` şunu yazıyordu:

```csharp
if (board.IsDeadlocked) board.TryResolveDeadlock();   // dönüş değeri yutuluyor
State = GameState.Playing;
```

`Play()` içinde aynı çağrının başarısızlığı doğru işleniyordu (`Finish(GameState.Lost)`), ama açılışta
yutuluyordu. 2x2 + 1 Box = 3 renkli hücre; K=6 ile üçünün de farklı çıkma olasılığı `(6/6)(5/6)(4/6)`,
yani **%56**. Ölçüldü: 500 seed'in 277'si `Playing` durumunda ve hamlesiz başlıyordu.

### Seçilen çözüm

**Üretim, shuffle'ın yapamadığını yapabilir.** `DeadlockResolver` renk sayılarını korumak zorunda
olduğu için *takas* eder ve hiçbir renk iki kez geçmiyorsa başarısız olur — bu doğru davranış.
`Generate()` ise sıfırdan **atama** yapar, kısıtı yoktur. Dolayısıyla `GuaranteeALegalMove()` tek bir
yazma işlemidir ve başarısız olamaz: üst satır Box tutmadığı için oradaki herhangi iki komşu, iki
komşu renkli hücredir.

Sütun sabit sıfır değil, çekiliyor — garanti edilen çift hep aynı köşede oturmasın diye. Bu, Karar
28'de shuffle'ın çifti örneklemesinin gerekçesinin aynısı.

**Tek istisna tek sütunlu tahta:** yatay komşusu yoktur ve Box'lar üst satırın altındaki her satırı
doldurursa geriye eşleşecek tek bir renkli hücre kalır. Core `Cols >= 1`'e izin verdiği için bu şekil
temsil edilebilir; `LevelConfig` 2-10'a kıstığı için oyunda üretilemez.

**Ve `Begin()` artık başarısızlığı okuyor:**

```csharp
State = board.IsDeadlocked && !board.TryResolveDeadlock() ? GameState.Lost : GameState.Playing;
```

Hamlesi ve çaresi olmayan bir seviye bitmiştir. Oyuncuyu asla cevap veremeyecek bir tahtaya
dokundurmaya devam ettirmek, kaybettirmekten kötüdür — Karar 29'un "sessiz bozulmayı gürültülü hataya
çevir" refleksinin aynısı, bu sefer oyuncuya dönük.

### Ders

Bu iki kusur da **hesapla değil, elle oynayarak** bulundu. Yazdığı boyutta (10x10, K=6) ikisi de
pratikte görünmez: Box her zaman var, ve 100 hücrede doğuştan deadlock astronomik olarak imkânsız.
Kapsam boşluğunu görünür kılan şey **aralığın ucundaki bir config**'ti — 2x2. Test paketi artık
üretimi yedi farklı şekil ve 200 seed ile sınıyor, ve o şekillerin çoğu kimsenin kazara oynamayacağı
şekiller.

---

## Uygulama notları

Karar sayılacak kadar büyük değil ama koddan okunmayacak kadar da örtük olan şeyler.

- **`GroupFinder` stack sınırı.** Hücre **push edilirken** işaretlenir, pop edilirken değil. Pop'ta
  işaretlense aynı hücreyi dört komşusu da itebilir ve stack hücre sayısını aşabilirdi. Push'ta
  işaretleme her hücrenin en fazla bir kez itilmesini garanti eder → `stack` boyutu tam olarak `M*N`.
  Karar 3'ün iterative DFS tercihini tamamlayan detay.

- **`FallAnimator` hamle listesi.** Silinen slotun yerine son eleman çekiliyor; `Tick` bu yüzden
  **geriye doğru** dönüyor. Öne çekilen eleman o karede zaten işlenmiş olur → ne atlanır ne iki kez
  işlenir.

- **Shuffle animasyonunda ölçek blok başına yazılır**, `BoardView`'un transform'una değil.
  Ölçeklenmiş bir parent "1 birim = 1 hücre" varsayımını bozar (Karar 10), ve 0 ölçekte bir çocuğun
  world pozisyonunu ayarlamak sıfıra bölmedir.

- **`FitCamera` dolgusu eksende asimetrikti.** `cameraPadding` karşılaştırmadan *sonra*
  `orthographicSize`'a ekleniyordu, yani her zaman yarım-yükseklik biriminde. Dikey ekranda yatay
  karşılığı `padding * aspect`'e düşüyor (0.5 → 0.28) ve tahta çerçevesi kadrajdan taşıyordu. Dolgu artık
  her iki ihtiyaca `max`'tan **önce** ekleniyor.

- **HUD uGUI kullanıyor, tahta kullanmıyor — ve bu bir çelişki değil.** Tahtayı uGUI'den uzak tutan
  sebep canvas rebuild'dir: her karede yüz blok hareket ederse canvas her kare yeniden kurulur. Hamlede
  bir kez değişen dört etiket tam ters durum. Olmayan bir maliyetten kaçınmak için metni sprite'la
  çizmek kargo kültü olurdu. HUD ayrı bir Canvas'ta duruyor, o rebuild başka hiçbir şeye dokunmuyor.

---

## Dokümandaki tutarsızlıklar

README'ye yazılacak — dokümanın gerçekten okunduğunu gösterir.

1. **Sütun sayısı:** Metin "2 to 10 columns" diyor, ama Örnek 1'de `N = 12`.
   → **1. sayfadaki kısıtlar (2–10) referans alınır.** Kod config'ten gelen değeri sabit sınır koymadan çalıştırır, böylece iki yorum da karşılanır. *(Örneklerdeki sayıların amacı blastable/non-blastable ayrımını göstermek, kesin değer vermek değil.)*

2. **C eşiği:** Örnek 1'de `C=9` yazıp "more than 10" deniyor. Örnek 2 tutarlı (`C=8`, "more than 8").
   → **Doğru kural `> C`**, Örnek 1'de yazım hatası var.

3. **Box hasarı grup başına, blok başına değil.** Doküman: *"It gets 1 damage if an adjacent group is blasted."*
   → 5'lik bir grubun 3 bloğu aynı Box'a komşuysa Box **1** hasar alır, 3 değil.

---

## Dokümandaki kısıtların tam listesi

- Minimum grup boyutu: **2**
- **K** (renk sayısı): 1–6, her renk farklı ikon
- **M** (satır): 2–10, **N** (sütun): 2–10
- İkon eşikleri: grup > A → 1. ikon, > B → 2. ikon, > C → 3. ikon, aksi halde default
- Yeni bloklar tahtanın **dışında** üretilir, ilgili sütunun üstünden düşer
- **Box Obstacle:** gravity yok, üstündeki blokların düşmesini de engeller, 2 can, komşu grup patlayınca 1 hasar
- Düşenler varken duranlar patlatılabilmeli
- Deadlock tespiti + **kör olmayan** shuffle
- Unity, 3rd party serbest, Library hariç zip

---

## Box hasarı — uygulama (damga tekniği burada gerçekten kullanılıyor)

Karar 3'te "bu ölçekte gereksiz" denen damga tekniği burada tam yerine oturuyor:

```csharp
blastStamp++;                                   // her blast yeni damga
foreach (int cell in group) {
    foreach (int nb in Neighbors(cell)) {
        if (cells[nb].Type != CellType.Box) continue;
        if (boxStamp[nb] == blastStamp) continue;   // bu blast'ta zaten hasar aldı
        boxStamp[nb] = blastStamp;
        cells[nb].Health--;
    }
}
```
`HashSet` yok, allocation yok, temizlik yok. Grup 5 blokla aynı Box'a dokunsa bile hasar 1.

---

## Hamle akışı — SIRA ÖNEMLİ

1. Grubu patlat
2. Komşu Box'lara hasar uygula (**grup başına 1**)
3. Gravity + yeni blok üretimi
4. `RecalculateGroups()`
5. Hamle sayacını artır
6. **Kazandın mı?** (Box kalmadı mı)
7. **Kaybettin mi?** (hamle bitti ve Box kaldı)
8. **Deadlock mı?** → shuffle → çözülemiyorsa yenilgi

**Neden bu sıra:**
- **6 mutlaka 7'den önce.** Son hamlede son Box kırılırsa hem "Box kalmadı" hem "hamle bitti" doğrudur. Ters sırada oyuncu kazandığı hamlede kaybeder.
- **8 en sonda.** Kazanılmış bir tahtada shuffle animasyonu oynatmanın anlamı yok.

---

## Test planı — 9 test

**Ölçüt:** *"Bunu sessizce kırarsam fark eder miyim?"* Kırıldığında ekranda hemen belli olan şeyleri test etmenin değeri düşük; sessiz bozulanlar değerli.

| # | Test | Neden değerli |
|---|---|---|
| 1 | **İkon eşikleri, sınır değerlerinde** (`A`, `A+1`, `B`, `B+1`, `C`, `C+1`) | Off-by-one buranın klasik hatası, **doküman bu konuda kendisi tutarsız.** Listedeki en değerli test. |
| 2 | **Box hasarı grup başına** (5'lik grubun 3 bloğu aynı Box'a komşu → hasar tam 1) | Yanlışsa Box iki kat hızlı kırılır ve **gözle asla fark edilmez** |
| 3 | **Minimum grup 2** (tek blok patlamaz) | Temel kural |
| 4 | **Komşuluk ortogonal** (çapraz aynı renk gruba dahil değil) | Flood fill'de kolay yapılan hata |
| 5 | **Box gravity'yi bölüyor** (üstündekiler altına geçmiyor, altında kalıcı boşluk) | Sütun segment mantığını kanıtlar |
| 6 | **Shuffle en az bir grup üretiyor** — sabit tohumla değil, **~200 farklı tohumla** | Garanti adımının gerçekten *garanti* olduğunu ancak böyle kanıtlarsın |
| 7 | **Shuffle renk sayılarını koruyor** | Takas yerine üzerine yazma hatasını **başka hiçbir şey yakalamaz** |
| 8 | **Kazanma/kaybetme sırası** (son hamlede son Box → kazandın) | Yukarıdaki sıra hatasını kilitler |
| 9 | **Satır sarması yok** (satır sonundaki hücre, bir üst satırın başındakiyle aynı gruba girmiyor) | Karar 15'te işaretlendi: 1D index'te `±1` satır sınırını aşar. Test 4 çaprazı kontrol ediyor, bu hata **yatay** — hiçbir mevcut test yakalamıyor. Tahtanın ortasında her şey doğru görünüyor. |

**Bilinçli yazılmayanlar:** deadlock tespiti (6 zaten kapsıyor), boş tahta, geçersiz config, animasyon süresi, tıklama koordinat çevrimi → ya başka bir testin yan ürünü ya da kırıldığında ekranda anında belli oluyor.

> **Uygulamada:** bu 9 **senaryo**, 5 dosyada **58 test metoduna** açıldı. Sayının şişmesi kapsam
> kayması değil: her senaryo sınır değerlerinde ve birden fazla tahta şeklinde ayrı ayrı iddia
> ediliyor (ikon eşikleri tek başına 6 sınır değeri, shuffle testleri ~200 tohum üzerinde koşuyor).
> Değerli olan sayı 58 değil, hâlâ **hangi 9 şeyin sessizce kırılabileceğini** bilmemiz.

**README cümlesi:** *"Core tamamen Unity'den bağımsız olduğu için kritik davranışlar 9 senaryo / 58 EditMode testiyle kilitlendi."*

---

## Kapsam kararları özeti

**Dahil:** blast mekaniği, ikon seviyeleri, Box Obstacle, gravity, deadlock + akıllı shuffle, hedef (tüm Box'ları kır), hamle limiti, skor, kazanma/kaybetme, ses, Box kırılma partikülü, object pooling, sprite atlas, 9 unit test, README + profiler ölçümleri.

**Hariç:** çoklu seviye/progression, çoklu dokunuş, level editor, ulaşılabilirlik analizi, özel bloklar (roket/bomba), zincirleme kombolar, kayıt/yükleme, lokalizasyon.

**Ses kaynakları (CC0 / ücretsiz ticari):**
- **Kenney.nl** — tamamen CC0, atıf gerekmez. Interface Sounds, Impact Sounds, Digital Audio paketleri birebir uygun.
- freesound.org (CC0 filtresiyle), Mixkit, OpenGameArt.org

Gerekli ses ~5: blast (grup boyutuna göre 2-3 varyant), Box hasarı, Box kırılması, blok yere düşme, shuffle.
Unity import ayarı: `Load Type: Decompress on Load` + `Force to Mono` → README'ye bellek maddesi.
