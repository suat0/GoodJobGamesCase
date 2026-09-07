# Blast

A collapse / tile-matching game built for the Good Job Games case study, in **Unity 6000.2.6f2**.
Tap any group of two or more adjacent same-coloured blocks to blast it; blocks above fall in, new ones
drop from the top, and the board is never allowed to reach a state with no legal move.

The case names performance — memory, CPU, GPU — as its focus, so that is what the architecture is
organised around. The short version: **the game rules never touch the engine, and nothing allocates
after the board is built.**

---

## The rules, as implemented

| Rule | Where |
|---|---|
| 2+ orthogonally adjacent same-colour blocks form a blastable group | `GroupFinder.cs` |
| K colours (1–6), M rows and N columns (2–10) | `BoardConfig.cs`, `LevelConfig.cs` |
| Icon tiers: group size `> C` → 3rd icon, `> B` → 2nd, `> A` → 1st, else default | `Board.TierAt` |
| Emptied cells refill from above and from blocks spawned off the top of the column | `GravityResolver.cs` |
| Box obstacle: ignores gravity, blocks the fall of everything above it, 2 health, takes **one** damage per adjacent group — not per block | `Board.cs`, `Cell.cs` |
| Settled blocks stay tappable while others are still falling | `BoardView.TryPickCell` |
| Deadlock is detected and resolved without a blind reshuffle | `DeadlockResolver.cs` |

Two inconsistencies in the case document, and how they were read:

1. The constraints say 2–10 columns, but Example 1 uses `N = 12`. **The page-one constraints are
   treated as authoritative.** The code still runs whatever the config gives it, with no hard-coded
   ceiling, so both readings work.
2. Example 1 writes `C = 9` and then says "more than 10". Example 2 is self-consistent, so the rule
   is read as **`> C`**.

Full reasoning for these and every other decision is in `Docs/DECISIONS.md`.

---

## Architecture

```
Assets/Scripts/
├── Core/          Pure C# game rules. No UnityEngine, enforced by the compiler.
│   Board, Cell, Grid, GroupFinder, GravityResolver, DeadlockResolver, GameSession
├── Game/          The Unity shell.
│   ├── Board/     BoardView, BlockView, BlockPool, BoardCamera
│   ├── Effects/   FallAnimator, EffectRunner, Easing
│   └── UI/        HudView
└── Tests/EditMode/  61 test cases over Core, engine-free
```

### 1. Core cannot reference the engine

`BlastGame.Core.asmdef` is marked **No Engine References**. Writing `using UnityEngine` in `Core`
does not fail review — it fails to compile. `UnityEngine.Random` is unreachable for the same reason,
so randomness is injected as a `System.Random` and every board is reproducible from a seed.

This is what makes the tests engine-free: they assert on rules, not on a scene.

### 2. The rules resolve instantly; animation trails behind

`Board.Blast()` produces the final board in one call — removals, falls, spawns, regrouping, deadlock
check. The board is never observable in a half-resolved state. The view then walks blocks from where
they were drawn to where the board says they now are, and that animation is **purely cosmetic**: it
cannot change an outcome, and dropping a frame of it loses a sparkle, never a move.

One consequence is deliberate: *"is this block still in the air?"* lives in the view
(`FallAnimator.IsSettled`), never in Core. Core has no notion of time.

### 3. Restraint as a design goal

No event bus, no DI container, no command pattern, no interfaces with a single implementation, no
LINQ. `Docs/FUTURE_WORK.md` records what a production version would add — and, more usefully, the
**threshold** at which each one starts paying for itself.

---

## Performance

### Memory

Nothing on the gameplay path allocates. The mechanisms, not the intention:

- `GroupFinder` keeps its `groupIdOf`, `groupSizes` and DFS `stack` as fields, allocated once. The
  search is iterative, and cells are marked **when pushed** rather than when popped, which bounds the
  stack at exactly `M*N`.
- `FallAnimator` and `EffectRunner` are struct arrays with a live count, walked by one `Tick`.
  Finishing an entry swaps the last one into its slot, so the loop runs backwards and nothing is
  skipped or done twice.
- `BlockPool` creates every block once and then only activates and deactivates. It **throws** rather
  than growing when exhausted: capacity is derived from the board, so running out means the view
  leaked a block, and a pool that quietly allocates hides the bug it exists to catch.
- `Board.Blast` writes into one reused `BlastResult`. It is valid during the call and never stored.
- The HUD writes `label.SetText("{0:0}", value)`, not an interpolated string, so the score counter
  can change every frame while it climbs without putting anything on the heap.

### Draw calls

Every sprite in the game — blocks, obstacles, blast effects, the background gradient and the board
frame — is a sprite in `BlockAtlas`, on one material, in one sorting layer. Effects are pooled
`SpriteRenderer`s rather than a `ParticleSystem` specifically because a particle system draws with its
own material and would split the batch.

The HUD is a separate `Canvas`, which is its own batch by nature. Its graphics have `raycastTarget`
and `maskable` turned off, keeping them off the list the raycaster walks on every tap and out of the
mask stencil test.

### CPU

Input is grid arithmetic — `ScreenToWorldPoint` and a floor division. No colliders, no physics, no
raycast. The whole board has **one** `Update`; no block has one of its own.

