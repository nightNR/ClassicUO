# UOAM hub wire protocol (reverse-engineered)

Status: **position/map visibility and chat both work**, live-verified against a
real hub (`map.knuckleheads.dk:2000`, 2026-08-07). The key discovery: **chat
requires its own dedicated, separately-handshaken second TCP connection** - a
real client always opens two sockets (this document previously only accounted
for one), and every chat-family request this client sent on the primary
connection got an explicit RPC fault, no matter how byte-perfect, until it
started opening and binding a second connection the same way. See "Two
connections" below. Separately, the server can also fault a *fresh* primary
connection outright if it's reconnecting under the same player name too soon
after a previous session fully registered - see "Faults" below; this looks
like server-side session-cleanup timing, not a client bug, and is only
mitigated (not fixed) by backing off before retrying. Panic is **not**
covered — never captured, still unknown.

This document supersedes `src/uoamhub/*.c` as the source of truth for the client
implementation in `src/ClassicUO.Client/Network/UOAM/`. `uoamhub` is a from-scratch
*server* reimplementation; it only ever had to emit a handful of canned response
templates, never construct a valid client request. Its source explains the
response side reasonably well but says nothing about the real handshake or the
extra fields real "data" packets carry — those were only recoverable by capturing
an actual UOAM client's traffic (Wireshark, `tcp.port==2000`) and diffing/decoding
it byte-by-byte, cross-checked with `tshark -x`.

## Framing

Every packet, both directions:

```
offset 0      0x05                    magic
offset 1      0x00                    magic
offset 2      packet type (see below)
offset 3      0x03                    magic
offset 4      0x10                    magic
offset 5-7    0x00 0x00 0x00
offset 8-11   uint32 LE  total packet length (including this header)
offset 12-15  uint32 LE  sequence number (client picks it; server echoes it back)
```

A single `recv()` may return more than one packet back-to-back; split by reading
the length field at offset 8 repeatedly.

Packet types seen:

| Type (offset 2) | Direction | Meaning |
|---|---|---|
| `0x0b` | C→S | handshake (RPC bind) |
| `0x0c` | S→C | handshake ack (RPC bind_ack) |
| `0x0d` | S→C | handshake **nak** (bind_nak) — sent back if the handshake packet isn't byte-correct |
| `0x0e` | C→S | mandatory post-handshake "mystery" packet, purpose unconfirmed |
| `0x0f` | S→C | response to `0x0e` |
| `0x00` | C→S | data (position update / query-list / chat poll / chat send) |
| `0x02` | S→C | data response (ack / no-chat / player-list / chat-data) |
| `0x03` | S→C | DCE/RPC fault (see "Faults" below) |

These type values line up with DCE/RPC PDU type numbers (11=bind, 12=bind_ack,
13=bind_nak, 0=request, 2=response) — the protocol is built on an RPC/NDR-style
wire format (it embeds the well-known NDR transfer-syntax UUID
`8a885d04-1ceb-11c9-9fe8-08002b104860`), but it is **not** standard DCE/RPC framing
end to end — the outer header above is custom, only the embedded string/struct
encoding borrows NDR conventions (see "NDR-ish strings" below).

## Handshake (type `0x0b` → `0x0c`)

The request is a **fixed 116-byte byte template** — verified byte-identical across
two independent captures (different sessions, different local ports). Only the
sequence field (offset 12-15) varies; every other byte, including two embedded
GUIDs, is a protocol constant:

```
05 00 0b 03 10 00 00 00 74 00 00 00 <seq:4>     00 00 00 00
d0 16 d0 16 00 00 00 00 02 00 00 00 00 00 01 00
10 66 15 9e 5c 7b d2 11 b8 cf 00 80 c7 97 1b e1   <- GUID A (interface UUID, fixed)
01 00 00 00 04 5d 88 8a eb 1c c9 11 9f e8 08 00
2b 10 48 60 02 00 00 00 01 00 01 00 10 66 15 9e
5c 7b d2 11 b8 cf 00 80 c7 97 1b e1 01 00 00 00   <- GUID A repeated
2c 1c b7 6c 12 98 40 45 03 00 00 00 00 00 00 00
01 00 00 00
```

