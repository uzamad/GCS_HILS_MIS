#!/usr/bin/env python3
"""
diag_decode.py  — GCS_240626 Telemetry Decoder Diagnostic
============================================================
Paste raw bytes from the Packet Monitor for Packet 1 and this script
will decode every HR1 field, showing raw bytes alongside eng values.

Usage:
    python diag_decode.py

Paste the full 164-byte hex string when prompted (spaces OK).
"""

import sys

def u(pkt, off, n, mn, mx):
    """Read n bytes little-endian unsigned, convert to eng value."""
    raw = sum(pkt[off+i] << (8*i) for i in range(n))
    maxRaw = (1 << (n*8)) - 1
    eng = raw / maxRaw * (mx - mn) + mn
    return raw, eng

def hex_at(pkt, off, n):
    return ' '.join(f'{pkt[off+i]:02X}' for i in range(n))

BANNER = """
╔══════════════════════════════════════════════════════════════════╗
║          GCS_240626 — HR1 Decode Diagnostic                     ║
║  Paste full 164-byte Packet 1 hex (from Packet Monitor)         ║
╚══════════════════════════════════════════════════════════════════╝
"""
print(BANNER)
raw_in = input("Paste Packet 1 hex bytes (164 bytes = 328 hex chars, spaces OK):\n> ").strip()
hex_clean = raw_in.replace(' ','').replace('\n','').replace(':','')
if len(hex_clean) < 8:
    print("Need at least the first 4 header bytes.")
    sys.exit(1)

pkt = bytes.fromhex(hex_clean)
print(f"\nReceived {len(pkt)} bytes.")

# Verify header
if len(pkt) >= 4:
    if pkt[0]==0xAA and pkt[1]==0x55 and pkt[2]==0xDD:
        print(f"  Header: AA 55 DD {pkt[3]:02X}  ✓  (Packet #{pkt[3]})")
        if pkt[3] != 1:
            print("  ⚠ This is not Packet 1 — HR1 fields are only in ODD packets.")
    else:
        print(f"  ⚠ Header mismatch: {pkt[0]:02X} {pkt[1]:02X} {pkt[2]:02X} {pkt[3]:02X}")

if len(pkt) < 90:
    print(f"  ⚠ Only {len(pkt)} bytes — need at least 90 to decode HR1.")
    sys.exit(1)

print()
print("══════════════════ HALF RATE FRAME 1 [62-89] ══════════════════")
print(f"  {'Field':<22} {'Bytes':>16}  {'Raw':>7}  {'Decoded':>14}  {'ICD Range'}")
print("  " + "─"*80)

fields = [
    ("Heading",           62, 2,     0,    360,  "°"),
    ("Latitude",          64, 4,   -90,     90,  "°"),
    ("Longitude",         68, 4,  -180,    180,  "°"),
    ("CAS",               72, 1,     0,    100,  "m/s"),
    ("PS",                73, 2, 22600, 108000,  "Pa"),
    ("PD",                75, 1,     0,   6400,  "Pa"),
    ("TAS",               76, 1,     0,    100,  "m/s"),
    ("Hp",                77, 2, -1000,  11000,  "m"),
    ("Throttle_com",      79, 1,     0,      1,  "pu"),
    ("Throttle_pos",      80, 1,     0,      1,  "pu"),
    ("Radar_Alt",         81, 2,     0,    100,  "m"),
    ("Psi_Com",           83, 2,     0,    360,  "°"),
    ("CAS_Com",           85, 1,     0,    100,  "m/s"),
    ("Alt_Com",           86, 1,     0,  11000,  "m"),
    ("Slew_Rate_Alt_Com", 87, 1,     0,  11000,  "m"),
    ("AGL",               88, 2,     0,   1000,  "m"),
]

for name, off, n, mn, mx, unit in fields:
    raw, eng = u(pkt, off, n, mn, mx)
    hexbytes = hex_at(pkt, off, n)
    print(f"  {name:<22} [{hexbytes:>16}]  {raw:>7}  {eng:>12.2f} {unit}  [{mn}, {mx}]")

print()
print("══════════════════ REVERSE CHECK ══════════════════════════════")
print("  What raw byte values would be needed to show given 'actual' values?")
print()

actuals = [
    ("Heading",  26.46,  0,   360, 62, 2),
    ("CAS",      64.56,  0,   100, 72, 1),
    ("Hp",     1032.00, -1000, 11000, 77, 2),
]
for name, actual, mn, mx, off, n in actuals:
    maxRaw = (1 << (n*8)) - 1
    raw_needed = round((actual - mn) / (mx - mn) * maxRaw)
    be = [(raw_needed >> (8*i)) & 0xFF for i in range(n)]
    le = list(reversed(be))
    got_raw, got_eng = u(pkt, off, n, mn, mx)
    print(f"  {name:<14}  actual={actual:>9.2f}  →  raw_needed={raw_needed:>6}  "
          f"LE bytes=[{' '.join(f'{b:02X}' for b in le)}]")
    print(f"  {'':14}  packet has raw={got_raw:>6}  →  decoded={got_eng:.2f}  "
          f"(bytes at [{off}-{off+n-1}]=[{hex_at(pkt,off,n)}])")
    if abs(got_eng - actual) < 1.0:
        print(f"  {'':14}  ✓ Close match!")
    else:
        diff = got_eng - actual
        print(f"  {'':14}  ✗ Discrepancy = {diff:+.1f}  — {'ENCODER BUG' if raw_needed != got_raw else 'DECODER CHECK OK'}")
    print()

print("══════════════════ RAW BYTE HEX DUMP [60-90] ══════════════════")
for i in range(60, min(91, len(pkt))):
    if i % 16 == 0: print(f"\n  [{i:03X}]  ", end="")
    print(f"{pkt[i]:02X} ", end="")
print("\n")
