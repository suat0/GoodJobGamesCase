# Kararlar

Bu dosya, projedeki her önemli tercihin **elenen alternatiflerini ve eleme gerekçelerini** tutuyor.
Mimarinin kendisi `ARCHITECTURE.md`'de, üretime çıksa ne eklenirdi sorusu `FUTURE_WORK.md`'de.

Buradaki kararların çoğu ekleme değil **eleme** yönünde. Sebebi tek bir bağlam notunda toplanabilir:

> Tahta en fazla 10×10 = **100 hücre**. Bu ölçekte optimizasyonların çoğu ölçülebilir fark yaratmaz.
> Dolayısıyla bir seçeneği "daha hızlı" diye seçmek çoğu zaman kendini kandırmaktır; **aynı sonuca
> daha pahalı yoldan gitmek optimizasyon değildir.** Aşağıdaki eleme gerekçelerinin çoğu bu kuraldan
> türüyor.

---

## Mantık ve görsel ayrımı

### Seçenekler

| | Yaklaşım | Eksi |
|---|---|---|
| A | Her blok kendini yönetir (GameObject merkezli) | Tahtanın gerçeği sahnede. Düşme sırasında tutarlı durum yok, test edilemez, 100 ayrı `Update` |
| B | Merkezi `BoardManager` + aptal bloklar | Mantık hâlâ `UnityEngine`'e bağımlı, unit test zor |
| C | Saf C# Core + ince View | Daha fazla dosya, index'lerle çalışma, **aşırı soyutlama riski** |

**A elendi**, ve sebebi performans değil: case'in en zor maddesiyle doğrudan çatışıyor —
*"Players should be able to blast stationary blocks at all times while other blocks are falling."*
Animasyon tahtanın gerçeğiyse, animasyon sürerken tahtanın tutarlı bir durumu yok demektir. A ile
çözülebilir ama sürekli özel durum yamalarıyla.

**B elenmedi, savunulabilir.** Gerçek bir stüdyoda çoğu takım B ile gider ve haklıdır. Tek eleme
sebebi test edilebilirlik.

**Seçilen: C**, ama sıkı bir sadelik disipliniyle — aksi halde C'nin maliyeti (soyutlama şişkinliği)
faydasını yer. Somut sınırlar: event bus yok, DI container yok, command pattern yok, ve interface
ancak gerçekten ikinci bir implementasyon olacaksa yazılır.

---

## Grup hesabı: ne zaman, ne kadar

### Seçenekler
- **A:** Sadece tıklanınca, tıklanan hücreden flood fill
- **B:** Her değişiklikten sonra tam tarama, sonucu sakla
- **C:** Kirli bölge takibi (incremental)

**C elendi.** "Hangi bölge etkilendi" sorusunun cevabı sanılandan geniş: bir sütun düştüğünde yan
sütunlardaki gruplar birleşebilir veya bölünebilir, zincirleme yayılır. 100 hücrelik tam tarama
mikrosaniyeler sürerken, ölçülemez bir kazanç için gerçek bir bug riski satın alınmaz.

**A elendi — ve sebebi performans DEĞİL, oyun kuralı.** Case her bloğun ikonunun ait olduğu grubun
boyutuna göre değişmesini istiyor. Yani **tıklama olmasa bile** tahtadaki tüm grup boyutları her an
bilinmek zorunda. A bunu karşılayamaz.

> Mimariyi burada performans değil, gözden kaçması kolay bir gereksinim belirledi.

**Seçilen: B.** Tek `RecalculateGroups()` üç işi birden görüyor: patlatılabilirlik, ikon seviyeleri, ve
**bedavaya gelen deadlock tespiti** — en büyük grup 2'den küçükse deadlock, ekstra tarama maliyeti
sıfır.

Flood fill iterative DFS, kendi `int[] stack`'iyle. BFS ile hız farkı yok (bağlı bileşenin tamamı
gezilmek zorunda, erken çıkış yok); karar sadeliğe düştü — DFS'in kabı bir dizi ve bir index, BFS'te
`Queue<T>` allocation getirir.

### Elenen: `visitedStamp`

Her taramada diziyi temizlemek yerine artan bir damga sayacı kullanılabilirdi. **Bu ölçekte yazmaya
değmez** — 100 elemanlık temizlik zaten bir `memset`. *Ama aynı teknik Box hasarında gerçekten
kullanılıyor, çünkü orada temizlenecek şey her hamlede değişiyor.*