GUID A = `10 66 15 9e 5c 7b d2 11 b8 cf 00 80 c7 97 1b e1` appears twice, identical
in both captures → a fixed interface identifier, not per-session data. Offset
20-23 is the "master client id" (0 = primary connection; a secondary connection —
UOAM historically opens two TCP sockets per client — would echo the primary's
assigned id here instead, mirrored in `uoamhub.c`'s `master_id` concept. Not
implemented client-side; this client only opens one socket).

The ack response (`0x0c`) is 84 bytes and matches `uoamhub.c`'s
`packet_handshake_response` template closely: offset 20-23 = assigned
client/session id (uint32 LE — needed for the next packet), offset 24-31 =
listening port as ASCII (`"2000\0"`), rest is fixed/embeds the NDR GUID again. A
malformed handshake gets back type `0x0d` (bind_nak) instead, and the connection
is then unusable — this was the original symptom that led to the full
re-investigation (a naive 24-byte handshake without the RPC bind structure always
got `0x0d`).

## Mandatory post-handshake packet (type `0x0e`)

A real client always sends this immediately after the handshake ack, before any
position/list traffic. Skipping it against a real server (not `uoamhub`) leaves
the connection in a state where subsequent data packets appear to be silently
dropped — this was the second symptom (connect "succeeded" per the handshake
but nothing else ever worked).

Fixed 72-byte template, one dynamic field patched in:

```
05 00 0e 03 10 00 00 00 48 00 00 00 <seq:4>    d0 16 d0 16
<clientId:4>                        01 00 00 00 01 00 01 00
a2 a0 6e d7 f0 2c b9 46 a8 7b e4 22 77 bc 1e f2   <- ChatInterfaceGuid, fixed
01 00 00 00 04 5d 88 8a eb 1c c9 11 9f e8 08 00
2b 10 48 60 02 00 00 00
```

`clientId` = the id from the handshake ack. The 16-byte GUID at offset 32-47
was originally assumed to be a random per-call "activity id" (every capture
showed different, high-entropy-looking bytes... except it turned out to
actually be the *same* fixed value every time, once compared across enough
independent captures - see "Two connections" below for what it actually is).
The `0x0f` response to this is not parsed; nothing downstream depends on it -
this packet on the primary connection appears to be little more than a
capability probe; the interface it names only becomes usable via its own
connection, per below.

## Two connections - chat needs its own dedicated socket

**This is the single most important fact about this protocol that isn't
obvious from a short capture.** A real client always opens a *second* TCP
connection to the same server, bound to the *first* connection via its own
handshake, and uses it **exclusively for chat** (register/poll/send). Every
chat-family request sent on the primary connection gets an explicit RPC fault
response (see "Response discriminator" - byte 2 = `0x03`, byte 3 = `0x23`
instead of the usual `0x03`), *even when the bytes are a byte-for-byte replay
of a real client's own request that is known to have succeeded* in the capture
it was taken from. Position/list work fine on the primary connection alone;
chat does not work there at all, regardless of how correct the bytes are.

This was missed for a long time because a single-socket capture (just the
primary connection) looks completely self-contained - nothing about it
signals "there should be another connection". It was only found by capturing
**two real UOAM clients running side by side** and noticing each one actually
holds *three* TCP connections open for the whole session, not one - two of
which are for position/list-shaped operations and a third, distinctly-sized
one, is chat.

The second connection's handshake is the same 72-byte shape as the `0x0e`
"mystery" packet above, but:

- packet type is `0x0b` (a real handshake, not `0x0e`)
- offset 20-23 is **not** this connection's own client id - it's the
  **primary connection's** assigned client id (`uoamhub.c`'s documented
  "secondary connection" / `master_id` concept, confirmed live)
- the GUID at offset 32-47 is `ChatInterfaceGuid` (the same fixed value as the
  `0x0e` packet above) - this is the chat interface's actual abstract-syntax
  UUID, not a random per-call id as first assumed

```
05 00 0b 03 10 00 00 00 48 00 00 00 <seq:4>    d0 16 d0 16
<primary's clientId:4>              01 00 00 00 01 00 01 00
a2 a0 6e d7 f0 2c b9 46 a8 7b e4 22 77 bc 1e f2   <- ChatInterfaceGuid
01 00 00 00 04 5d 88 8a eb 1c c9 11 9f e8 08 00
2b 10 48 60 02 00 00 00
```

