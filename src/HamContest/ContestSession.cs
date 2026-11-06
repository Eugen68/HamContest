using System.Collections.Immutable;

namespace HamContest;

public sealed record SerialReservation(Guid Id, int Number);
public sealed record SessionState(int Version, string DefinitionId, StationInfo OwnStation, int NextSerial,
    SerialReservation? PendingSerial, ImmutableArray<Qso> Contacts);

/// <summary>A single-station journal. Calls are synchronized; returned state is immutable.</summary>
public sealed class ContestSession
{
    private readonly object _gate = new();
    private ImmutableArray<Qso> _contacts = [];
    private int _nextSerial;
    private SerialReservation? _pending;
    public IContestRules Rules { get; }
    public StationInfo OwnStation { get; }
    public ImmutableArray<Qso> Contacts { get { lock (_gate) return _contacts; } }
    public int NextSerial { get { lock (_gate) return _nextSerial; } }
    public SerialReservation? PendingSerial { get { lock (_gate) return _pending; } }

    public ContestSession(IContestRules rules, StationInfo ownStation, int firstSerial = 1)
    {
        ArgumentNullException.ThrowIfNull(rules); ArgumentNullException.ThrowIfNull(ownStation);
        if (!Values.IsCallsign(ownStation.Callsign)) throw new ArgumentException("Invalid station callsign.", nameof(ownStation));
        if (firstSerial < 1) throw new ArgumentOutOfRangeException(nameof(firstSerial));
        Rules = rules; OwnStation = ownStation with { Callsign = Values.Key(ownStation.Callsign) }; _nextSerial = firstSerial;
    }

    /// <summary>Repeat calls return the same pending serial. Save the session BEFORE transmitting it.</summary>
    public SerialReservation ReserveSerial()
    {
        lock (_gate)
        {
            if (!Rules.UsesSerialNumbers) throw new InvalidOperationException("This definition does not use serial numbers.");
            if (_pending != null) return _pending;
            if (_nextSerial == int.MaxValue) throw new InvalidOperationException("Serial sequence exhausted.");
            _pending = new(Guid.NewGuid(), _nextSerial++);
            return _pending;
        }
    }

    /// <summary>Abandoning a contact never recycles a number that might already have been transmitted.</summary>
    public void AbandonSerial(SerialReservation reservation)
    {
        lock (_gate) { RequirePending(reservation); _pending = null; }
    }

    public Qso Log(Qso contact, SerialReservation? reservation = null)
    {
        ArgumentNullException.ThrowIfNull(contact);
        lock (_gate)
        {
            if (_contacts.Any(q => q.Id == contact.Id)) throw new InvalidOperationException("QSO already logged with this identity.");
            if (Rules.UsesSerialNumbers)
            {
                if (reservation == null) throw new InvalidOperationException("Reserve and persist a serial before logging.");
                RequirePending(reservation);
                if (contact.Sent.Serial != null && contact.Sent.Serial != reservation.Number) throw new ArgumentException("Sent serial differs from reservation.", nameof(contact));
                contact = contact with { Sent = contact.Sent with { Serial = reservation.Number } };
            }
            else if (reservation != null || contact.Sent.Serial != null)
                throw new ArgumentException("This definition does not use a transmitted serial.", nameof(contact));
            contact = Normalize(contact);
            Validate(contact);
            _contacts = _contacts.Add(contact);
            if (reservation != null) _pending = null;
            return contact;
        }
    }

    public void Replace(Qso contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        lock (_gate)
        {
            int index = IndexOf(contact.Id);
            if (_contacts[index].Sent.Serial != contact.Sent.Serial) throw new ArgumentException("An already transmitted serial cannot be changed.", nameof(contact));
            contact = Normalize(contact); Validate(contact);
            _contacts = _contacts.SetItem(index, contact);
        }
    }

    public void Remove(Guid id) { lock (_gate) _contacts = _contacts.RemoveAt(IndexOf(id)); }
    public ContestScore Score() { lock (_gate) return Rules.Score(_contacts, OwnStation); }
    public SessionState CaptureState() { lock (_gate) return new(1, Rules.DefinitionId, OwnStation, _nextSerial, _pending, _contacts); }

    public static ContestSession Restore(IContestRules rules, SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != 1 || state.DefinitionId != rules.DefinitionId || state.Contacts.IsDefault)
            throw new InvalidDataException("Unknown session version, changed contest definition or missing contacts.");
        var session = new ContestSession(rules, state.OwnStation, state.NextSerial);
        var ids = new HashSet<Guid>();
        var serials = new HashSet<int>();
        foreach (var contact in state.Contacts)
        {
            session.Validate(contact);
            if (!ids.Add(contact.Id)) throw new InvalidDataException("Duplicate QSO identity in saved session.");
            if (contact.Sent.Serial is { } number && (!rules.UsesSerialNumbers || !serials.Add(number) || number >= state.NextSerial))
                throw new InvalidDataException("Reused serial or invalid next serial in saved session.");
        }
        if (state.PendingSerial is { } pending && (!rules.UsesSerialNumbers || pending.Id == Guid.Empty || pending.Number < 1
            || pending.Number != state.NextSerial - 1 || serials.Contains(pending.Number)))
            throw new InvalidDataException("Invalid pending serial reservation.");
        session._contacts = state.Contacts.Select(Normalize).ToImmutableArray();
        session._pending = state.PendingSerial;
        return session;
    }

    private int IndexOf(Guid id)
    {
        for (int i = 0; i < _contacts.Length; i++) if (_contacts[i].Id == id) return i;
        throw new KeyNotFoundException("QSO not found.");
    }
    private void RequirePending(SerialReservation reservation)
    {
        if (_pending == null || _pending != reservation) throw new InvalidOperationException("Serial reservation is not active in this session.");
    }
    private void Validate(Qso contact)
    {
        var errors = Rules.Validate(contact, OwnStation);
        if (errors.Length != 0) throw new ArgumentException(string.Join("; ", errors.Select(e => e.Field + ": " + e.Message)), nameof(contact));
    }
    private static Qso Normalize(Qso qso) => qso with
    {
        Time = qso.Time.ToUniversalTime(), Station = qso.Station with { Callsign = Values.Key(qso.Station.Callsign) }
    };
}