---

## Core ↔ View iletişimi

### Seçenekler
- **A:** Doğrudan çağrı + dönüş değeri
- **B:** C# `event` / Observer
- **C:** Global event bus

**C elendi.** Global mesaj altyapısının değeri çok sayıda birbirini tanımayan sistem varken doğar;
burada iki taraf var ve birbirlerini gayet iyi tanıyorlar. Derleyici hiçbir şeyi doğrulayamaz.

### A ile B arasındaki asıl nüans

**Event'ler bağımlılığı kaldırmaz, derleyicinin göremediği bir yere taşır.**

- A'da ok tek yönlü. Core, View'ın varlığından haberdar değil — ne referans, ne interface, ne
  delegate. Core'u konsol uygulamasına, sunucuya, teste taşıyabilirsin.
- B'de Core hâlâ View'ı tanımıyor ama **kimin dinlediğini bilmediği bir sözleşme yayınlıyor.**
  `BlastResult`'ın şekli değişirse View yine kırılır.

DIP'in tam ifadesi "bağımlılık kurma" değil, *"üst seviye modül alt seviyeye bağımlı olmasın"*. Burada
üst seviye Core, alt seviye View, ve Core zaten View'a bağımlı **değil** → DIP'in koruduğu şey A ile de
sağlanıyor.

**Seçilen: B**, ve gerekçesi mimari değil pratik: dört ayrı dinleyici var (`BoardView`, `HudView` ve
ikisinin alt sistemleri) ve hepsi aynı olaya farklı tepki veriyor.

> **Kalibrasyon:** object pooling ile observer bu projede **aynı ağırlıkta değil.** Pooling'in somut
> ve ölçülebilir gerekçesi var. Observer savunulabilir bir stil tercihi, ve ikisi aynı dille
> anlatılmamalı.

Kötü kurulmuş bir event sistemi hiç kurmamaktan kötü olduğu için üç kural: `OnEnable`/`OnDisable`
(`Start`/`OnDestroy` değil — obje kapanıp açılırsa çift abonelik), lambda yerine isimli metot
(lambda sökülemez), ve tek `BlastResult` örneği (dinleyici veriyi o an tüketir, saklamaz).

---

## Assembly sınırı

### Seçenekler
- **A:** Tek assembly (Unity varsayılanı)
- **B:** Üç assembly: Core, Game, Tests
- **C:** Granüler (Core/View/Input/UI ayrı)

**C elendi.** 5-6 asmdef, aralarında referans grafiği, ve her yeni sınıfta "bu hangi assembly'ye ait"
sorusu. View, Input ve UI arasında engellemek istediğimiz bir bağımlılık yok → ayırmak sadece sürtünme
üretir.

**A elendi, ve sebebi derleme hızı değil: niyetin uygulanabilir olmaması.** "Core saf kalacak" A'da
sadece bir niyet — yorgun bir anda `Debug.Log` yazarsın, derlenir, kimse fark etmez, kural erozyona
uğrar.

**Seçilen: B.** Kilit mekanizma `.asmdef`'teki **No Engine References**: işaretlendiğinde Core'da
`using UnityEngine` yazmak derleme hatası verir. Mimari karar bir konvansiyon olmaktan çıkıp
**derleyicinin zorladığı bir kural** haline gelir.

Aynı kural `UnityEngine.Random`'ı da yasaklıyor → rastgelelik `System.Random` olarak dışarıdan
enjekte ediliyor, ve testler sabit tohumla deterministik hale geliyor.

> Bir sınıf kendi rastgeleliğini, zamanını veya dosya erişimini kendisi yaratmasın. Bunlar dışarıdan
> gelmeli, yoksa test edilemezler.

---

## `GroupFinder` veriye nasıl erişir

Flood fill'in üç geçici diziye ihtiyacı var ve bunlar bir kez alloc edilip yeniden kullanılacak. Yani
**geçici durumun kalıcı bir eve ihtiyacı var.** Soru o evin nerede olduğu.