The response is a normal handshake ack (type `0x0c`) on this second
connection. Once acked, chat register/poll/send (see below) all work
correctly when sent on *this* connection - live-verified: a chat poll sent
here gets a normal 44-byte "NoChat" response, not a fault.

`seq` on this connection is local to it (a fresh TCP connection, its own
sequence space) - it does not need to continue from the primary connection's
counter.

## Data packets (type `0x00`, C→S)

Common prefix, 48 bytes:

```
offset 0-15    header (see Framing)
offset 16-19   uint32 LE = totalLength - 24     (redundant "stub length", always this — both directions, every packet type below)
offset 20-21   uint16 LE major
offset 22-23   uint16 LE minor
offset 24-43   password, ASCII, NUL-padded, 20 bytes
offset 44-47   4-byte marker (see below)
```

major/minor per operation:

| Operation | major | minor |
|---|---|---|
| Position update | 0 | 1 |
| Query list | 0 | 2 |
| Chat poll | 1 | 1 |
| Chat send (text or font/color) | 1 | 0 |

**The offset 44-47 marker** matches `uoamhub.c`'s `struct player_info.ip[4]`
position exactly (documented there as "the internal client's IP address, not
affected by NAT"). A real client sends its actual local IPv4 address there (e.g.
`0a 08 00 04` = `10.8.0.4` in one capture). The server does not appear to validate
it (`uoamhub.c` never reads it); this client sends the real local IP read from the
connected socket, falling back to zero if unavailable, to match real-client
behavior as closely as possible without relying on unverified server tolerance.

### Position update (140 bytes total)

Prefix (48) + a 92-byte block: `name[64]` (ASCII, NUL-terminated/padded) +
`reserved[12]` (always zero) + `position[16]`.

**Position field layout — the one place this deviates structurally from
`uoamhub.c`'s documented `struct` (which implies four same-sized fields in
x,y,z,map order):**

```
position[16]:
  byte 0-2    zero/reserved
  byte 3      Z  (signed, 1 byte)
  byte 4-7    X  (uint32 LE — only the low 16 bits are ever meaningfully nonzero)
  byte 8-11   Y  (uint32 LE)
  byte 12-15  Map/facet (uint32 LE, 0 = Felucca)
```

Verified: a captured real client's position (X=2882, Y=742, Z=102, Map=0) decodes
correctly with this layout, and a position sent by this project's own client
(X=1234, Y=5678, Z=5) round-tripped byte-exact through the live server's
query-list response.

### Query list (48 bytes total, no payload beyond the common prefix)

Response (`0x02`, "PlayerList"): 36-byte header + N × 92-byte player blocks (same
`name/reserved/position` shape as above) + 4-byte zero trailer.

```
offset 0-23    standard response header incl. offset16-19 length field
offset 24-27   uint32 LE player count
offset 28-31   uint32 LE = 1 (constant, unexplained, matches uoamhub.c's template)
offset 32-35   uint32 LE player count (duplicated)
offset 36..    N × 92-byte player blocks
offset (36+92N)..+4   4 zero bytes (trailer)
```

### Chat poll (75 bytes total for an 11-byte name, i.e. `password.Length` doesn't
affect size — size depends only on the player name length)

