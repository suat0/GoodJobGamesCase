# ROADMAP — Uygulama Planı ve Commit Sırası

Karar gerekçeleri: `DECISIONS.md` · Proje bağlamı: `CLAUDE.md`

**İlke:** Core önce bitirilip test edilir. Unity'de hiçbir şey görünmezken mantığın doğruluğundan emin oluruz; sonra View yazılırken bir bug çıkarsa **nerede olmadığını** biliyoruz.

**Commit formatı:** Conventional Commits (`feat`, `test`, `chore`, `perf`, `docs`, `fix`)
Her commit **tek başına derlenmeli** ve anlamlı bir birim olmalı.

---

## Faz 0 — İskelet

Amaç: proje ayakta, mimari sınırlar derleyici tarafından zorlanıyor.

### `chore: unity project setup and folder structure`
- Unity 2022 LTS (veya güncel LTS), 2D template
- `Assets/Scripts/{Core,Game}`, `Assets/Tests/EditMode`, `Assets/{Art,Audio,Prefabs,Scenes}`
- `.gitignore` (Unity şablonu — `Library/`, `Temp/`, `Logs/`, `Build/` hariç)

### `chore: assembly definitions with engine-free core`
- `BlastGame.Core.asmdef` → **"No Engine References" işaretli** ✔
- `BlastGame.Game.asmdef` → Core'a referans
- `BlastGame.Tests.asmdef` → Core'a referans, `nunit.framework` + Editor platform
- **Doğrulama:** Core'a geçici `using UnityEngine;` ekle, derleme hatası verdiğini gör, sonra sil

### `chore: import art assets and configure sprite atlas`
- 26 PNG → `Assets/Art/`
- Sprite Atlas oluştur, tüm sprite'ları ekle
- Import ayarları: `Point (no filter)` veya `Bilinear`, uygun `Pixels Per Unit` (hücre = 1 dünya birimi olacak şekilde)

### `feat(game): level config scriptable object`
- `LevelConfig`: `Rows`, `Cols`, `ColorCount`, `ThresholdA/B/C`, `BoxCount`, `MoveLimit`, `Seed`
- `OnValidate` ile aralık kısıtları (2–10, 1–6)
- Örnek asset: 10×10, K=6, A=4, B=7, C=9

**Faz 0 çıktısı:** Boş ama doğru yapılandırılmış proje.

---

## Faz 1 — Core: tahta ve grup bulma ✅

### `feat(core): cell data model and board container` ✅
- `Cell.cs`: `CellType` enum (`: byte`) + `Cell` struct + `MakeColor`/`MakeBox`/`Empty` factory'leri
- `Grid.cs`: saf index matematiği (`Index`, `RowOf`, `ColOf`, `TryStep`) — Karar 17
- `Board.cs`: `cells` dizisi, `Rows`/`Cols`, `Index`, `InBounds`, `TryNeighbor`, `CellAt`, `ToString()`
- **Yorum notları:** satır 0 = alt; struct kopya tuzağı; dizi indekslemede metot çağrısının kopya üretmediği
- `System.Random` constructor'dan enjekte edilir

### `feat(core): board generation with top-row box constraint` ✅
- **En üst satıra Box konmaz** → uygun hücreler `[0, (Rows-1)*Cols)` bitişik öneki, filtreleme gerekmiyor
- **Kısmi Fisher-Yates** ile tekrarsız yerleştirme (Karar 18)
- Renkler hücre başına düzgün rastgele (Karar 19)
- `BoxCount` kapasiteye kırpılır; hedef bu sayıyı okumaz → `RemainingBoxes()` tahtadan sayar (Karar 20)

