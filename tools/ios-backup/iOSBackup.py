import argparse
import contextlib
import hashlib
import json
import os
import plistlib
import re
import struct
import sys

try:
    from Crypto.Cipher import AES
    from iphone_backup_decrypt import (BackupNotEncryptedError, EncryptedBackup,
                                       IncorrectPassphraseError, NotABackupFolderError)
except ImportError:
    print(json.dumps({"error": "Python is missing the iphone_backup_decrypt package. "
                               "Install it with: pip install -r requirements.txt"}))
    sys.exit(3)

GAME_DOMAIN = "AppDomain-com.chairentertainment.IB3"
SAVE_FILES = "Documents/SAVE/%"
KEYCHAIN_DOMAIN = "KeychainDomain"
KEYCHAIN_FILE = "keychain-backup.plist"

UUID_PATTERN = re.compile(rb"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")
# Very old iOS used MD5(MAC address) instead of a UUID.
MD5_PATTERN = re.compile(rb"(?<![0-9A-Za-z])[0-9a-f]{32}(?![0-9A-Za-z])")

# Keychain items are AES-GCM with an empty IV, which makes the first counter block 0...01.
GCM_EMPTY_IV_COUNTER = b"\x00" * 15 + b"\x01"


class HelperError(Exception):
    pass


def allow_old_backups() -> None:
    """Lets backups made on iOS 10.1 and older unlock.

    The library always runs a first PBKDF2 round that uses the DPIC and DPSL fields. Those fields only exist in
    backups made on iOS 10.2 or newer. Older backups skip that round and go straight to the second one.
    """
    from hashlib import pbkdf2_hmac
    from iphone_backup_decrypt import utils

    original = utils.BackupKeyBag.unlock_with_passphrase

    def unlock_with_passphrase(self, passphrase):
        if b"DPIC" in self.attrs and b"DPSL" in self.attrs:
            return original(self, passphrase)
        iterations = utils.BackupKeyBag._validate_iterations(self.attrs[b"ITER"], "ITER", utils._MAX_ITER_ITERATIONS)
        return self.unlock_with_key(pbkdf2_hmac("sha1", passphrase, self.attrs[b"SALT"], iterations, 32))

    utils.BackupKeyBag.unlock_with_passphrase = unlock_with_passphrase


def main() -> int:
    args = parse_args()
    password = sys.stdin.readline().rstrip("\r\n")
    try:
        # The library prints warnings; keep stdout for the JSON result only.
        with contextlib.redirect_stdout(sys.stderr):
            result = extract(args.backup, args.out, password)
    except HelperError as ex:
        print(json.dumps({"error": str(ex)}))
        return 2
    except Exception as ex:
        print(json.dumps({"error": f"Couldn't read the backup: {ex}"}))
        return 1
    print(json.dumps(result))
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--backup", required=True, help="the backup folder (holds Manifest.plist)")
    parser.add_argument("--out", required=True, help="where to write the game's SAVE files, if the backup has them")
    return parser.parse_args()


def is_old_backup(backup_folder: str) -> bool:
    """Backups made on iOS 9 and older list their files in Manifest.mbdb instead of Manifest.db."""
    return (os.path.exists(os.path.join(backup_folder, "Manifest.mbdb"))
            and not os.path.exists(os.path.join(backup_folder, "Manifest.db")))


class OldEncryptedBackup:
    """Reads an encrypted backup made on iOS 9 or older.

    The library only reads the newer layout. In the old one, Manifest.mbdb is a plain list of files. Each file
    sits in the backup folder under SHA1("domain-path"), encrypted with its own key. That key is wrapped with
    one of the keybag's class keys, the same way as in newer backups.
    Only the methods this helper uses are provided.
    """

    def __init__(self, backup_folder: str, password: str):
        from iphone_backup_decrypt import utils

        with open(os.path.join(backup_folder, "Manifest.plist"), "rb") as file:
            manifest = plistlib.load(file)
        if not manifest.get("IsEncrypted"):
            raise BackupNotEncryptedError("Backup is not encrypted.")
        self.folder = backup_folder
        self.keybag = utils.BackupKeyBag(manifest["BackupKeyBag"])
        self.keybag.unlock_with_passphrase(password.encode("utf-8"))
        if not self.keybag.unlocked:
            raise IncorrectPassphraseError("Wrong backup password.")
        with open(os.path.join(backup_folder, "Manifest.mbdb"), "rb") as file:
            self.records = list(self._read_mbdb(file.read()))

    @staticmethod
    def _read_mbdb(data: bytes):
        if data[:4] != b"mbdb":
            raise HelperError("This backup's file list (Manifest.mbdb) is damaged.")
        pos = 6

        def string():
            nonlocal pos
            length = struct.unpack(">H", data[pos:pos + 2])[0]
            pos += 2
            if length == 0xFFFF:
                return b""
            value = data[pos:pos + length]
            pos += length
            return value

        while pos < len(data):
            domain, path, _link, _hash, enc_key = (string() for _ in range(5))
            mode, = struct.unpack(">H", data[pos:pos + 2])
            # mode 2, inode 8, uid 4, gid 4, three times 4 each, then size 8, class 1, property count 1.
            size, protection, prop_count = struct.unpack(">QBB", data[pos + 30:pos + 40])
            pos += 40
            for _ in range(prop_count * 2):
                string()
            yield {"domain": domain.decode("utf-8", "replace"), "path": path.decode("utf-8", "replace"),
                   "is_file": mode & 0xF000 == 0x8000, "size": size, "protection": protection, "key": enc_key}

    @staticmethod
    def _like(pattern: str, value: str) -> bool:
        # SQL LIKE as SQLite does it: % is any run, _ is one character, ASCII case doesn't matter.
        regex = "".join(".*" if c == "%" else "." if c == "_" else re.escape(c) for c in pattern)
        return re.fullmatch(regex, value, re.IGNORECASE | re.DOTALL) is not None

    def _matches(self, path_like: str, domain_like):
        return [r for r in self.records if r["is_file"] and self._like(path_like, r["path"])
                and (domain_like is None or self._like(domain_like, r["domain"]))]

    def _file_key(self, record: dict) -> bytes:
        enc_key = record["key"]
        wrapped = enc_key[-40:]
        # The class is in the record's flag byte and in the key's first 4 bytes; try each reading.
        classes = [record["protection"]]
        if len(enc_key) >= 44:
            classes += [struct.unpack("<I", enc_key[:4])[0], struct.unpack(">I", enc_key[:4])[0]]
        for protection_class in dict.fromkeys(classes):
            try:
                return self.keybag.unwrap_key_for_class(protection_class, wrapped)
            except ValueError:
                continue
        raise HelperError(f"Couldn't unlock {record['path']} in the backup.")

    def _decrypt(self, record: dict) -> bytes:
        file_id = hashlib.sha1(f"{record['domain']}-{record['path']}".encode("utf-8")).hexdigest()
        location = os.path.join(self.folder, file_id)
        if not os.path.exists(location):
            location = os.path.join(self.folder, file_id[:2], file_id)
        with open(location, "rb") as file:
            data = file.read()
        if not record["key"]:
            return data
        plain = AES.new(self._file_key(record), AES.MODE_CBC, iv=b"\x00" * 16).decrypt(data)
        return plain[:record["size"]]

    def extract_file_as_bytes(self, relative_path: str, *, domain_like=None) -> bytes:
        matches = [r for r in self._matches("%", domain_like) if r["path"] == relative_path]
        if not matches:
            raise FileNotFoundError(relative_path)
        return self._decrypt(matches[0])

    def extract_files(self, *, relative_paths_like: str, domain_like=None, output_folder: str) -> None:
        for record in self._matches(relative_paths_like, domain_like):
            name = os.path.basename(record["path"])
            if name in ("", ".", ".."):
                continue
            with open(os.path.join(output_folder, name), "wb") as file:
                file.write(self._decrypt(record))


