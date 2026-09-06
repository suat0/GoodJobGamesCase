# CLAUDE.md — Proje Bağlamı

Bu dosya Claude Code'un projeye sıfırdan girdiğinde bilmesi gereken her şeyi içerir.
Kararların **gerekçeleri** için `DECISIONS.md`, ilerleme planı için `ROADMAP.md`.

---

## Proje nedir

Good Job Games **junior game developer** pozisyonu için verilen case.
Unity ile **collapse/blast** (tile-matching) oyunu. Toon Blast / Lily's Garden / Pet Rescue Saga tarzı.

**Case'in açık odağı:** performans (bellek, CPU, GPU).
**Ek hedef:** aday hem öğrenmek hem iyi bir ürün çıkarmak istiyor. Best practice'lere uyulacak ama **proje gereğinden fazla karmaşıklaştırılmayacak.**

---

## Oyun kuralları (case dokümanından)

- Aynı renkli **2+** bitişik blok = patlatılabilir grup. Komşuluk **ortogonal** (çapraz sayılmaz).
- **K** renk sayısı: 1–6. Her renk farklı ikon.
- **M** satır: 2–10, **N** sütun: 2–10.
- Boşalan hücreler yukarıdaki bloklarla ve **tahtanın dışında üretilip sütunun üstünden düşen** yeni bloklarla dolar.
- **İkon seviyeleri:** grup boyutu `> C` → 3. ikon, `> B` → 2. ikon, `> A` → 1. ikon, aksi halde default.
- **Box Obstacle:** gravity'den etkilenmez, üstündeki blokların düşmesini de engeller, **2 can**, komşu bir grup patladığında **1** hasar alır (grup başına 1, blok başına değil).
- **Düşen bloklar varken duran bloklar her zaman patlatılabilmeli.**
- Deadlock tespit edilmeli ve **"kör shuffle" olmayan** bir çözüm uygulanmalı.

### Doküman tutarsızlıkları (bilinçli yorumlar)
1. Metin "2–10 sütun" diyor ama Örnek 1'de `N=12` → **1. sayfadaki kısıtlar (2–10) esas alınır.** Kod yine de config'ten gelen değeri sabit sınır koymadan çalıştırır.
2. Örnek 1'de `C=9` yazıp "more than 10" deniyor; Örnek 2 tutarlı → **doğru kural `> C`.**

---

## Mimari — değiştirilemez kurallar

### 1. Core saf C#, Unity'den tamamen bağımsız
`Assets/Scripts/Core/` altındaki hiçbir dosyada `using UnityEngine` **olmayacak**.
Bu bir konvansiyon değil, `.asmdef`'te **"No Engine References"** ile derleyici tarafından zorlanıyor.
`UnityEngine.Random` de yasak → rastgelelik `System.Random` olarak **dışarıdan enjekte edilir**.

### 2. Mantık anında çözülür, animasyon geriden gelir
`Blast()` çağrıldığı anda Core tahtayı **son hâline** getirir: silinenler, düşenler, yeni bloklar, gruplar, deadlock — hepsi hesaplanır. Tahta hiçbir zaman tutarsız bir ara durumda kalmaz.
View blokları eski konumlarından yeni konumlarına animasyonla taşır. **Animasyon tamamen kozmetiktir.**

### 3. "Oturmuş blok" filtresi View'da yaşar
Tıklanan hücredeki **görsel** blok hâlâ animasyondaysa tıklama yutulur.
Core "kim havada" bilgisini **hiç taşımaz.** Bu tek görsel temas noktası bilinçli olarak View'dadır.

### 4. Sadelik disiplini
- **Event bus yok, DI container yok, command pattern yok, gereksiz interface yok**
- **Yeni bir Core tipi ancak üç şartı birden geçerse açılır** (Karar 17):
  (a) durumu yok ya da kendi durumunun tek sahibi, (b) birden fazla çağıranı var **ve** paylaşamıyorlar,
  (c) adı gerçek bir kavram. `Utils`/`Helpers`/`Manager` adı koymak zorunda kalıyorsan ortada kavram yok.
  *(Bu madde eskiden "Core en fazla 4-6 dosya" idi; dosya sayısı yanlış şeyi ölçüyordu.)*
- Interface sadece gerçekten ikinci bir implementasyon olacaksa yazılır
- **LINQ yok** (enumerator allocation → GC)
- Oyun sırasında **sıfır allocation** hedefi

---

## Veri modeli

