# H2Sharp.Extensions

Optional user-defined aggregates for H2Sharp: examples of the aggregate extension points and a
small set of ready-to-use SQL aggregates. They are UDFs registered on a connection, not builtins,
so a program pays for them only when it asks for them.

| SQL name | Aggregate |
|---|---|
| `STRING_AGG` | concatenate values with a separator |
| `GEOMETRIC_MEAN` | geometric mean |
| `HARMONIC_MEAN` | harmonic mean |
| `PRODUCT` | product of values |
| `WEIGHTED_AVG` | weighted average |
| `COUNT_IF` | count rows matching a condition |

```csharp
using H2Sharp.Data;
using H2Sharp.Extensions;

using H2Connection connection = new("Data Source=app;Storage=Memory");
((IH2Extensions)connection).RegisterH2Aggregates();   // all of the above
connection.Open();
```

Individual aggregates can be registered with the matching `RegisterStringAgg`,
`RegisterGeometricMean`, `RegisterHarmonicMean`, `RegisterProduct`, `RegisterWeightedAverage`
or `RegisterCountIf` extension method; each accepts a SQL name and `AggregateOptions`. Every
provider implements `IH2AggregateProvider`, so an application can add its own aggregate through
the same contract rather than editing the engine.

The registration contract is the same as for the other optional packages: configure a closed,
not disposed, connection and only then call `Open()`.
