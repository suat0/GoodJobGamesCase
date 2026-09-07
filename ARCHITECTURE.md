# Architecture

The technical companion to the README: the rules the code holds itself to, the data model, and the
algorithms worth explaining. What a production version would add on top is in the `Scope and future
work` section of `README.md`.

---

## Three rules the rest follows from

### 1. Core cannot reference the engine

`Assets/Scripts/Core/` is a separate assembly marked **No Engine References**. `using UnityEngine`
there does not fail review, it fails to compile. `UnityEngine.Random` is unreachable for the same
reason, so randomness is injected as a `System.Random` and every board is reproducible from a seed.

The payoff is the test suite: 63 cases that assert on rules, with no scene, no `GameObject` and no
play mode.

### 2. Logic resolves instantly, animation trails behind

`Board.TryBlast()` returns with the board in its final state — removals, Box damage, gravity, refill,
regrouping. The board is never observable half-resolved.

The view then walks blocks from where they were drawn to where the board says they now are. That
animation is **cosmetic**: it cannot change an outcome, and a dropped frame loses a sparkle, not a
move.

### 3. "Is this block still falling?" lives in the view

A tap on a cell whose *visual* block is mid-air is swallowed by `BoardView.TryPickCell`. Core has no
notion of time and never learns which blocks are in flight.

The filter asks about the tapped cell alone, never the group — landed blocks stay tappable while
others fall, so a group with one member mid-air must still blast.

---

## Data model

```csharp
public enum CellType : byte { Empty = 0, Color = 1, Box = 2 }

public struct Cell {
    public CellType Type;
    public byte Color;    // only meaningful when Type is Color
    public byte Health;   // only meaningful when Type is Box
}

Cell[] cells;   // one dimension, M*N, row-major
```

`Cell` has only two legitimate shapes, so it is built through factories rather than a constructor: a
three-field constructor would allow a coloured Box.

`BoardConfig` is the one conversion point between Unity and Core. `LevelConfig` is a
`ScriptableObject` and cannot cross into Core, so `GameController` copies its values across and
nothing else does. `MoveLimit` and `Seed` are absent from `BoardConfig` — neither shapes a board.

`Grid` is stateless index arithmetic shared by `Board` and `GroupFinder`. `Grid.TryStep` checks
bounds on `(row, col)` and never on `index ± 1`, which would connect the end of one row to the start
of the next.

### Conventions that will bite if forgotten

- **Row 0 is the bottom row**, so `worldY = origin.y + row * cellSize` needs no negation. Gravity
  falls toward decreasing index and new blocks enter from the highest.
- **`Cell` is a mutable struct in an array**, so the copy trap applies:
  ```csharp
  var c = cells[i]; c.Color = 2;   // wrong, mutates the copy
  cells[i].Color = 2;              // right
  ```
- **No sentinel colour for an empty cell.** `CellType.Empty` is separate, which it has to be — a Box
  has no colour either.
- **`groupIdOf` is filled with `-1`, not cleared.** Zero is a valid group id, so "no group" needs a
  different value.
- **`GroupFinder` holds no reference to the board.** It takes the cells per call as a
  `ReadOnlySpan<Cell>` and owns only its own scratch arrays.
- **Scratch arrays are class fields, allocated once** — `groupIdOf`, `groupSizes`, `stack`,
  `boxStamp`. Nothing on the gameplay path allocates.

---

## Move order

This sequence is a game rule, not an implementation detail:

1. Blast the group
2. Damage adjacent Boxes — **one per group**, not per neighbouring block
3. Gravity, then refill from above the column
4. Recalculate groups
5. Increment the move counter
6. **Won?** (no Boxes left)
7. **Lost?** (out of moves with a Box standing)
8. **Deadlocked?** → resolve

**6 must come before 7.** On the last move both can be true, and asking about the loss first would
take the win away on the move that earned it.

**8 comes last**, because shuffling a board that has already been won is an animation with nothing
behind it. A shuffle costs no move: the player neither caused it nor could avoid it.

### One invariant

Every method that changes the board ends by recalculating groups, `Generate()` included. No caller
has to remember to ask, and the board is never seen through stale group data.

---

## Algorithms

### Group finding

One full scan after every change answers three questions at once: what is blastable, what icon each
block shows, and whether the board is deadlocked (largest group < 2). Incremental would be wrong —
one column collapsing can merge or split groups several columns away.

Iterative DFS over an `int[]` stack, no recursion and no `Queue<T>`. **Cells are marked when pushed,
never when popped**: marking on pop would let all four neighbours push the same cell, and the stack
could exceed the cell count. Marking on push bounds it at exactly `M*N`.

### Box damage

A Box takes one damage per adjacent *group*, not per neighbouring block. A stamp compared against a
counter bumped once per blast does that with no set, no allocation and nothing to clear between
moves — old marks go stale by themselves.

