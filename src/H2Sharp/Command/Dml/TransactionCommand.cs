/* Copyright 2004-2026 H2 Group. MPL 2.0 or EPL 1.0.
 * Ported from org.h2.command.dml.TransactionCommand. Copyright 2026 H2Sharp contributors. */
using H2Sharp.Engine;
using H2Sharp.Result;

namespace H2Sharp.Command.Dml;

public class TransactionCommand : Prepared
{
    private readonly int type;
    private string? savepointName;
    private string? transactionName;
    public TransactionCommand(SessionLocal session, int type) : base(session) => this.type = type;
    public void SetSavepointName(string name) => savepointName = name;
    public void SetTransactionName(string name) => transactionName = name;
    public override long Update()
    {
        SessionLocal session = GetSession();
        switch (type)
        {
            case CommandInterface.SetAutocommitTrue: session.SetAutoCommit(true); break;
            case CommandInterface.SetAutocommitFalse: session.SetAutoCommit(false); break;
            case CommandInterface.Begin: session.Begin(); break;
            case CommandInterface.Commit: session.Commit(false); break;
            case CommandInterface.Rollback: session.Rollback(); break;
            case CommandInterface.Savepoint: session.AddSavepoint(savepointName!); break;
            case CommandInterface.RollbackToSavepoint: session.RollbackToSavepoint(savepointName!); break;
            default: throw new TODOException("TransactionCommand.update: transaction command " + type + " is not ported (" + transactionName + ")");
        }
        return 0;
    }
    public override bool IsTransactional() => true;
    public override bool NeedRecompile() => false;
    public override ResultInterface? QueryMeta() => null;
    public override int GetCommandType() => type;
    public override bool IsCacheable() => true;
}
