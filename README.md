# IBSaveConverter

Moves Infinity Blade III characters between the PC version and the Android port.

A save holds up to 5 characters on both platforms. The two games store them differently:

- **PC:** the `Documents\My Games\Infinity Blade III\SwordGame\Cloud` folder. Characters can be added or replaced one slot at a time.
- **Android:** the game loads one zip that holds the whole save. Loading a zip replaces everything on the phone, so the zip has to contain every character the phone should have.

## PC → Android

1. **PC save folder.** Found automatically; change it if needed.
2. **Characters to bring.** Tick one or more.
3. **Android save.** Either:
   - *Start a new Android save:* the zip holds only the ticked characters, from slot 1.
   - *Add to an Android save I already have:* pick a zip exported from the phone. Its characters stay in their slots and the ticked ones go into its free slots. Blocked with a clear message if they don't fit in 5.

   A preview shows exactly what the Android save will hold.
4. **Save as.** Then **Make Android save**.

## Android → PC

1. **Android save.** The zip the Android game exported.
2. **PC save folder.** Found automatically. A missing folder is created.
3. **What to bring over.** Either:
   - *The whole Android save:* the PC ends up with exactly the Android characters, in the same slots. The characters on the PC are replaced.
   - *One character:* pick a character and a PC slot (1-5). The other PC characters stay.

   A preview shows what the PC will have. Then **Install on PC**.

## Single save files

Both tabs also take a single save file (`.bin`) instead of, or as well as, a whole save. It can be:
- **unencrypted** (A save from iOS or PC that doesnt have encryption),
- **PC encrypted** (a `_SwordSaveX_N-0.bin` from a Cloud folder), or
- **Android encrypted** (a `_SwordSaveX_N-0.bin` from an Android zip).

The app works out which one it is and encrypts it the right way for where it's going.

- **PC → Android:** "Add save file…" under the PC save folder. The file joins the characters list (ticked, with a Remove button), so it can start a new Android save or be added to an existing one. It works without a PC folder too.
- **Android → PC:** "Choose save file…" under the Android save. The file's character goes into the PC slot you pick.
