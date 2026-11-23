# H2Sharp.Crypto

Optional `ENCRYPT` and `DECRYPT` SQL functions for H2Sharp. They are kept out of the engine:
encryption is an optional package (ADR-001), so the core stays free of cryptographic code and a
connection opts in explicitly before it is opened.

```csharp
using H2Sharp.Crypto;
using H2Sharp.Data;

using H2Connection connection = new("Data Source=app;Storage=Memory");
((IH2Extensions)connection).UseCrypto();
connection.Open();
```

```sql
CALL ENCRYPT('AES', 'my key', X'01020304');
CALL DECRYPT('AES', 'my key', X'...');
```

AES comes from the BCL; XTEA and FOG are implemented in this package. Both functions are
registered as ordinary H2Sharp UDFs, so they follow the engine's argument conversion, error
codes and transaction rules. See the repository's `docs/ADO_NET_GUIDE.md` for the extension
registration contract (closed, not disposed, connection: create → configure → Open).