| | Yaklaşım | Eksi |
|---|---|---|
| A1 | Constructor'da `Cell[]` alır, Board ile aynı diziyi paylaşır | İki nesne aynı diziye referans tutar; "Board bu diziyi asla yeniden atamaz" yazısız bir sözleşme |
| A2 | Constructor sadece boyutları alır, veri her çağrıda `ReadOnlySpan<Cell>` gelir | `rows`/`cols` iki yerde yaşıyor |
| B | `static` metotlar | **Çalışmaz** — scratch dizilerinin yaşayacak yeri yok; static field global durum olur, testler birbirini bozar |
| C | Grup bulma `Board.cs` içine gömülür | Board hem veri sahibi hem algoritma olur |

**Seçilen: A2.** Tutulan referans yok → bayatlayamaz, ve `ReadOnlySpan` "okurum, yazmam"ı **tip
seviyesinde** söyler. A2'nin tek açığı (`rows`/`cols` ile verinin ayrışabilmesi) bir
`[Conditional("UNITY_ASSERTIONS")]` assert ile kapatıldı — release build'de tamamen kayboluyor.

Bu desenin adı **caller-owned workspace**: veriyi sahibi tutar, algoritma kendi çalışma alanını tutar,
ve çalışma anında hiç allocate etmez. Recast'in `dtNavMeshQuery`'si, Box2D'nin çözücüleri ve LAPACK'in
`WORK` dizisi aynı desen. Hepsi A1 şeklinde, çünkü C/C++'ta ödünç referansı ifade edecek tek yol yorum
yazmak; C#'ta `ReadOnlySpan` varken kullanmamak için sebep yok.

---

## Yeni bir tip ne zaman açılır

Komşuluk matematiğinin iki müşterisi çıktı — `Board` (Box hasarı) ve `GroupFinder` (flood fill). Ama
`GroupFinder`, `Board`'a referans tutmuyor.

Seçenekler: her ikisi kendi 4'lük döngüsünü yazsın (aynı off-by-one iki kopyada), `GroupFinder`
`Board`'a bağlansın (yukarıdaki kararı geri alır), ya da ayrı bir `Grid` tipi.

**Seçilen: ayrı `Grid`** — ve bu vesileyle o zamana kadar kullandığım ölçüt değişti. Başlangıçta
"Core en fazla 4-6 dosya" gibi bir sınır vardı; `Grid` eklenince o sınırın **yanlış şeyi ölçtüğü**
ortaya çıktı. Amaç soyutlama şişkinliğini önlemekti, ama `Grid` bunların hiçbiri değil: 25 satır,
durumsuz, dört saf fonksiyon, ve doğrudan test edilebilir. Sayı yerine geçen ölçüt:

> **Bir tipi ayırmak için üç şart birden gerekir:**
> 1. Durumu yok, ya da kendi durumunun tek sahibi
> 2. Birden fazla çağıranı var **ve** yapısal olarak paylaşamıyorlar
> 3. Adı gerçek bir kavram. `Helpers`, `Utils`, `Manager` gibi bir ad koymak zorunda kalıyorsan ortada
>    kavram yok, sadece kod taşınmış demektir.

Üçüncü madde en işe yarayanı: **adın kendisi testtir.**

İkinci şart mekanik değil. `DeadlockResolver` de Fisher-Yates kullanıyor, yani ikinci çağıran var —
ama ortak bir `Shuffle<T>` çıkarmadık. Fark: `Grid`'de paylaşılan şey **hata üretmesi kolay** bir
formüldü. "İki çağıranı var" tek başına yetmiyor, **"paylaşmamak risk üretiyor mu"** da sorulmalı.

---

## Kazanılamaz tahta

### Önce bir düzeltme kaydı

İlk gerekçem yanlıştı. *"Aynı sütunda iki Box varsa aradaki hücre kalıcı boş kalır, alttaki Box dört
tarafından ölü hücreyle çevrilir ve asla kırılamaz"* diye yazmıştım. Hata: üstteki Box'ın kendisinin
de kırılabilir olduğunu hesaba katmamıştım.

**Doğru tümevarım:** bir sütundaki *en üstteki* Box'ın üstünde onu tıkayan hiçbir şey yok → üstü her
zaman dolar → her zaman kırılabilir. Kırılınca sıradaki Box en üste geçer, sütun yeniden açılır.

→ Kalıcı boşluklar **geçici kilit** üretir, kalıcı değil. Yapısal olarak ulaşılamaz Box pratikte yok.

### Geriye kalan gerçek uç durum

