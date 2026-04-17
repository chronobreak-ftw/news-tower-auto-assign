# News Tower Auto-Assign

A BepInEx 5 plugin for News Tower that automates reporter assignment and small routine steps on the live story board.

## What it does

- **Auto-assigns reporters** to incoming stories, favoring paths that line up with your current weekly goals when that option is on.
- **Chases weekly objectives intelligently** (the goals the game gives you each week):
  - *Stacking goals* (e.g. "do several of X"): always treated as a match; reward stacks per story.
  - *One-and-done goals* (e.g. "publish one scoop"): chased until something on the board already covers them; after that, the mod stops treating extra matching paths as special.
  - *Scoop* goals get the highest tie-break priority, but only while that one-and-done goal is still open-once an in-progress story is covering the scoop, other scoop paths no longer get that bonus.
- **Discards** some stories automatically: risky ones that don't help a goal, weekend arrivals you haven't started (when that's enabled), impossible "dead end" chains, or stories where nobody with the right skill will be free for a long time and the story doesn't match a goal (see `NewsTowerAutoAssign/ASSIGNMENT_LOGIC.md` for the plain-language rules).
- **Stays passive in the very early game** until you have enough reporters on staff (configurable), so tutorials and the first few days aren't overwhelmed.
- **Pays bribes automatically** when you can afford it, using the same cost the game would roll if you opened the bribe screen once; the mod remembers that roll so repeated checks don't throw off the game's random sequence.
- **Clears suitcase "new unlock" steps** in the background so story chains don't stall when a node would otherwise wait for you to open the newsbook. This is separate from optionally **skipping the suitcase popup** if it still appears.
- **Skips cosmetic popups** where safe (e.g. risk spinner), matching manual outcomes.
- **Fills open ad slots** on the Ads tab when that option is on, using staff who have the right skills; boycotted ads are skipped. The early-game reporter count gate does **not** apply to ads.

## How to use

1. Install [BepInEx](https://github.com/BepInEx/BepInEx/releases) into your News Tower folder.
2. Launch the game once so BepInEx creates `BepInEx/plugins/`.
3. Copy `NewsTowerAutoAssign.dll` into `<News Tower>/BepInEx/plugins/`.
4. Launch the game again.

**Optional settings** live in `BepInEx/config/newstower.autoassign.cfg` under the `[Dev]` section.

## License

[MIT](LICENSE)