Prefix (48) + 4-byte constant `00 00 02 00` + player name as an
[NDR-ish string](#ndr-ish-strings) (unpadded, since it's the last field).

Response when nothing is pending ("NoChat"): fixed **44 bytes** total — note this
is *not* the 28 bytes `uoamhub.c`'s `packet_no_chat` constant implies; the real
server's no-chat response is a different, larger size.

Response when a message is pending ("ChatData"), verified against a real
"Ahoj svet.. pisem sem aby som napisal" message:

```
offset 0-23   standard response header
offset 24-27  uint32 LE subtype (1 = text, 3 = font/color)
offset 28-31  uint32 LE = 0x00020000 (constant)
offset 32..   sender name, NDR-ish string, padded to a 4-byte boundary
offset ..     message text, NDR-ish string, guarded by a 0x00020004 marker
                instead of the usual zero "offset" field (unexplained but
                consistent across every capture), unpadded (last field)
```

Font/color payloads (subtype 3) carry extra fields after the message text (RGB
color bytes, then a font-name string) that this client does not send or parse.

### Chat send (variable length)

Prefix (48) + 4 zero bytes + `uint32 LE subtype` (1 = text) + 4-byte constant
`00 00 02 00` + sender name as an NDR-ish string (**padded** to a 4-byte
boundary, since another field follows) + message text as an NDR-ish string
guarded by the `0x00020004` marker (unpadded, last field).

Verified byte-exact against 4 different real captured messages (including one
sent while testing that happened to describe changing font/color — that message
is still an ordinary subtype-1 text send; it has nothing to do with the actual
font/color-announce packet type, which is a structurally different, larger
payload this client never constructs).

Works live, sent on the dedicated chat socket (see "Two connections" above) -
sending it on the primary socket still faults, same as poll.

### Chat registration (subtype 2) - discovered, not needed

A real client also sends one more packet type this document didn't originally
cover: right after its *first* position update on the primary connection, it
sends a "registration" packet using the same major/minor (1/0) as chat send,
but subtype **2** instead of 1 (text) or 3 (font/color):

```
prefix(48) + 4 zero bytes + subtype(4, = 2) + 0x00020000 marker(4)
  + the player's own name as an unpadded NDR-ish string + a 10-byte zero trailer
```

The trailer's exact size is confirmed only for one captured name length (5
chars + NUL = 6); whether it scales with name length or is genuinely fixed at
10 bytes regardless is unconfirmed - there was only one example to derive it
from. The server's ack to a real client's registration is a plain 28-byte Ack
with offset20-23 = 1 (see the discriminator table below) - not a fault.

This was found by capturing a **fresh, cold-start** real client session
(Wireshark started before the client connected) and misread at the time as
the missing piece explaining chat faults - implementing and sending it
(byte-verified identical to the captured request) still faulted on its own,
which is what led to digging further and finding the real cause: chat needs
its own dedicated, separately-handshaken socket (see above). Once that socket
exists, chat poll/send work on it directly, with no registration packet at
all - `UOAMClient` does not send this packet. It's documented here only
because a real client still sends it (on the primary socket, redundantly, as
far as this client's behavior is concerned) and a future reader diffing
captures will otherwise wonder what it is.

## NDR-ish strings

Every embedded string (player name, chat text) uses the same three-`uint32`
prefix as a classic DCE/RPC NDR conformant-varying string:

```
uint32 LE  max_count      (chars including NUL)
uint32 LE  offset         (always 0 in every capture)
uint32 LE  actual_count   (same as max_count in every capture)
<max_count> bytes: ASCII chars + NUL terminator
```

Except the *message text* field in chat send/receive, whose middle `uint32` is
the constant `0x00020004` instead of `0`. Genuinely unexplained; every capture
across two independent sessions agrees on this value, so it's treated as a fixed
protocol constant rather than a real "offset".

Padding: if another field follows an NDR-ish string in the same packet, the
string block (12-byte header + `max_count` bytes) is padded with zero bytes up to
the next 4-byte boundary. If the string is the *last* field in the packet, no
trailing padding is added (the packet just ends there — this is why real packet
lengths are frequently *not* multiples of 4).

## Response discriminator (type `0x02` packets)

`uoamhub.c`'s `respond()` unconditionally overwrites offset 16-19 of every
type-`0x02` response with `(total_length - 24)` — **this field can never be used
to distinguish response subtypes**, despite `uoamhub.c`'s static template arrays
appearing to hardcode different "marker" values there (`0x04`, `0x14`,
`0xffffffff`) before that overwrite runs. That appearance is what caused the
first implementation of this client's `Classify()` to be wrong from the start.

The actual discriminator is total length first, then offset 20-23 (`marker20`):
a plain Ack is **always exactly 28 bytes regardless of marker20** - a chat
registration/send ack uses marker20=1, a position/list ack uses marker20=0,
same meaning either way. Only once length rules out 28 does marker20 matter:

| length | marker20 | Meaning |
|---|---|---|
| 28 | 0 or 1 | Ack |
| 44 | 1 | NoChat |
| anything else | 0 | PlayerList |
| anything else | 1 | ChatData |

## Faults (type `0x03`, S→C)

A genuine DCE/RPC fault PDU. Two distinct causes have been observed live, both
classified the same way (`UOAMResponseKind.Fault`) since either means the
connection is over:

1. **Chat sent on the primary connection** (see "Two connections" above) -
   every chat-family request (poll/register/send) faults when sent on the
   wrong socket, byte-for-byte-identical bytes notwithstanding.
2. **Reconnecting under the same player name too soon** after a previous
   session fully registered with the server. Captured live (`UOAM7.pcapng`,
   2026-08-07): a fresh primary connection's handshake, mystery packet, and
   position update all succeeded normally, but the very next request (query
   list) got this back instead of a player list:

   ```
   05 00 03 03 10 00 00 00 20 00 00 00 03 00 00 00
   20 00 00 00 00 00 00 00 0b 00 01 1c 00 00 00 00
   ```

   offset 24-27 is an NCA-style status code (`0x1c01000b` here - high word
   `0x1c01` is the standard "Win32 error mapped into NCA status space" prefix,
   low word `0x000b` = Win32 `ERROR_BAD_FORMAT`). The server then closes the
   connection with a FIN a few milliseconds later. In the same capture, a
   short-lived connection that itself faulted immediately was followed 8
   seconds later by a full, stable reconnect under the same name - but a
   *fully-registered* 38-second session (during which chat worked, messages
   were sent) was followed by two more reconnect attempts, 8 and 16 seconds
   after its own graceful disconnect, that both faulted the same way. This
   suggests the server takes longer to release a player-name slot after a
   session that actually registered than after one that never got that far -
   exact cooldown unconfirmed, but it's longer than ~16 seconds.

   This is server-side state, not a malformed request - the exact same
   request bytes succeed in other connections in the same capture. There is
   no known client-side fix beyond backing off before retrying under the same
   name; `UOAMManager`'s existing exponential-backoff reconnect (see
   `SustainedConnectionMs` in `UOAMManager.cs`) already does this for
   *automatic* reconnects, but a **manual** disconnect+reconnect bypasses it
   entirely and can hit this immediately.