Box'lar kırılabilir, ama **kırılacak renkli blok kalmayabilir.** En küçük örnek 2×2:

```
[Box] [Box]
[ R ] [ R ]
```

Alttaki iki kırmızıya tıkla → patlarlar, iki Box da hasar alır. Ama patladıkları hücreler Box'ların
altında, yukarıdan yeni blok gelemez. Tahtada 0 renkli blok, 2 canlı Box. **Case 2×2'yi açıkça
destekliyor**, yani ele alınması gerek.

### Sektör bunu nasıl çözüyor

Runtime'da değil, content pipeline'da: seviyeler elle tasarlanıyor ve yayın öncesi bot'larla binlerce
kez oynanarak doğrulanıyor (akademik literatür de bunu problem olarak ele alıyor — arXiv 2409.06349).
Spawner'lar da otomatik değil, açık bir tasarım öğesi: hangi sütunun blok üreteceği seviyenin
özelliği.

**Bize çevirisi:** level designer'ımız yok, **generator'ımız var.** Sektörde insanın yaptığı işi üretim
kodumuz yapmak zorunda.

### Türetilen kural: en üst satıra Box konmaz

Zincirleme sonuçları:

- Her sütunun en üst hücresi Box değil → her sütun yukarıdan blok alabiliyor → **en üst satır her
  zaman dolu**
- Üst satır dolu → her zaman yan yana en az iki renkli blok var → shuffle'ın grubu koyacak yeri var
- Her sütundaki en üstteki Box'ın üstü her zaman doluyor → her zaman hasar alabiliyor

Bu kural **dokümanda yok, biz ekledik.** Maliyeti tasarım alanının çok az daralması.

### Seçilen: üretim kısıtı **ve** runtime kontrolü birlikte

Tek başına üretim kısıtı elendi, çünkü **garanti benim akıl yürütmeme dayanıyor ve akıl yürütmem bu
projede bir kez zaten yanlış çıktı** (yukarıdaki düzeltme kaydı). Kanıtlanmamış bir invariant'a tek
dayanak olarak güvenmek kötü mühendislik.

Tek başına runtime yenilgi de elendi: önlenebilecek bir durumu önlemeyip sadece "kaybettin" demek,
hastalığı tedavi etmek yerine sonucunu kabullenmek.

> İkisi farklı iş görüyor: kısıt kötü tahtanın oluşmasını engelliyor, kontrol ise kısıtın varsayımı
> bozulursa (yeni engel tipi, config değişikliği) oyunu kilitlenmekten koruyor. Toplam maliyet ~5
> satır.

---

## Deadlock shuffle algoritması

**Doküman kısıtı:** *"...implement a shuffling solution which doesn't rely on 'blindly shuffle N times
until deadlock is resolved'."*

### Seçenekler
- **A — Kör shuffle + tekrar dene:** doküman açıkça dışlıyor, ve sonlanma garantisi yok
- **B — Karıştır + bir grubu zorla**
- **C — Minimal müdahale:** hiç karıştırma, tek takasla iki bloğu komşu yap → **elendi:** teknik olarak
  en zarif, oyuncu deneyimi en kötüsü. Görünmez düzeltme oyunun rastgele davrandığı hissini verir, ve
  tahta hâlâ tek hamlelik kalır → ardışık deadlock zinciri
- **D — Renkleri baştan üret** → **elendi:** 15 kırmızı varken sonrasında 3 kalabilir; oyuncunun
  biriktirdiği "büyük grup potansiyeli" buharlaşır. **Shuffle kavramsal olarak yeniden düzenlemedir,
  yeniden üretme değil.**

**Seçilen: B.** Tek geçişte en az bir geçerli grup — deneme yok, döngü yok, sonlanma garantili.

### Garanti adımı her zaman mı çalışsın

"Önce grup oluştu mu diye bak" bedava görünüyor çünkü `RecalculateGroups()` zaten çağrılacak. Ama
kontrol "grup yok" derse garanti adımı **ve tekrar** hesaplama gerekir.

**Seçilen: her zaman zorla.** Tek kod yolu, dallanma yok, ve *"her shuffle tam bir geçişte biter"*
cümlesi kodun bir özelliği oluyor — ortalama durum hakkında bir iddia değil. Case'in yasakladığı şey
belirsiz süreli döngüydü; bunun en net zıddı bu.

### Çifti seçme

