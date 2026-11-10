using System.Collections.Immutable;
using HamContest.Udc;

namespace HamContest.Tests;

public class SessionTests
{
    [Fact]
    public void RepeatsAndRestartKeepTheReservedSerial()
    {
        var rules = Fixtures.SerialRules();
        var session = new ContestSession(rules, Fixtures.Own, 7);
        var reservation = session.ReserveSerial();
        Assert.Equal(reservation, session.ReserveSerial());
        Assert.Equal(8, session.NextSerial);
        session = SessionStore.Deserialize(SessionStore.Serialize(session), rules);
        Assert.Equal(reservation, session.ReserveSerial());
        var contact = session.Log(Fixtures.Contact(), reservation);
        Assert.Equal(7, contact.Sent.Serial);
        Assert.Null(session.PendingSerial);
        Assert.Throws<InvalidOperationException>(() => session.Log(contact, reservation));
        Assert.Single(session.Contacts);
        Assert.Equal(8, session.ReserveSerial().Number);
    }

    [Fact]
    public void InvalidContactKeepsReservationAndDoesNotMutateJournal()
    {
        var session = new ContestSession(Fixtures.SerialRules(), Fixtures.Own);
        var reservation = session.ReserveSerial();
        Assert.Throws<ArgumentException>(() => session.Log(Fixtures.Contact("not a call"), reservation));
        Assert.Equal(reservation, session.PendingSerial);
        Assert.Empty(session.Contacts);
        Assert.Throws<InvalidOperationException>(() => session.Log(Fixtures.Contact(), new(Guid.NewGuid(), 1)));
        Assert.Throws<InvalidOperationException>(() => session.Log(Fixtures.Contact()));
    }

    [Fact]
    public void AbandonedAndDeletedSerialsAreNeverRecycled()
    {
        var session = new ContestSession(Fixtures.SerialRules(), Fixtures.Own);
        session.AbandonSerial(session.ReserveSerial());
        var second = session.ReserveSerial();
        var qso = session.Log(Fixtures.Contact(), second);
        Assert.Equal(2, qso.Sent.Serial);
        session.Remove(qso.Id);
        Assert.Equal(3, session.ReserveSerial().Number);
    }

    [Fact]
    public void EditingAndDeletionRecalculateDupesWithoutChangingTransmittedSerials()
    {
        var session = new ContestSession(Fixtures.SerialRules(), Fixtures.Own);
        var first = session.Log(Fixtures.Contact(), session.ReserveSerial());
        var second = session.Log(Fixtures.Contact(), session.ReserveSerial());
        Assert.True(session.Score().Contacts[1].IsDupe);
        Assert.Throws<ArgumentException>(() => session.Replace(first with { Sent = first.Sent with { Serial = 99 } }));
        session.Replace(second with { Station = second.Station with { Callsign = "DL2ABC" } });
        Assert.Equal(4, session.Score().Total);
        session.Replace(second);
        session.Remove(first.Id);
        Assert.False(session.Score().Contacts[0].IsDupe);
        Assert.Equal(2, session.Score().Total);
    }

    [Fact]
    public void RestoreRejectsChangedRulesCountersAndDuplicateIdentities()
    {
        var rules = Fixtures.SerialRules();
        var session = new ContestSession(rules, Fixtures.Own);
        var qso = session.Log(Fixtures.Contact(), session.ReserveSerial());
        var state = session.CaptureState();
        Assert.Throws<InvalidDataException>(() => ContestSession.Restore(Fixtures.Rules(("PointsPerContact", "99")), state));
        Assert.Throws<InvalidDataException>(() => ContestSession.Restore(rules, state with { NextSerial = 1 }));
        Assert.Throws<InvalidDataException>(() => ContestSession.Restore(rules, state with { Contacts = [qso, qso] }));
        Assert.Throws<InvalidDataException>(() => ContestSession.Restore(rules, state with { Version = 2 }));
        Assert.Throws<InvalidDataException>(() => ContestSession.Restore(rules, state with { Contacts = default }));
    }

    [Fact]
    public void AtomicFileSaveRestoresUtcAndPendingReservation()
    {
        string folder = Path.Combine(Path.GetTempPath(), "HamContest-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "session.json");
        try
        {
            var rules = Fixtures.SerialRules();
            var session = new ContestSession(rules, Fixtures.Own);
            var qso = Fixtures.Contact() with { Time = new(2026, 11, 6, 14, 0, 0, TimeSpan.FromHours(2)) };
            session.Log(qso, session.ReserveSerial());
            SessionStore.Save(path, session);
            var pending = session.ReserveSerial();
            SessionStore.Save(path, session);
            var loaded = SessionStore.Load(path, rules);
            Assert.Equal(TimeSpan.Zero, loaded.Contacts[0].Time.Offset);
            Assert.Equal(12, loaded.Contacts[0].Time.Hour);
            Assert.Equal(pending, loaded.PendingSerial);
            Assert.Single(Directory.GetFiles(folder));
            Assert.Equal(session.Score().Total, loaded.Score().Total);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void ConcurrentReservationCallsReturnOneNumber()
    {
        var session = new ContestSession(Fixtures.SerialRules(), Fixtures.Own);
        var results = Enumerable.Range(0, 40).AsParallel().Select(_ => session.ReserveSerial()).ToArray();
        Assert.Single(results.Distinct());
        Assert.Equal(2, session.NextSerial);
    }

    [Fact]
    public void InvalidFrequencyModeTimestampAndControlCharactersAreRejected()
    {
        var rules = Fixtures.Rules();
        foreach (var contact in new[]
        {
            Fixtures.Contact() with { FrequencyHz = 14080 }, Fixtures.Contact() with { Mode = (ContactMode)999 },
            Fixtures.Contact() with { Time = default }, Fixtures.Contact() with { Received = new() { Value = "DX\nQSO:" } }
        }) Assert.NotEmpty(rules.Validate(contact, Fixtures.Own));
    }
}
