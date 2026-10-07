# UUID byte order in H2 vs .NET

Established empirically against the reference JVM (JDK 25) while porting
`org.h2.util.Bits`.

## H2 behaviour

`Bits.uuidToBytes(UUID uuid)` writes `getMostSignificantBits()` and
`getLeastSignificantBits()` **big-endian** via `MethodHandles.byteArrayViewVarHandle`.
The resulting 16 bytes are the canonical UUID **text order**.

Verified on the reference JVM:

| UUID text | `getMostSignificantBits()` | `getLeastSignificantBits()` | H2 bytes |
|---|---|---|---|
| `00000000-0000-0000-0000-000000000001` | `0` | `1` | `00000000000000000000000000000001` |
| `ffffffff-ffff-ffff-ffff-ffffffffffff` | `-1` | `-1` | `ffffffffffffffffffffffffffffffff` |
| `123e4567-e89b-12d3-a456-426614174000` | `1314564453825188563` | `-6605018797301088256` | `123e4567e89b12d3a456426614174000` |
| `80000000-0000-0000-0000-000000000000` | `-9223372036854775808` | `0` | `80000000000000000000000000000000` |
| `00000000-0001-0000-8000-000000000000` | `65536` | `-9223372036854775808` | `00000000000100008000000000000000` |

Note the sign: `getMostSignificantBits()` returns a **signed** `long`, so any UUID whose
`time_low` has the high bit set produces a negative value. Port accordingly — do not switch
to unsigned.

## .NET behaviour

`Guid.ToByteArray()` returns a different layout: `Data1`, `Data2`, `Data3` as
**little-endian**, then `Data4` verbatim. For `123e4567-e89b-12d3-a456-426614174000` that is
`67453e1239 9be8 d312 00 74 14 66 42 56 a4`, which is *not* the H2 wire format.

## Conclusion

Use `Guid.TryWriteBytes(span, bigEndian: true, out written)` and
`Guid.TryReadBytes(span, bigEndian: true, out uuid)` (.NET 8+). These produce/consume exactly
the canonical text order, so they match H2. `Guid.ToByteArray()` must not be used for
persistence.

`Guid.ToString()` on both runtimes produces the same text, so
`Guid.Parse`/`Guid.ToString()` round-trips across Java and .NET without conversion — that is
the property to rely on when a UUID is exposed as text (which is what `ValueUuid` does).

## Consequences elsewhere

* `ValueUuid` stores the 16 H2-format bytes and converts at the ADO.NET boundary.
* The conformance suite must compare UUIDs as text, never as `Guid.ToByteArray()`.