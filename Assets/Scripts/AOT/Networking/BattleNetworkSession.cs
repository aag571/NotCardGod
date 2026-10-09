public sealed class BattleNetworkSession
{
    public long SessionId { get; private set; }

    private long nextSnapshotRevision;
    private long lastAppliedSnapshotRevision;
    private long nextLocalActionSequence;
    private long lastGuestActionSequence;

    public void Reset()
    {
        SessionId = 0L;
        nextSnapshotRevision = 0L;
        lastAppliedSnapshotRevision = 0L;
        nextLocalActionSequence = 0L;
        lastGuestActionSequence = 0L;
    }

    public void BeginHost(long sessionId)
    {
        Reset();
        SessionId = sessionId;
    }

    public void StampAction(BattleNetworkMessageData message)
    {
        message.startSequence = SessionId;
        message.actionSequence = ++nextLocalActionSequence;
    }

    public void StampSnapshot(BattleNetworkMessageData message)
    {
        message.startSequence = SessionId;
        message.snapshotRevision = ++nextSnapshotRevision;
    }

    public bool TryAcceptSnapshot(BattleNetworkMessageData message)
    {
        if (message == null || message.snapshot == null || message.startSequence <= 0L ||
            message.snapshotRevision <= 0L)
            return false;

        if (SessionId != 0L && message.startSequence != SessionId)
            return false;
        if (message.snapshotRevision <= lastAppliedSnapshotRevision)
            return false;

        SessionId = message.startSequence;
        lastAppliedSnapshotRevision = message.snapshotRevision;
        return true;
    }

    public bool TryAcceptGuestAction(BattleNetworkMessageData message)
    {
        if (message == null || message.action == null || message.startSequence != SessionId ||
            message.actionSequence <= lastGuestActionSequence)
            return false;

        lastGuestActionSequence = message.actionSequence;
        return true;
    }
}
