# Config encryption

`encode.exe` and `decode.exe` encrypt and decrypt a `.conf` file so that a normal user
cannot read or edit the settings.

## Why AES-GCM and not RSA

RSA is an **asymmetric** cipher: it works with a public key and a private key, not with
a password. Encrypting a file "with the password `Kopolop0_90u`" is a **symmetric**
job, and the standard answer for that is AES with a key derived from the password.
That is what 7-Zip, `age`, VeraCrypt and every other password-based file tool does.

Wrapping RSA around a password would mean deriving an RSA key pair from the password
anyway — slower, far more code, more ways to get it wrong, and no more security.

So: **AES-256-GCM**, key from **PBKDF2-HMAC-SHA256**.

- **AES-256** — the block cipher, 256-bit key.
- **GCM** — an *authenticated* mode. It stores a 16-byte tag, so a wrong password or a
  single changed byte is detected and reported. Without this you would get silent
  rubbish out and a confusing JSON parse error.
- **PBKDF2, 310 000 iterations** — turns a short password into a 256-bit key and makes
  brute-forcing slow. 310 000 is the OWASP recommendation for PBKDF2-HMAC-SHA256.
- **Random 16-byte salt** — the same file with the same password encrypts to different
  bytes every time, and prebuilt tables are useless.
- **Random 12-byte nonce** — the size AES-GCM is designed for.

## What this protects, and what it does not

The key `Kopolop0_90u` is **compiled into the wrapper exe**, because the app has to
decrypt the file on its own with nobody typing anything.

That means:

- A user who opens the `.conf` in Notepad sees noise. Good.
- A user who edits the file breaks the tag, and the app says so. Good.
- Someone who inspects the exe with a hex editor or a decompiler finds the key.

So treat it as **obfuscation of settings**, not as a secret store.
**Never put a password, token or API key in a `.conf` file.**

## Usage

```powershell
# encrypt in place, keeps mstodo.conf.bak next to it
encode.exe "mstodo.conf" "Kopolop0_90u"

# decrypt in place, also keeps a .bak
decode.exe "mstodo.conf" "Kopolop0_90u"

# write the result somewhere else instead (no .bak is made)
encode.exe "mstodo.conf" "Kopolop0_90u" "mstodo.enc"
```

Both arguments should be inside double quotes, which matters when a path has spaces or
a password has odd characters.

### Options

| option | meaning |
|---|---|
| `-f`, `--force` | overwrite an existing output file, or encrypt an already encrypted file |
| `--no-backup` | do not create the `.bak` file when writing in place |
| `-h`, `--help` | show help |

### Exit codes

| code | meaning |
|---|---|
| 0 | success |
| 1 | bad arguments |
| 2 | file could not be read or written |
| 3 | crypto error — wrong password, damaged file, or not an encrypted file |

## File format

All numbers are little-endian.

| offset | size | field |
|---:|---:|---|
| 0 | 8 | magic, the ASCII bytes `WASENC1\0` |
| 8 | 1 | KDF id — `1` = PBKDF2-HMAC-SHA256 |
| 9 | 4 | KDF iteration count, `uint32` |
| 13 | 16 | random salt |
| 29 | 12 | random nonce (the AES-GCM IV) |
| 41 | 16 | AES-GCM authentication tag |
| 57 | n | ciphertext |

The header is 57 bytes, so an encrypted file is exactly 57 bytes bigger than the
plain one.

The magic is what lets the wrapper decide: it reads the first 8 bytes of the `.conf`,
and only decrypts when they match. That is why plain and encrypted config files both
just work.

The iteration count is written into the header instead of being hardcoded, so files
made today can still be read if the default is raised in a future version.

The implementation is [`src/Shield.Crypto/ConfCrypto.cs`](../src/Shield.Crypto/ConfCrypto.cs)
— about 150 lines, using only `System.Security.Cryptography`. No third-party crypto
library is involved.

## Using a different password

Nothing stops you from encrypting a file with your own password, but the wrapper only
knows the built-in one, so it would not be able to read it. If you need a different
key, change `ConfigLoader.ConfPassword` in
[`src/WebAppShield/ConfigLoader.cs`](../src/WebAppShield/ConfigLoader.cs) and rebuild.
