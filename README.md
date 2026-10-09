# IBSaveConverter

Moves Infinity Blade III and Infinity Blade II characters between PC, Android and iOS.

| | Infinity Blade III | Infinity Blade II |
|---|---|---|
| Bring characters from | PC, Android, iOS, Save file | PC, Android, Save file |
| Send them to | PC, Android, iOS | PC, Android |

A save holds 5 characters. A save can't be sent back to its own kind (PC to PC), except iOS to iOS.

## How to use

1. Start `IBSaveConverter.exe` and pick a game.
2. **Bring characters from:** pick where the save is. The characters appear below.
3. **Characters:** tick the ones to bring.
4. **Send them to:** pick the destination. A preview shows what it will hold. Press the button.

Before the app writes into a folder that already has saves, it backs the folder up next to itself. Replacing characters asks you to confirm first.

## Where the saves are

- **PC:** `Documents\My Games\<game>\SwordGame\Cloud`. Found automatically.
- **Android:** the zip the Android port exports. Loading a zip replaces the whole save on the phone, so the new zip must hold every character you want to keep. Pick "Add to an Android save I already have" and choose the zip you exported from the phone.
- **iOS (IB3):** the game's `SAVE` folder, copied off the device.
- **Save file:** one or more `.bin` files: unencrypted, PC, Android or iOS.

**Export decrypted copy…** writes a save with nothing encrypted or compressed, so you can inspect it a save editor (use mine).

## iOS keys

- **Fixed key:** the game has the fixed-key patch. It uses the PC key and opens right away.
- **Legacy:** an unpatched game. Its key is unique to the device. Pick an **encrypted** iOS backup and its password and the app finds the key, or type the key in. The key is remembered.

## Converting a legacy iOS save

Use this when the game on the device does not have the fixed-key patch. Its saves are locked with a key that belongs to that one device. The app needs two things from the device: the `SAVE` folder and the key.

Try the quick check first. In the app, pick **iOS** and browse to the `SAVE` folder. If it says "This save uses the fixed key", you are done and can skip the rest.

### What you need

- The iPhone or iPad the saves came from, with the game still installed, and a USB cable.
- A Windows PC with Apple Devices (or iTunes) and [iMazing](https://imazing.com).

### Steps

1. **Copy the `SAVE` folder off the device (iMazing).**
   - Plug in the device, unlock it and tap **Trust**. Open iMazing and select the device.
   - Click **Back Up** under Quick Actions so iMazing has fresh data.
   - Click **More**, then **Apps**, then **Infinity Blade III**, then open **Documents**. If the game is not listed, click the cogwheel above the app list and show all apps. If Documents can't be opened, open the app's **Backup** folder instead.
   - Select the `SAVE` folder and click **Copy to PC**. Pick an empty folder.
2. **Make an encrypted backup (Apple Devices).**
   - Select the device and open **General**. Under Backups, choose "Back up all of the data on your iPhone to this PC".
   - Tick **Encrypt local backup** and set a password. Write it down. It can't be recovered.
   - Click **Back Up Now** and wait for it to finish.
   - The backup is kept in `%USERPROFILE%\Apple\MobileSync\Backup`. iTunes uses `%APPDATA%\Apple Computer\MobileSync\Backup`.
   - iMazing keeps its own backups elsewhere, so make this one in Apple Devices.
3. **Read the key in IBSaveConverter.**
   - Pick the game, then **Bring characters from**, then **iOS**. Browse to the `SAVE` folder you copied.
   - In the **Device key** section, choose "Find it in an encrypted backup of the device".
   - Browse to the backup folder (the `Backup` folder is fine, the app uses the newest backup in it). Type the backup password and click **Read backup**.
   - The app tries every key in the backup. When one fits, the characters appear. The key is shown with a Copy button and remembered, so you only do this once.
4. **Tick the characters and send them where you want.**

### Sending back to the device

Choose **iOS** as the destination and keep **Use the device's own key** on, so the unpatched game can read the result. Close the game on the device, then use iMazing to put the new `SAVE` folder in place of the old one (see iMazing's [guide to transferring files to an app](https://imazing.com/guides/how-to-transfer-files-folders-mac-pc-computer-app-iphone-ipad-ipod-touch)). The app backs up an existing folder before it writes. Keep your original copy too.

### Things that go wrong

- **The backup is not encrypted.** Only an encrypted backup holds the key. Without it the app finds no key.
- **The password.** It is the backup password you set in step 2. It is not the phone passcode or your Apple ID password.
- **Wrong device or wrong install.** The `SAVE` folder and the backup must come from the same device. If no key fits, check both.
- **Don't delete the game early.** Deleting the game, wiping the device or restoring from an unencrypted backup can lose the key. Saves locked with a lost key can't be opened by anyone. Finish all steps first.