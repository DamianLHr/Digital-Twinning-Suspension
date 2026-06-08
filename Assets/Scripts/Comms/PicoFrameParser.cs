/// <summary>
/// Pure, streaming de-framer for inbound TwinData. Feed it bytes from any source
/// (a serial read, a test buffer, …) and it emits one TwinData each time a full
/// <c>0xAA 0xBB</c> + struct arrives.
///
/// Uses a ROLLING header match (state machine), so it is robust to junk between
/// frames and to a header byte appearing in the stream — e.g. <c>0xAA 0xAA 0xBB</c>
/// still frames correctly. Because it has no IO, it is unit-testable without a
/// serial port (this is the "framing codec" extracted out of PicoSerialTransport).
/// </summary>
public sealed class PicoFrameParser
{
    private enum State { Header0, Header1, Body }

    private State _state = State.Header0;
    private readonly byte[] _body;
    private int _got;

    public PicoFrameParser()
    {
        _body = new byte[PicoProtocol.SizeOf<TwinData>()];
    }

    /// <summary>Feed one byte. Returns true (and fills <paramref name="frame"/>) when a
    /// complete frame has just been parsed.</summary>
    public bool Feed(byte b, out TwinData frame)
    {
        frame = default;
        switch (_state)
        {
            case State.Header0:
                if (b == PicoProtocol.InHeader[0]) _state = State.Header1;
                return false;

            case State.Header1:
                if (b == PicoProtocol.InHeader[1]) { _state = State.Body; _got = 0; }
                else if (b == PicoProtocol.InHeader[0]) _state = State.Header1;  // stay: could be the real start byte
                else _state = State.Header0;
                return false;

            default: // Body
                _body[_got++] = b;
                if (_got >= _body.Length)
                {
                    _state = State.Header0;
                    frame = PicoProtocol.BytesToStruct<TwinData>(_body);
                    return true;
                }
                return false;
        }
    }

    /// <summary>Discard any partial-frame progress (e.g. after a reconnect).</summary>
    public void Reset()
    {
        _state = State.Header0;
        _got = 0;
    }
}