def extract(backup_folder: str, out_folder: str, password: str) -> dict:
    if not password:
        raise HelperError("Enter the backup password.")

    allow_old_backups()
    try:
        if is_old_backup(backup_folder):
            backup = OldEncryptedBackup(backup_folder, password)
        else:
            backup = EncryptedBackup(backup_directory=backup_folder, passphrase=password)
            backup.test_decryption()
    except NotABackupFolderError:
        raise HelperError("This folder isn't an iOS backup. Pick the folder that holds Manifest.plist.")
    except BackupNotEncryptedError:
        raise HelperError("This backup isn't encrypted, so it doesn't hold the save key. "
                          "Turn on \"Encrypt local backup\" and back up again.")
    except IncorrectPassphraseError:
        raise HelperError("Wrong backup password.")
    except KeyError as ex:
        raise HelperError(f"This backup's lock information is missing {ex}, so it can't be read. "
                          "Make a new encrypted backup and try again.")

    device_ids = find_device_ids(backup)
    if not device_ids:
        raise HelperError("No save key was found in the backup's keychain.")

    os.makedirs(out_folder, exist_ok=True)
    backup.extract_files(relative_paths_like=SAVE_FILES, domain_like=GAME_DOMAIN, output_folder=out_folder)
    has_saves = os.path.exists(os.path.join(out_folder, "LocalFileHeaderCache"))

    return {"saveFolder": os.path.abspath(out_folder) if has_saves else None,
            "deviceName": device_name(backup_folder), "deviceIds": device_ids}


def find_device_ids(backup: EncryptedBackup) -> list:
    keychain = plistlib.loads(backup.extract_file_as_bytes(KEYCHAIN_FILE, domain_like=KEYCHAIN_DOMAIN))
    uuids, md5s = [], []
    for items in keychain.values():
        if not isinstance(items, list):
            continue
        for item in items:
            blob = item.get("v_Data") if isinstance(item, dict) else None
            if not isinstance(blob, bytes):
                continue
            plain = decrypt_item(backup, blob)
            if plain is None:
                continue
            uuids += [m.decode() for m in UUID_PATTERN.findall(plain)]
            md5s += [m.decode() for m in MD5_PATTERN.findall(plain)]
    return list(dict.fromkeys(uuids + md5s))


def decrypt_item(backup: EncryptedBackup, blob: bytes):
    if len(blob) < 12:
        return None
    version, protection_class = struct.unpack("<II", blob[:8])
    if version >= 3:
        key_length = struct.unpack("<I", blob[8:12])[0]
        wrapped_key, encrypted = blob[12:12 + key_length], blob[12 + key_length:-16]
    else:
        wrapped_key, encrypted = blob[8:48], blob[48:-16]

    try:
        key = backup.keybag.unwrap_key_for_class(protection_class & 0xF, wrapped_key)
    except Exception:
        # Items marked "this device only" can't be unwrapped from a backup.
        return None
    return AES.new(key, AES.MODE_CTR, nonce=b"", initial_value=GCM_EMPTY_IV_COUNTER).decrypt(encrypted)


def device_name(backup_folder: str) -> str:
    try:
        with open(os.path.join(backup_folder, "Manifest.plist"), "rb") as file:
            return plistlib.load(file).get("Lockdown", {}).get("DeviceName", "")
    except (OSError, plistlib.InvalidFileException):
        return ""


if __name__ == "__main__":
    sys.exit(main())