Komşu çift **rezervuar örneklemesiyle** (k=1) seçiliyor. İlk çifti almak zorlanan grubu her seferinde
aynı köşeye koyardı; hepsini toplamak boyu bilinmeyen bir liste allocate ederdi.

### Animasyon

Kafa karışıklığının kaynağı: **blokları taşımıyoruz**, hücrelerin renk değerlerini yer değiştiriyoruz.
Mantıksal olarak hiçbir şey hareket etmiyor, ama oyuncunun modeli "bloklar karıştı".

Blokları uçurmak (permütasyonu kaydedip görselleri taşımak) oyuncunun modeline birebir uyardı, ama
**zor olan deadlock algoritması, animasyonu değil.** Küçül-değiş-büyü aynı bilgiyi iletiyor, maliyeti
onda biri.

---

## Input: tıklamayı hücreye çevirme

Seçenekler: collider + raycast, doğrudan matematik, ya da blok başına `OnMouseDown`.

**Raycast elendi, ve sebebi performans DEĞİL, doğruluk.** Raycast **görsel** dünyayı sorguluyor; biz
**mantıksal** hücreyi istiyoruz. Bu iki dünyayı bilerek ayırdık — raycast onları geri birleştirir ve
düşen blokta yanlış hücre döndürerek "oturmuş blok" filtresini bozar.

Tahtada sıfır collider olması (fizik motoru hiç uyanmıyor) güzel bir **yan etki**, ana gerekçe değil.

> Bu karar mantık/görsel ayrımının doğrudan sonucu. **İyi bir mimari kararı sonraki kararları
> kolaylaştırır.**

Eski `Input` API'si kullanıldı: yeni Input System paket kurulumu, action asset ve `PlayerInput`
bileşeni gerektiriyor, ihtiyacımız tek satır.

---

## Sunum katmanında sessizce toparlanmak yerine fırlatmak

`BlockPool.Rent` havuz tükenince büyümek yerine fırlatıyor. Kapasite tahtadan türüyor, dolayısıyla
tükenmesi absorbe edilecek bir yük tepesi **değil** — view'ın bir bloğu iade etmediği anlamına gelir.
Sessizce allocate eden bir havuz, var olmak için kurulduğu bug'ı gizler.

Aynı refleks üç yerde daha: çift iade yakalanıyor, bir hücreye zaten uçan blok varken ikincisi
reddediliyor, ve atanmamış sprite'lar `Bind` anında bir kez kontrol ediliyor.

Ortak nokta: hepsi **sessiz bozulmayı gürültülü hataya çeviriyor.** Sessiz olanların üçü de ekranda
yanlış bir görüntü olarak belirir, ki bakarak fark edilmez.

---

## Efekt katmanı: `ParticleSystem` mi, havuzlanmış `SpriteRenderer` mı

**`ParticleSystem` elendi.** Kendi material'i ile çizer, ve tahta + efektler + arka plan + çerçeve şu
an tek material paylaşıyor. Araya farklı bir material giren an batch ikiye bölünür — cilayı tam da
ölçülen şeyi bozarak eklemek ters bir takas olurdu.

İkinci gerekçe: `ParticleSystem` blok sprite'ını kullanamaz. Patlayan bloğun **kendi görüntüsünün**
dağılması, jenerik bir toz bulutundan hem daha okunur hem bedava.

**Seçilen: havuzlanmış `SpriteRenderer`**, ve `FallAnimator` ile bilinçli olarak aynı şekilde —
önceden ayrılmış struct dizisi, geriye doğru dönen tek `Tick`. Birini anlayan diğerini de anlar.

**Havuz ayrımı tercih değil zorunluluk:** efekt sprite'ları kendi havuzundan kiralanıyor. `ApplyBlast`
patlayanları serbest bırakmayı, yerlerine gelecekleri kiralamadan **önce** yapıyor; ölen bloğu
animasyon süresince tutmak ana havuzu tam ihtiyaç anında aç bırakırdı.

Tavan aşıldığında efekt **sessizce düşüyor** — havuzun fırlatma davranışının aksine. Orada tükenme bir
bug, burada 100 bloklu bir tahtanın tek hamlede patlaması. Düşürülen bir kıvılcım eksik bir parıltı,
yanlış bir tahta değil.

