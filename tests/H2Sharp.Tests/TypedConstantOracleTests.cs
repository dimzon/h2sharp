/* Copyright 2026 H2Sharp contributors. MPL 2.0 or EPL 1.0.
 * Numeric typed constants: fold once, retain SQL type, reuse the converted value. */
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using H2Sharp.Command;
using H2Sharp.Data;
using H2Sharp.Engine;
using H2Sharp.Expressions;
using H2Sharp.Expressions.Functions;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

[Collection(OracleCollection.Name)]
public sealed class TypedConstantOracleTests
{
    [JavaOracleFact]
    public void TypedCastsEvaluateOnceAndKeepTheReferenceType()
    {
        string[] types = ["NUMERIC(10,2)", "REAL", "DOUBLE PRECISION", "BIGINT", "SMALLINT", "TINYINT"];
        SessionLocal session = new(new Database(ConnectionInfo.Memory("typed_constant_fold")), "sa")
        {
            CastDataProvider = new TestCastDataProvider(),
            CompareMode = CompareMode.GetInstance(null, 0),
        };
        List<string> port = [];
        try
        {
            foreach (string type in types)
            {
                TypeInfo target = new Parser(session).ParseExpression("CAST(1 AS " + type + ")").GetTypeInfo();
                CountingLiteral source = new(ValueInteger.Get(1));
                Expression folded = new CastSpecification(source, target).Optimize(session);
                Assert.IsAssignableFrom<ValueExpression>(folded);
                Assert.Equal(1, source.Calls);
                Assert.Equal(target, folded.GetTypeInfo());
                Value value = folded.GetValue(session);
                for (int i = 0; i < 100; i++) Assert.Same(value, folded.GetValue(session));
                Assert.Equal(1, source.Calls);
                port.Add("=" + source.Calls + ":" + folded.GetType().Name + ":"
                    + target.GetValueType() + ":" + target.GetPrecision() + ":" + target.GetScale() + ":" + value.GetString());
            }
        }
        finally { session.Close(); }
        List<string> java = OracleRunner.Run(types, """
            try(java.sql.Connection c=java.sql.DriverManager.getConnection("jdbc:h2:mem:typed_constant_fold")) {
              org.h2.engine.SessionLocal s=(org.h2.engine.SessionLocal)((org.h2.jdbc.JdbcConnection)c).getSession();
              for(String type:inputs) {
                org.h2.value.TypeInfo target=new org.h2.command.Parser(s).parseExpression("CAST(1 AS "+type+")").getType();
                int[] calls={0};
                ValueExpression source=new ValueExpression(org.h2.value.ValueInteger.get(1)) {
                  @Override public org.h2.value.Value getValue(org.h2.engine.SessionLocal session) {
                    calls[0]++; return super.getValue(session);
                  }
                };
                Expression folded=new org.h2.expression.function.CastSpecification(source,target).optimize(s);
                org.h2.value.Value value=folded.getValue(s);
                for(int i=0;i<100;i++) if(value!=folded.getValue(s)) throw new AssertionError("Converted again");
                System.out.println("="+calls[0]+":"+folded.getClass().getSimpleName()+":"+target.getValueType()+":"
                  +target.getPrecision()+":"+target.getScale()+":"+value.getString());
              }
            }
            """, numbers: null, extraJavaArgs: ["-Duser.language=en"], packageName: "org.h2.expression");
        Assert.Equal(java, port);
    }

    [JavaOracleFact]
    public void BigIntegerDialectUsesTheReferencesNumericLiterals()
    {
        string[] values = ["0", "1", "-1", "2147483648", "-9223372036854775808", "9223372036854775808"];
        List<string> port = [];
        foreach (string text in values)
            port.Add(OracleRunner.Hex(Dialect.AppendBigInteger(new StringBuilder(), BigInteger.Parse(text)).ToString()));
        List<string> java = OracleRunner.Run(values, """
            for(String text:inputs) {
              org.h2.value.ValueNumeric v=org.h2.value.ValueNumeric.get(new java.math.BigInteger(text));
              System.out.println(hex(v.getSQL(0)));
            }
            """);
        Assert.Equal(java, port);
    }

    private sealed class CountingLiteral : ValueExpression
    {
        internal int Calls;
        internal CountingLiteral(Value value) : base(value) { }
        public override Value GetValue(SessionLocal session)
        {
            Calls++;
            return base.GetValue(session);
        }
    }
}