```csharp
public enum CellType : byte { Empty = 0, Color = 1, Box = 2 }

public struct Cell {
    public CellType Type;
    public byte Color;    // sadece Type == Color iken anlamlı
    public byte Health;   // sadece Type == Box iken anlamlı
}

Cell[] cells;                              // 1D, M*N
int Index(int r, int c) => r * Cols + c;   // AggressiveInlining
```

`Cell` yalnızca iki meşru şekle sahip; constructor yerine **factory** kullanılır
(üç alanı alan bir constructor "renkli Box" gibi imkânsız kombinasyonlara izin verirdi):
```csharp
Cell.MakeColor(byte color)
Cell.MakeBox(byte health = Cell.BoxMaxHealth)
Cell.Empty                                 // == default(Cell)
```

### `BoardConfig` — Core'un kurulum verisi
```csharp
public readonly struct BoardConfig {
    Rows, Cols, ColorCount, ThresholdA, ThresholdB, ThresholdC, BoxCount
    public void Validate();   // fırlatır
}
```
`Board(BoardConfig, Random)`, `GroupFinder(BoardConfig)`, `Generate()` parametresiz.
`LevelConfig` (ScriptableObject) Core'a **giremez**; değerlerini `BoardConfig`'e kopyalar —
Core ↔ Game arasındaki tek dönüşüm noktası. `MoveLimit` ve `Seed` burada **yok** (tahtanın şeklini
belirlemiyorlar).

### `Grid` — saf index matematiği
`Grid.Index/RowOf/ColOf/TryStep`, durumsuz. Hem `Board` hem `GroupFinder` kullanır.
**`TryStep` sınırı `(r, c)` üzerinden kontrol eder**; 1D index'te `±1` satır sınırını aşıp
satır sonunu bir üst satırın başına bağlar.

### ⚠️ Kritik konvansiyonlar
- **Satır 0 = tahtanın ALT satırı.** Unity world Y yukarı arttığı için (`worldY = origin.y + row * cellSize`). Gravity index azalan yöne düşer, yeni bloklar en yüksek index'ten girer.
- **Struct kopya tuzağı:**
  ```csharp
  var c = cells[i]; c.Color = 2;   // YANLIŞ — kopya, diziye yazmaz
  cells[i].Color = 2;              // DOĞRU
  ```