Ölçüldü: 10×10 tahtada 100 hücre + backdrop + çerçeve **tek batch**, ve parçacıklar ekrandayken batch
sayısı değişmiyor.

---

## Oynanabilirlik garantisi üretimde başlar

Elle test sırasında 2×2 bir tahta kilitli açıldı: hamle yok, shuffle yok, kayıp yok. İki ayrı kusurdu
ve **testlerin hiçbiri ikisini de görmüyordu**, çünkü `Board.Generate()` testlerde hiç çağrılmıyordu.
Oyuncunun gördüğü her tahtayı üreten metot sıfır kapsamdaydı.

**Kusur 1 — `boxCount == 0` grup taramasını atlıyordu.** Yerleştirilecek Box yoksa `Generate()` erken
dönüyordu. Taranmamış grup verisiyle her hücre patlatılamaz, bütün tahta deadlock görünüyor. Ölçüldü:
8×8, K=4, Box=0 için 200 seed'in 200'ü. Oyun bunu kazara toparlıyordu (açılışta shuffle çağrılıyordu),
yani Box'sız her seviye gereksiz bir shuffle ile başlıyordu — ve Box'sız, case dökümanındaki iki
örneğin de şekli.

**Kusur 2 — çözümsüz tahta sessizce "oynanıyor" kalıyordu.** Açılıştaki `TryResolveDeadlock()`
çağrısının dönüş değeri yutuluyordu. 2×2 + 1 Box = 3 renkli hücre; K=6 ile üçünün de farklı çıkma
olasılığı %56. Ölçüldü: 500 seed'in 277'si hamlesiz başlıyordu.

### Seçilen çözüm

**Üretim, shuffle'ın yapamadığını yapabilir.** `DeadlockResolver` renk sayılarını korumak zorunda
olduğu için *takas* eder ve hiçbir renk iki kez geçmiyorsa başarısız olur — bu doğru davranış.
`Generate()` ise sıfırdan **atama** yapar, kısıtı yoktur. Dolayısıyla garanti adımı tek bir yazma
işlemidir ve başarısız olamaz: üst satır Box tutmadığı için oradaki herhangi iki komşu, iki komşu
renkli hücredir.

Sütun sabit sıfır değil, çekiliyor — garanti edilen çift hep aynı köşede oturmasın diye.

Ve açılış artık başarısızlığı okuyor: hamlesi ve çaresi olmayan bir seviye bitmiştir. Oyuncuyu asla
cevap veremeyecek bir tahtaya dokundurmaya devam ettirmek, kaybettirmekten kötüdür.

> **Ders:** iki kusur da hesapla değil **elle oynayarak** bulundu. Yazıldığı boyutta (10×10, K=6)
> ikisi de pratikte görünmez. Kapsam boşluğunu görünür kılan şey **aralığın ucundaki bir config**'ti.

---

## Deadlock çözümü iki kademeli: takas, sonra atama

Yukarıdaki düzeltmenin ardından kalan soru: çözücünün başarısız olmaya hakkı var mı?

### Ölçüm önce

2000 oyun × 3 config, rastgele oynayan bir bot:

| Config | Kazandı | Hamle bitti | **Tahta öldü** | Shuffle görüldü |
|---|---|---|---|---|
| 2×2, K=3, Box=1 | 1566 | 0 | **434 (%22)** | 441 |
| 2×3, K=6, Box=1 | 1664 | 0 | **336 (%17)** | 1284 |
| 10×10, K=6, Box=8 | 0 | 2000 | 0 | **0** |

İki şey birden çıkıyor. Küçük tahtalarda oyunların beşte biri "çözülemedi" diye bitiyordu. Ve
varsayılan 10×10'da **shuffle 2000 oyunda bir kez bile tetiklenmedi** — deadlock çözümü, teslim edilen
varsayılan seviyede görünmez. Küçük config'ler onu görmenin tek yolu.

### Neden başarısız oluyordu

Grup yapmak iki ayrı şey gerektiriyor: **koyacak yer** (iki komşu renkli hücre) ve **yapacak malzeme**
(en az iki kez geçen bir renk). Kod ikisini tek bir koşula katlıyordu, ve malzeme yoksa *yer varken
bile* pes ediyordu.

### Seçilen: kademelendir, kısıtı kaldırma