### `feat(core): iterative DFS group finder` ✅
- `GroupFinder.cs`: `groupIdOf`, `groupSizes`, `stack` — **sınıf field'ı, bir kez alloc**
- `Recalculate(ReadOnlySpan<Cell>)` — tahtaya referans tutmaz (Karar 13/A2)
- `groupIdOf` **`-1` ile doldurulur** (`Array.Clear` değil — 0 geçerli bir grup id'si)
- **Push'ta işaretle, pop'ta değil** → stack boyutu `M*N` ile sınırlı
- `LargestGroupSize` bedava geliyor (deadlock tespiti Faz 3'te bunu okuyacak)
- LINQ ve recursion **yok**

### `feat(core): icon tier resolution from group size` ✅
- `BoardConfig.cs`: `readonly struct` + `Validate()` (Karar 21)
- `TierAt`: `> C` → 3, `> B` → 2, `> A` → 1, aksi → default — **saklanmaz, okunurken türetilir** (Karar 14)
- Eşikler `GroupFinder` constructor'ında; `Board` tek satır forward eder

### `test(core): group finding, adjacency and icon tiers`
- **Test 1:** ikon eşikleri sınır değerlerinde (`A`, `A+1`, `B`, `B+1`, `C`, `C+1`)
- **Test 3:** minimum grup 2 — tek blok patlamaz
- **Test 4:** komşuluk ortogonal — çapraz aynı renk gruba dahil değil
- **Test 9:** satır sarması yok — satır sonu, bir üst satırın başıyla birleşmiyor
- Test yardımcısı: string'den tahta kuran `BoardBuilder` (okunabilir test verisi)

**Faz 1 çıktısı:** Tahta kuruluyor, gruplar doğru bulunuyor, ikonlar çözülüyor, testler yeşil.
Unity'de hâlâ hiçbir şey görünmüyor.

> **Yan bulgu:** Core motordan bağımsız olduğu için üç dosya düz C# olarak (`mcs`/`dotnet`) Unity
> açılmadan derlenip çalıştırılabiliyor. Karar 1'in en somut faydası bu — README'ye girmeye değer.

---

## Faz 2 — Core: gravity ve Box Obstacle

### `feat(core): gravity resolver with box segmentation`
- `GravityResolver.cs`: her sütun Box'lar **duvar** kabul edilerek segmentlere bölünür
- Her segment kendi içinde çöker
- En üst segmente yeni bloklar üretilir (tahtanın dışından, sütunun üstünden)
- Box altındaki kalıcı boşluklar **doğru davranış** — koda yorum

### `feat(core): box obstacle damage with per-group stamping`
- Damga tekniği: `blastStamp++` / `boxStamp[nb]`
- Grup başına 1 hasar, blok başına değil
- `Health == 0` → hücre `Empty` olur

### `feat(core): blast result and board mutation`
- `BlastResult.cs`: silinen index'ler, taşınan bloklar (eski→yeni index), yeni doğanlar, hasar alan/kırılan Box'lar
- **Tek örnek, `Clear()` ile yeniden kullanılır** — yayın başına allocation yok
- `Board.Blast(index)`: patlat → hasar → gravity → recalc

### `test(core): gravity segmentation and box damage`
- **Test 2:** 5'lik grubun 3 bloğu aynı Box'a komşu → hasar tam 1
- **Test 5:** Box üstündekiler altına geçmiyor; altında kalıcı boşluk oluşuyor

**Faz 2 çıktısı:** Tam bir hamle Core'da uçtan uca çalışıyor.

---

## Faz 3 — Core: deadlock ve akıllı shuffle

### `feat(core): deadlock detection`
- `RecalculateGroups()` sonrası en büyük grup < 2 → deadlock
- Ayrı tarama yok, ek maliyet sıfır

### `feat(core): fisher-yates shuffle with guaranteed group`
- `DeadlockResolver.cs`
- Doğru Fisher-Yates (azalan aralık) — naif versiyona düşmeme
- **Garanti adımı:** komşu çift + en çok bulunan renk, **takas** ile yerleştirme (renk sayıları korunur)
- Garanti adımı **her zaman** çalışır (önce kontrol yok)
- Çözülemezlik: hiçbir renk 2+ değilse VEYA hiçbir iki renkli hücre komşu değilse → `false` döner

### `test(core): shuffle guarantee and color preservation`
- **Test 6:** ~200 farklı tohum, her seferinde geçerli grup oluştuğu doğrulanır
- **Test 7:** shuffle öncesi/sonrası renk sayıları birebir aynı

**Faz 3 çıktısı:** Core tamamen bitti, 8 test yeşil. Unity hâlâ açılmadı.

---

## Faz 4 — View: çizim ve pooling

### `feat(view): block pool with zero runtime instantiation`
- `BlockPool.cs`: başlangıçta `M*N + tampon` obje, `SetActive` ile alınıp bırakılır
- Oyun sırasında `Instantiate`/`Destroy` **yok**

### `feat(view): board renderer and camera fit`
- `BlockView.cs`: cache'lenmiş `SpriteRenderer`, `GetComponent` çağrısı yok
- `BoardView.cs`: Core durumunu çizer; sprite seçimi renk + ikon seviyesinden
- Kamera `orthographicSize` ile sığdırma (tahta ölçeklenmez)
- Tüm bloklar aynı sorting layer + order

### `chore(view): verify single draw call in frame debugger`
- Frame Debugger ekran görüntüsü alınır (README için)

**Faz 4 çıktısı:** Tahta ekranda, statik, tek draw call.

---

## Faz 5 — Input ve düşme animasyonu

### `feat(input): grid-math input handler`
- `ScreenToWorldPoint` → `FloorToInt` → hücre
- Sınır kontrolü; **collider yok**
- Eski `Input` API'si

### `feat(view): single-loop fall animator with settled tracking`
- `FallAnimator`: tek `Update`, aktif düşen blok listesi
- Sabit hız: `duration = mesafe / hız`, `t = elapsed / duration`, `IsSettled = t >= 1`
- Blok başına `Update` **yok**

### `feat(input): ignore clicks on unsettled blocks`
- **B2 filtresi** — case'in "duranlar her zaman patlatılabilmeli" maddesinin karşılığı
- Filtre View'da; Core "kim havada" bilmiyor

**Faz 5 çıktısı:** Oyun oynanabilir. Blast, düşme, yeni bloklar, ikon değişimi çalışıyor.

---

## Faz 6 — Oyun döngüsü ve UI

### `feat(game): game controller and turn flow`
- Hamle akışı **tam sırayla** (blast → hasar → gravity → recalc → sayaç → kazandın? → kaybettin? → deadlock?)
- Observer event'leri: `OnBoardChanged`, `OnDeadlockResolved`
- `OnEnable`/`OnDisable` abonelik, isimli metotlar

### `feat(game): objective, move limit and win/lose states`
- Hedef: tüm Box'lar kırıldı
- `BoxCount == 0` → hedef `null`, sınırsız hamle (**ayrı mod değil, opsiyonel veri**)
- Shuffle hamle **saymaz**
- Shuffle çözemezse → yenilgi

### `feat(ui): score, moves and objective hud`
- Skor (grup boyutuna göre artan), hamle sayacı, kalan Box sayısı
- Kazandın/kaybettin paneli + yeniden başlat

### `feat(view): shuffle animation`
- Küçül → sprite değiş → büyü (uçma animasyonu değil, gerekçe `DECISIONS.md` 9a)

### `test(core): win before lose ordering`
- **Test 8:** son hamlede son Box kırılıyor → kazandın

**Faz 6 çıktısı:** Tam oyun döngüsü.

---

## Faz 7 — Cila

### `feat(audio): sound effects`
- ~5 ses: blast (2-3 varyant), Box hasarı, Box kırılması, blok düşme, shuffle
- Kaynak: Kenney.nl (CC0)
- Import: `Decompress on Load` + `Force to Mono`
- `AudioController` `OnBoardChanged`'e abone

### `feat(vfx): box break particle`
- Sorting order blokların **üstünde** (batch bölünmesin)

**Faz 7 çıktısı:** Oyun bitmiş görünüyor.

---

## Faz 8 — Ölçüm ve teslim

### `perf: profiler verification and allocation fixes`
- Profiler: **GC Alloc = 0 B/frame** (oynanış sırasında)
- Frame Debugger: tahta tek draw call
- Ekran görüntüleri README'ye

### `docs: readme with architecture and performance rationale`
`CLAUDE.md`'deki "README'de mutlaka olması gerekenler" listesinin tamamı.

### `chore: final cleanup and build verification`
- Kullanılmayan kod/asset temizliği
- Farklı config'lerle test: 2×2, 10×10, K=1, K=6, Box=0, Box=çok
- Build alınıp çalıştığı doğrulanır
- **Zip: `Library/` hariç**

---

## Kontrol listesi (teslim öncesi)

- [ ] Core'da tek bir `using UnityEngine` yok (asmdef zorluyor)
- [ ] Testler yeşil — 9 senaryo, 57 test metodu (`DECISIONS.md` → Test planı)
- [ ] 2×2, 10×10, K=1, K=6, Box=0 konfigürasyonları çalışıyor
- [ ] Oynanış sırasında GC Alloc = 0
- [ ] Tahta tek draw call
- [ ] Düşen bloklar varken duran bloklar patlatılabiliyor
- [ ] Deadlock oluşturulup shuffle'ın tek geçişte çözdüğü görüldü
- [ ] Son hamlede son Box kırılınca kazanılıyor
- [ ] README tamam (elenen optimizasyonlar ve doküman tutarsızlıkları dahil)
- [ ] `Library/` hariç zip