- Boş hücre için sentinel renk **yok** — ayrı `CellType.Empty` kullanılır (Box'ın rengi olmadığı için bu şart).
- `groupIdOf`, `groupSizes`, `stack`, `boxStamp` dizileri **sınıf field'ı, bir kez alloc.** Her taramada yeniden ayrılmaz.
- **`groupIdOf` `-1` ile doldurulur, `Array.Clear` ile değil** — 0 geçerli bir grup id'si, "grup yok" ondan ayrı bir değer olmak zorunda.
- **`GroupFinder` tahtaya referans TUTMAZ.** `Recalculate(ReadOnlySpan<Cell>)` ile veriyi her çağrıda alır; sadece kendi scratch dizilerinin sahibidir (Karar 13/A2).

---

## Klasör yapısı

```
Assets/
  Scripts/
    Core/                    <- asmdef: No Engine References ✔
      Cell.cs, Grid.cs, BoardConfig.cs, Board.cs,
      GroupFinder.cs, GravityResolver.cs, DeadlockResolver.cs, BlastResult.cs
    Game/                    <- asmdef: Core'a bağımlı
      LevelConfig.cs, GameController.cs, BoardView.cs,
      BlockView.cs, BlockPool.cs, InputHandler.cs, AudioController.cs
      UI/
  Tests/EditMode/            <- asmdef: Core'a bağımlı
  Art/                       <- 26 PNG + Sprite Atlas
  Audio/  Prefabs/  Scenes/
```

**Sprite'lar (hazır):** `{Renk}_Default.png`, `{Renk}_A.png`, `{Renk}_B.png`, `{Renk}_C.png` — Blue, Green, Pink, Purple, Red, Yellow. Ayrıca `Box0.png` (2 can) ve `Box1.png` (1 can, hasarlı).

---

## Hamle akışı — bu sıra değiştirilmemeli

1. Grubu patlat
2. Komşu Box'lara hasar uygula (**grup başına 1**)
3. Gravity + yeni blok üretimi
4. `RecalculateGroups()`
5. Hamle sayacını artır
6. **Kazandın mı?** (Box kalmadı mı)
7. **Kaybettin mi?** (hamle bitti ve Box kaldı)
8. **Deadlock mı?** → shuffle → çözülemiyorsa yenilgi

**6 mutlaka 7'den önce:** son hamlede son Box kırılırsa oyuncu kazanır, kaybetmez.
**8 en sonda:** kazanılmış tahtada shuffle animasyonu anlamsız.

---

## İnvariant — tahta asla bayat grup verisiyle görülmez

Tahtayı değiştiren **her** metot `RecalculateGroups()` çağırarak biter (`Generate()` dahil).
Çağıranın hatırlaması gereken bir adım yok.

---

## Algoritma notları

### Grup bulma
Her değişiklikten sonra **tam tarama** (100 hücre, mikrosaniyeler). Tek `RecalculateGroups()` üç işi görür: patlatılabilirlik, ikon seviyeleri, deadlock tespiti (en büyük grup < 2).
**Iterative DFS**, kendi `int[] stack` + `top` index'i ile. Recursion yok, `Queue<T>` yok.

⚠️ **Hücre push edilirken işaretlenir, pop edilirken değil.** Pop'ta işaretlenseydi aynı hücreyi
dört komşusu da itebilirdi ve stack hücre sayısını aşabilirdi. Push'ta işaretleme her hücrenin en
fazla bir kez itilmesini garanti eder → `stack` boyutu tam olarak `M*N`.

### Box hasarı — damga tekniği
```csharp
blastStamp++;
foreach (int cell in group)
    foreach (int nb in Neighbors(cell)) {
        if (cells[nb].Type != CellType.Box) continue;
        if (boxStamp[nb] == blastStamp) continue;   // bu blast'ta zaten hasar aldı
        boxStamp[nb] = blastStamp;
        cells[nb].Health--;
    }
```
`HashSet` yok, allocation yok, temizlik yok.

### Gravity
Her sütun, Box'lar **duvar** kabul edilerek segmentlere bölünür. Her segment kendi içinde çöker.
Box'ın altındaki hücreler yukarıdan dolamaz → **kalıcı boşluk** oluşabilir. Bu doğru davranıştır, bug değil.

### Deadlock shuffle (case'in ayırt edici maddesi)
**Kör shuffle YASAK.** İki aşamalı, tek geçiş, sonlanma garantili:
1. Tüm renkli hücrelerin renklerini topla → **Fisher-Yates** ile karıştır → geri yaz
2. **Garanti adımı:** komşu iki renkli hücre `(i, j)` ve en çok bulunan renk `X` seç. `i` ve `j`'ye `X` rengini **takas ederek** yerleştir (üzerine yazarak değil → renk sayıları korunur).

Garanti adımı **her zaman** çalışır (önce "grup oluştu mu" kontrolü yapılmaz) → tek kod yolu, determinizm.

**Fisher-Yates doğru hâli:**
```csharp
for (int i = n - 1; i > 0; i--) { int j = rng.Next(0, i + 1); Swap(i, j); }
```
Naif `Swap(i, rng.Next(0, n))` yanlı dağılım üretir (`n^n` yol / `n!` permütasyon, tam bölünmez).

**Çözülemezlik koşulu (kesin):** (a) hiçbir renk 2+ kez bulunmuyor, VEYA (b) hiçbir iki renkli hücre komşu değil → shuffle imkânsız → **seviye kaybedildi.**

### Tahta üretimi
Kısıtlı rastgele. **Kural: en üst satıra Box konmaz.**
Bu tek kural şunları garantiler: her sütun yukarıdan blok alabilir → en üst satır her zaman dolu → her zaman ≥2 komşu renkli blok var → shuffle her zaman çalışır → tahta asla ölmez.
*(Bu kısıt dokümanda yok, bilinçli eklendi. Gerekçe `DECISIONS.md` Karar 8'de.)*

---

## Render ve input

- **`SpriteRenderer` + Sprite Atlas** → tahtanın tamamı **tek draw call**. Unity UI/Canvas kullanılmayacak (canvas rebuild maliyeti).
- Tüm bloklar aynı sorting layer + aynı order. `sharedMaterial`'a dokunma.
- **Partikül ve arka plan blokların katmanının dışında olmalı** (bloklar 0, partikül 10, arka plan negatif). Araya girerse batch bölünür.
- **Object pooling:** oyun başında `M*N + tampon` obje üretilir. Oyun sırasında **sıfır `Instantiate`/`Destroy`.**
- **Ekrana sığdırma kamerayla** (`orthographicSize`), tahtayı ölçekleyerek değil → 1 birim = 1 hücre kalır, ölçek hiçbir hesaba sızmaz.
  ```csharp
  camera.orthographicSize = Mathf.Max(rows * 0.5f, cols * 0.5f / camera.aspect) + padding;
  ```
- **Input collider/raycast ile DEĞİL, matematikle:** `ScreenToWorldPoint` → `FloorToInt((w - origin))`. Tahtada tek collider yok.
- **Eski `Input` API'si** (yeni Input System'in kurulum maliyeti bu ihtiyaç için gereksiz).
- **Düşme animasyonu sabit hız:** `duration = mesafe / hız`, `t = elapsed / duration`, `Lerp(start, target, t)`, `IsSettled = t >= 1`.
- **Tek `Update` döngüsü** (`FallAnimator`) tüm düşen blokları yönetir. Blok başına `Update` yok.

---

## Event'ler (Observer)

```csharp
public event Action<BlastResult> OnBoardChanged;  // BoardView, AudioController, ScoreController
public event Action OnDeadlockResolved;           // BoardView, AudioController
```

**Kurallar:**
1. `OnEnable` += / `OnDisable` -= (`Start`/`OnDestroy` değil — obje kapanıp açılırsa çift abonelik)
2. **Lambda ile abone olma** — sökülemez + closure allocation. Her zaman isimli metot.
3. `BlastResult` **tek örnek**, listeler `Clear()` ile yeniden doldurulur. Dinleyici veriyi saklamaz, o an tüketir.
4. Event yüzeyi küçük tutulur — "her ihtimale karşı" event eklenmez.

---

## Kapsam

**Dahil:** blast, ikon seviyeleri, Box Obstacle, gravity, deadlock + akıllı shuffle, hedef ("tüm Box'ları kır"), hamle limiti, skor, kazanma/kaybetme ekranı, ses, Box kırılma partikülü, pooling, sprite atlas, 9 unit test, README + profiler ölçümleri.

**Hariç:** çoklu seviye/progression, çoklu dokunuş, level editor, ulaşılabilirlik analizi, özel bloklar (roket/bomba), zincirleme kombo, kayıt/yükleme, lokalizasyon.

**Box = 0 durumu** (dokümanın iki örneğinde de böyle): hedef `null`, hamle limiti sınırsız, kazanma/kaybetme yok — sadece skor.
Bu **ayrı bir mod değil**; hedef bir veridir, kod dalı değil.

---

## `LevelConfig` alanları

`Rows`, `Cols`, `ColorCount`, `ThresholdA`, `ThresholdB`, `ThresholdC`, `BoxCount`, `MoveLimit`, `Seed`
*(`Seed`: testler ve "aynı tahtayı tekrar üret" ile debug kolaylığı için.)*

---

## Kod stili

- Türkçe konuşuluyor ama **kod ve yorumlar İngilizce**
- Yorumlar "ne yaptığını" değil **"neden böyle yapıldığını"** anlatır
- Kritik invariant'lar kod içinde yorumla işaretlenir (satır yönü, struct kopya tuzağı, event yaşam döngüsü)
- Erken optimizasyon yok; ama **maliyeti sıfır olan doğru tercih her zaman yapılır**

---

## README'de mutlaka olması gerekenler

1. Mimari özeti ve **Core'un neden Unity'den bağımsız** olduğu
2. Deadlock çözümünün **tek geçiş, sonlanma garantili** olduğu + kesin çözülemezlik koşulu
3. Performans kararları ve **ölçümler** (Profiler'da GC Alloc = 0/frame, Frame Debugger'da draw call)
4. **Değerlendirilip elenen** optimizasyonlar: SoA, bit packing, incremental grup hesabı, GPU instancing, `visitedStamp` — hepsi "bu ölçekte gerekçesi yok" notuyla
5. Doküman tutarsızlıkları ve nasıl yorumlandığı
6. **"En üst satıra Box konmaz"** kısıtının gerekçesi (dokümanda yok, biz ekledik)
7. Kalıcı boşluk davranışı ve neden yapısal kilit oluşturmadığı
7b. Core'un Unity olmadan derlenip çalıştırılabildiği (motordan bağımsızlığın somut kanıtı)
8. Kapsam dışı bırakılanlar ve nedenleri

> **Ton notu:** "Performansı önemsedim" demek yerine, *"bu ölçekte gerekmiyor ama şu ölçekte gerekirdi, maliyeti de sıfırdı"* demek çok daha güçlü. Elenen optimizasyonları yazmak, gereksiz olanları uygulamaktan daha iyi bir sinyaldir.