### What is verified, and what to check yourself

Verified in this repository:

- The engine-free boundary, by the compiler.
- 61 test cases across 6 fixtures, covering group finding and adjacency, icon tiers, gravity
  segmentation and Box damage, blast ordering, deadlock detection, shuffle guarantees, and
  that generation never produces a board with no legal move.

Worth confirming in the editor, since a claim about performance should be looked at rather than
believed: the **Profiler** during a blast-heavy stretch (GC Alloc should stay at 0 B/frame), and the
**Frame Debugger** (the board and its effects should be a single batch).

---

## Presentation

The art is the 26 sprites supplied with the case, plus two shapes generated by
`UiTextureGenerator` — a vertical gradient and a white rounded nine-slice. Those two are written into
`Assets/Art/` deliberately, so the atlas packs them and the background and board frame keep sharing
the blocks' material. Everything else is motion:

- Blocks fall under gravity (`sqrt(2d/g)`), not at a constant rate, and squash on landing — without
  becoming untappable for even a frame.
- A blasted block swells past its cell and collapses, throwing shards of its own sprite that fall and
  spin. Effects past a fixed cap are dropped rather than queued: a full board blasting at once would
  otherwise ask for hundreds.
- Breaking a Box knocks the camera. An ordinary blast has to be large before it earns the same.
- A deadlock shuffle shrinks every block away, swaps the colours while nothing is on screen, and grows
  them back — the alternative, flying each colour to its new cell, carries the same information for
  ten times the work.
- The objective shows the Box sprite and a count rather than the word "Boxes".

### Accessibility

The supplied art gives **every colour a different icon**, not just a different hue, so the board stays
readable without colour vision. That is the case's own design decision and it is preserved: the icon
tiers change the icon, never remove the distinction.

Screen shake is small and fires only on a Box break or a large blast. A production build would make it
switchable.

---

## Third party

Everything here is redistributable in source form.

| What | Licence | Why |
|---|---|---|
| TextMeshPro (ships in `com.unity.ugui`) | Unity | SDF text stays sharp at any size |
| [Baloo 2](https://fonts.google.com/specimen/Baloo+2) ExtraBold | OFL (`Assets/Fonts/`) | HUD typeface |
| Unity's built-in `UISprite` | Unity | The rounded nine-slice behind every panel |

No tweening library. The view already needed a preallocated, single-`Tick` animator for the board, and
a second mechanism whose only advantage is convenience would have been a dependency without a reason.

The font asset is generated by `PolishSetup.CreateFontAsset` rather than by hand, and is **static**:
its 95 printable ASCII glyphs are baked at build time, because a dynamic font asset renders missing
glyphs during play — which allocates. That decision has a known expiry date, recorded in
`Docs/FUTURE_WORK.md`: the first non-ASCII localisation needs a different font strategy.

---

## Running it

1. Open the project in Unity **6000.2.6f2**.
2. Open `Assets/Scenes/Game.unity` and press Play.

A level is a ScriptableObject — rows, columns, colour count, the three icon thresholds, Box count,
move limit and seed. Which one is played is a field on the `Game` object in the scene, so **a new
level is a new asset, not new code**. Four ship, chosen to cover the ends of the range the case
allows:

| Asset | What it is for |
|---|---|
| `Level_10x10` | The default. 10×10, six colours, eight Boxes, twenty moves — Example 1 from the case document |
| `Level_4x10` | Wide and short, so the camera fit is limited by width rather than height |
| `Level_2x2` | The smallest board the case allows. Three colours over three coloured cells run out of pairs quickly, so the shuffle is easy to trigger |
| `Level_8x8_NoBoxes` | No Boxes, so no objective and no move limit — the shape both of the case document's examples have |

A seed of `0` means a fresh board every run; any other value reproduces the same board exactly, which
is what makes Core testable.

### Editor tooling

`Assets/Editor/` holds three generators, not runtime code. Everything they produce is committed, so
the project opens and runs without them; they are here because the settings behind a generated asset
cannot be read back out of it.

- `HudBuilder` composes the HUD and saves the scene. The scene is what ships — this is a generator, so
  running it replaces the HUD wholesale. It exists so the layout and its thirteen colours are readable
  in one file instead of scattered through scene YAML.
- `UiTextureGenerator` writes `Backdrop.png` and `Frame.png`. Those two are the only images here that
  did not come with the case, and this is where the shapes and the reason they live in `Assets/Art`
  are recorded.
- `PolishSetup` builds the font asset at its exact sampling size, padding and character set.

### Tests

Unity → **Window → General → Test Runner → EditMode → Run All**, or from the command line:

```bash
/Applications/Unity/Hub/Editor/6000.2.6f2/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -runTests -testPlatform EditMode \
  -projectPath . -testResults results.xml
```

---

## Documents

| File | What it holds |
|---|---|
| `Docs/CLAUDE.md` | Project context: the rules, the architectural constraints, the data model |
| `Docs/DECISIONS.md` | Every decision with the alternatives that were eliminated and why |
| `Docs/ROADMAP.md` | The implementation order and what each commit was meant to deliver |
| `Docs/FUTURE_WORK.md` | What a production version would add, and the threshold for each |
