#!/usr/bin/env python3

import argparse
import hashlib
import json
import os
import sys

MASK32 = 0xFFFFFFFF
KEY_LEN = 0x14

C_A0 = 0x9FB498B3
C_A1 = 0x66B0CD0D
C_A2_F = 0x7BF36AE2
C_A2 = 0xF33D5697
C_A3 = 0xD675E47B
C_A3_B = 0x59D148C0
C_W0 = 0xB453C259
C_W0_MASK = 0x22222222


def rol32(x, n):
    x &= MASK32
    return ((x << n) | (x >> (32 - n))) & MASK32


def mix_five_words(w):
    w0, w1, w2, w3, w4 = [x & MASK32 for x in w]

    a0 = (w0 + C_A0) & MASK32
    b0 = rol32(a0, 30)

    a1 = (w1 + C_A1 + rol32(a0, 5)) & MASK32
    b1 = rol32(a1, 30)

    f2 = (~(a0 & C_W0_MASK)) & C_A2_F
    a2 = (rol32(a1, 5) + w2 + f2 + C_A2) & MASK32
    b2 = rol32(a2, 30)

    f3 = (((b0 ^ C_A3_B) & a1) ^ C_A3_B)
    a3 = (rol32(a2, 5) + w3 + f3 + C_A3) & MASK32

    f4 = (((b0 ^ b1) & a2) ^ b0)
    n0 = (f4 + w0 + w4 + rol32(a3, 5) + C_W0) & MASK32
    n1 = (w1 + a3) & MASK32
    n2 = (w2 + b2) & MASK32
    n3 = (w3 + b1) & MASK32
    n4 = (w4 + b0) & MASK32
    return [n0, n1, n2, n3, n4]


def mix_five_words_asm_style(w):
    w0, w1, w2, w3, w4 = [x & MASK32 for x in w]
    r11, ebx, r10, r8, r15 = w0, w1, w2, w3, w4
    esi = (r11 + C_A0) & MASK32
    eax = rol32(esi, 5)
    edi = (ebx + C_A1) & MASK32
    edi = (edi + eax) & MASK32
    eax = esi & C_W0_MASK
    esi = rol32(esi, 30)
    eax = (~eax) & MASK32
    r9 = edi
    eax &= C_A2_F
    r9 = rol32(r9, 5)
    eax = (eax + C_A2) & MASK32
    r9 = (r9 + r10) & MASK32
    r9 = (r9 + eax) & MASK32
    eax = esi ^ C_A3_B
    edx = r9
    eax &= edi
    edx = rol32(edx, 5)
    eax ^= C_A3_B
    edi = rol32(edi, 30)
    eax = (eax + C_A3) & MASK32
    edx = (edx + r8) & MASK32
    edx = (edx + eax) & MASK32
    r8 = (r8 + edi) & MASK32
    ecx = rol32(edx, 5)
    ebx = (ebx + edx) & MASK32
    ecx = (ecx + C_W0) & MASK32
    eax = esi ^ edi
    eax &= r9
    r9 = rol32(r9, 30)
    eax ^= esi
    r10 = (r10 + r9) & MASK32
    eax = (eax + r15) & MASK32
    eax = (eax + r11) & MASK32
    eax = (eax + ecx) & MASK32
    r15 = (r15 + esi) & MASK32
    return [eax, ebx, r10, r8, r15]


def derive_key(key_bytes):
    buf = bytearray(KEY_LEN)
    for i, b in enumerate(key_bytes[:KEY_LEN]):
        buf[i] ^= b
    words = [int.from_bytes(buf[i * 4:i * 4 + 4], "little") for i in range(5)]
    mixed = mix_five_words(words)
    return b"".join(w.to_bytes(4, "little") for w in mixed)


def rc4_ksa(key_bytes):
    s = list(range(256))
    j = 0
    n = len(key_bytes)
    for i in range(256):
        j = (j + s[i] + key_bytes[i % n]) & 0xFF
        s[i], s[j] = s[j], s[i]
    return s


def key_schedule(key_bytes):
    if len(key_bytes) > KEY_LEN:
        key_bytes = key_bytes[:KEY_LEN]
    mixed = derive_key(key_bytes)
    return [0, 0, rc4_ksa(mixed)], mixed


def crypt_stream(state, data):
    i, j, s = state[0], state[1], state[2]
    out = bytearray(len(data))
    for k, b in enumerate(data):
        i = (i + 1) & 0xFF
        j = (j + s[i]) & 0xFF
        s[i], s[j] = s[j], s[i]
        out[k] = s[(s[i] + s[j]) & 0xFF] ^ b
    state[0], state[1] = i, j
    return bytes(out)