Detecting a fault as soon as it arrives (rather than waiting for a subsequent
`Send()` to throw `IOException`/`SocketException`, which is how this was
originally noticed - the OS doesn't surface the server's FIN as a local error
until the next write) lets the reconnect/backoff timer start a couple of
seconds sooner. See `UOAMSocketChannel.Fault()`.

## Disconnect (no explicit logout packet)

There is no application-layer "I'm leaving" packet. Confirmed live
(`UOAM9.pcapng`, 2026-08-07, two real UOAM.exe instances, each disconnected via
its own UI button and then the app closed): every one of a client's sockets
gets an ordinary `RST` (`0x0014` TCP flags), all fired within the same
millisecond, with nothing but normal traffic (position updates, chat polls)
right up to it - no special packet precedes the reset. A real client
apparently never performs a graceful TCP close (`FIN`) at all, not even via
its own explicit disconnect button.

This client originally closed gracefully (`FIN`) on disconnect, which appeared
to make the hub linger on removing it from other clients' player lists longer
than a real client's abrupt `RST` does. `UOAMSocketChannel` now opens its
`TcpSocketWrapper` with `abortiveClose: true` (`Socket.LingerState =
new LingerOption(true, 0)`), so `Disconnect()`/`Dispose()` sends `RST` instead
of `FIN`, matching the real client's behavior. Only the UOAM connections do
this - the main game server connection is unaffected.

## Panic

Never captured — out of scope. See `docs/superpowers/specs/2026-08-06-uoam-integration-design.md`
for the earlier (still valid) reasoning on why this was deferred: the panic flag's
exact bit/offset within a `player_info` block is unconfirmed, and guessing here
would be worse than not implementing it, since a wrong guess silently corrupts
position data other clients read.

## Source of the captures

Two independent Wireshark captures against `map.knuckleheads.dk:2000` (a live,
real hub — not `uoamhub`), using a real UOAM client:

1. `uoam.pcap` (classic pcap) — handshake, position update loop, query-list, one
   font/color announce, no chat text.
2. `UOAM2.pcapng` (pcapng) — same session shape, plus 4 real chat text sends
   (used to derive/verify the chat send and chat-poll-response formats).

Both parsed with small custom Python scripts (no `scapy`/`dpkt`/tshark dependency
assumed, though `tshark -x` was used once to cross-check the manual byte parsing
against packet #100 of `UOAM2.pcapng` and matched exactly).