"Shuffle yeniden dağıtmaz, yeniden dizer" kuralı gerçek bir özellik — shuffle'a "aynı tahta karıştı"
hissini veren şey o. Kaldırmak yerine korunabildiği yerde korunuyor:

| Kademe | Koşul | Ne yapar |
|---|---|---|
| 1 | Bir renk ≥ 2 kez geçiyor | Karıştır + en sık rengi çifte **takasla** taşı. Renk sayıları korunur |
| 2 | Hiçbir renk 2 kez geçmiyor | Çiftin bir hücresine komşusunun rengini **ata**. Tam bir hücre değişir |
| Başarısız | İki komşu renkli hücre yok | Renk değiştirmek iki hücreyi komşu yapamaz |

### Neden tahtanın tamamını yeniden üretmiyoruz

İlk akla gelen çözüm `Generate()`'i tekrar çağırmak. Ama o renkleri yeniden çekmenin yanında
**kutuları yeni hücrelere taşır ve canlarını sıfırlar.** Sekiz kutunun dördünü çatlatmış bir oyuncu
ilerlemesinin silindiğini görür. Sektörün cevabı da aynı: engel katmanı asla yeniden üretilmez,
sadece renk katmanı — ve kademe 2 zaten bunun en küçük hâli.

### Sonuç

Geriye kalan tek başarısızlık "iki komşu renkli hücre yok", ve üretilen bir tahtada bu imkânsız (üst
satır kuralı + en az 2 sütun). Ölçüm tekrarlandı: üç config'de de **ölü tahta = 0**, ve 2×2'de görülen
shuffle sayısı 441'den 875'e çıktı — eskiden pes ettiği yerlerde artık çözüyor.

Yan fayda: `Lost` artık gerçek tahtalarda sadece "hamle bitti" anlamına gelebiliyor, yani HUD'daki
mesaj ayrıca düzeltmeye gerek kalmadan dürüstleşti.

---

## Klasör düzeni: ince taksonomi mi, asmdef sınırları mı

Referans olarak bakılan başka bir case projesi `Animation/`, `Board/`, `Core/`, `Data/`, `Effects/`,
`Mechanics/`, `UI/`, `Utils/` diye dokuz klasöre ayrılmış. İlk bakışta daha derli toplu.

**Elendi.** O dokuz klasörün tamamı tek bir asmdef altında, yani hiçbir sınır derleyici tarafından
zorlanmıyor. Sonucu somut: oradaki grup bulucu MonoBehaviour'lar üzerinde çalışıyor, dolayısıyla
grup bulmayı sınayan bir test bile sahnede gerçek bir `GameObject` kurmak zorunda.

Burada klasörler zaten asmdef sınırlarıyla örtüşüyor. On yedi dosyayı dokuz klasöre bölmek klasör
başına iki dosya demek olurdu, ve asıl güçlü sinyali — derleyicinin zorladığı `Core` sınırını —
görsel gürültüyle seyreltirdi.

> **Klasör sayısı mimari ölçmez.**

Seçilen: `Game/` altında üç alt klasör (`Board/`, `Effects/`, `UI/`), akış ve Unity kabuğu kökte.

---

## Sonradan gelen iki ayrım

Cila eklendikçe `BoardView` 399 → 554 satıra çıktı ve sekiz iş yapmaya başladı.

**Kamera ayrıldı.** `BoardCamera` üç şartı da geçiyor: kendi durumunun tek sahibi (sarsıntının döndüğü
taban konumu başka kimse yazamaz), gerçek bir kavram, ve adı `Manager` değil. Bir tahtayı
*çerçevelemek* ile *çizmek* yalnızca tarihsel olarak aynı sınıftaydı.

**Shuffle animasyonu ayrılmadı.** `blockAt` dizisine ve `Redraw()`'a doğrudan bağlı; ayırmak diziyi ve
bir callback'i geçirmeyi gerektirirdi, yani **kaldırdığından fazla bağ kurardı.** Satır sayısı
düşürmek uğruna yapılan bir ayrım, ayırdığı iki parçayı birbirine daha sıkı bağlıyorsa kayıptır.

**Easing eğrileri tek yere toplandı**, ama `Core`'a değil `Game/Effects/`'e: `Core`'un kapsamı
"motorsuz" değil, "oyunun kuralları". Ve sadece çağıranı olan üç eğri var — easing tabloları bir düzine
kullanılmayan fonksiyonun biriktiği yerdir.