def _textbook_rc4(key_bytes, data):
    s = rc4_ksa(key_bytes)
    i = j = 0
    out = bytearray(len(data))
    for k, b in enumerate(data):
        i = (i + 1) & 0xFF
        j = (j + s[i]) & 0xFF
        s[i], s[j] = s[j], s[i]
        out[k] = s[(s[i] + s[j]) & 0xFF] ^ b
    return bytes(out)


def selftest():
    errors = []

    test_words = ([0, 0, 0, 0, 0], [0xFFFFFFFF] * 5,
                  [0x01020304, 0x05060708, 0x090A0B0C, 0x0D0E0F10, 0x11121314],
                  [0x9E3779B9, 0x00000001, 0x80000000, 0x7FFFFFFF, 0xDEADBEEF])
    for w in test_words:
        a = mix_five_words(w)
        b = mix_five_words_asm_style(w)
        if a != b:
            errors.append(f"mix expression != mutation form for {w}: {a} vs {b}")

    key = bytes(range(20))
    mixed = derive_key(key)
    s = rc4_ksa(mixed)
    keystream_state = [0, 0, list(s)]
    ours = crypt_stream(keystream_state, b"\x00" * 64)
    theirs = _textbook_rc4(mixed, b"\x00" * 64)
    if ours != theirs:
        errors.append("KSA/PRGA disagrees with textbook RC4 for derived key")

    for klen in (0, 1, 7, 8, 19, 20, 25):
        k = bytes((i * 7 + 3) & 0xFF for i in range(klen))
        st, _ = key_schedule(k)
        if st[2][0] < 0 or st[2][0] > 255:
            errors.append(f"S[0] out of range for keylen {klen}")
        if klen > 20 and derive_key(k) != derive_key(k[:20]):
            errors.append("over-long key did not clamp to 20")

    st1, _ = key_schedule(bytes(range(20)))
    whole = crypt_stream(st1, bytes(range(128)))
    st2, _ = key_schedule(bytes(range(20)))
    part = crypt_stream(st2, bytes(range(64))) + \
        crypt_stream(st2, bytes(range(64, 128)))
    if whole != part:
        errors.append("stream state did not carry across calls")

    return errors


def fixture_vector(key_bytes):
    state, mixed = key_schedule(key_bytes)
    sbox = bytes(state[2])
    keystream = crypt_stream(state, b"\x00" * 32)
    return {
        "inputKey": key_bytes.hex(),
        "mixedKey": mixed.hex(),
        "sboxAfterKsa": sbox.hex(),
        "keystream32": keystream.hex(),
    }


def build_fixture():
    vectors = [
        fixture_vector(bytes(range(20))),
        fixture_vector(b"\x00" * 20),
        fixture_vector(b"\xff" * 20),
    ]
    body = {
        "kind": "synthetic",
        "build": 208784,
        "buildLabel": "208784",
        "imageSha256": "4A220846C8136DAC90380AD0545304D6622A84CF6B0D45A46014212125C4A64D",
        "algorithm": "MsgUtil KSA 5-word mixing + RC4 KSA, PRGA",
        "provenance": "Generated by tools/re/Gw2TransportCipher.py from the "
                      "static reconstruction in the schema re-derivation.",
        "noClaim": "Synthetic reference vector. It tests an implementation of "
                   "the reconstruction and does not confirm the client's wire "
                   "output; that needs a captured frame.",
        "vectors": vectors,
    }
    canonical = json.dumps(body, sort_keys=True, separators=(",", ":")).encode()
    body["sha256"] = hashlib.sha256(canonical).hexdigest()
    return body


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--selftest", action="store_true")
    ap.add_argument("--emit-fixture", metavar="PATH")
    args = ap.parse_args()

    if args.selftest:
        errors = selftest()
        for e in errors:
            print("FAIL", e)
        if errors:
            return 1
        print("OK Gw2TransportCipher selftest (4 groups)")
        return 0

    if args.emit_fixture:
        fixture = build_fixture()
        os.makedirs(os.path.dirname(os.path.abspath(args.emit_fixture)),
                    exist_ok=True)
        with open(args.emit_fixture, "w", newline="\n") as f:
            json.dump(fixture, f, indent=2, sort_keys=True)
            f.write("\n")
        print(f"wrote {args.emit_fixture} sha256 {fixture['sha256']}")
        return 0

    ap.print_help()
    return 0


if __name__ == "__main__":
    sys.exit(main())