### Gravity

Each column is split into segments by Boxes, which act as walls, and each segment collapses within
itself. Only the top segment refills.

A gap under a Box therefore stays empty, possibly for the rest of the level. **That is correct, not a
bug**, and it never locks a column: the topmost Box can always be damaged from above.

### Deadlock

Blind shuffle-and-recheck is ruled out by the case document, and it has no bound on how long it runs.
Instead: one pass that shuffles for the look of it, then places a group deliberately.

A single survey walk collects the coloured cells, the colour histogram, and — by reservoir sampling —
one adjacent pair. The sampled pair is the pair the guarantee step uses, which is what makes one pass
enough. Sampling rather than taking the first pair keeps the placed group out of the same corner
every time.

Two tiers, and only the colours ever move — Boxes, holes and Box health stay exactly where they are:

| Tier | When | What happens |
|---|---|---|
| 1 | Some colour occurs twice | The most common colour is **swapped** onto the sampled pair. Every colour count is preserved |
| 2 | No colour occurs twice | One cell of the pair is **assigned** its neighbour's colour. Exactly one cell changes |

Tier 2 exists because rearranging a set with no repeat still has no repeat, however it is ordered.
Small boards produce that routinely: a 2×2 holding one Box has three coloured cells, and with six
colours all three come out different more often than not.

**The one condition nothing can fix** is no two adjacent coloured cells — a colour cannot make two
cells neighbours. On a generated board that is unreachable, because the top row never holds a Box and
is therefore always a full run of colours.

### Board generation

Constrained random, and one constraint carries most of it: **no Box on the top row.** Every column can
then receive falling blocks → the top row is always full → two adjacent coloured cells always exist →
the resolver always has somewhere to place a group.

Generation also guarantees a legal move exists before play starts. It can do what the shuffle cannot:
the shuffle swaps and so needs a repeated colour, while generation assigns, so one write is enough
and cannot fail.

Boxes are placed by partial Fisher-Yates over the Box-eligible prefix of the array. Since row 0 is the
bottom and the array is row-major, "not the top row" costs no filtering.

---

## View layer

- **`SpriteRenderer` plus one sprite atlas.** Blocks, obstacles, blast effects, the background
  gradient and the board frame are all sprites in `BlockAtlas`, so the whole world is one batch.
  Measured: 100 cells plus backdrop plus frame render as a single draw call.
- **The board avoids Canvas**, because a canvas rebuild is the cost worth avoiding when a hundred
  blocks move every frame. The HUD *does* use Canvas — four labels changing once per move are the
  opposite case, and it sits on its own canvas so its rebuild touches nothing else.
- **Effects are pooled `SpriteRenderer`s, not a `ParticleSystem`**, which would draw with its own
  material and split the batch.
- **Object pooling.** Every block is created at startup; no `Instantiate` or `Destroy` during play.
  The pool throws rather than growing when exhausted — capacity comes from the board, so running out
  means the view leaked a block.
- **The camera is fitted to the board**, never the board scaled to the screen. Scale would leak into
  the input maths and into every fall distance.
- **Input is arithmetic, not raycasting.** `ScreenToWorldPoint` and a floor division; there is not one
  collider on the board. The reason is correctness rather than speed — a raycast asks the visual
  world what was hit, and during a fall the visual world is deliberately behind the logical one.
- **One `Update` for the whole board.** `FallAnimator` and `EffectRunner` are preallocated struct
  arrays walked by a single tick; no block has an `Update` of its own.
- Blocks fall under gravity, `sqrt(2d/g)`, so distance still sets the duration.

---

## Events

`GameController` raises four; the view and the HUD subscribe to it and it holds no reference to
either.

```csharp
public event Action<Board> OnBoardReady;
public event Action<BlastResult> OnBoardChanged;
public event Action OnDeadlockResolved;
public event Action OnStatusChanged;
```

- Subscribe in `OnEnable`, unsubscribe in `OnDisable` — not `Start`/`OnDestroy`, or an object that is
  disabled and re-enabled subscribes twice.
- Named methods, never lambdas: a lambda cannot be unsubscribed.
- `BlastResult` is a single reused instance. Read it during the call and never store it.

---

## Scope

**In:** blasting and icon tiers, the Box obstacle, gravity with segments, deadlock detection and
resolution, the objective and move limit, scoring, the end-of-level panel, pooling, the sprite atlas,
blast and landing effects, 63 unit tests.

**Out:** progression across levels, a level editor, special blocks, chained combos, save/load,
localisation. The `Scope and future work` section of `README.md` records the threshold at which each
would start paying for itself.

A board with no Boxes has no objective and no move limit, which is the shape both of the case
document's examples have. That is data rather than a second mode — the turn runs the same either way.
