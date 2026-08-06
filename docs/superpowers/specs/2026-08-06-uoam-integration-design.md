# UOAM Integration Design

Dátum: 2026-08-06
Status: Approved (pending spec self-review)

## Cieľ

ClassicUO sa vie pripojiť na externý UOAM hub server (`E:\Projects\uoamhub`, protokol
odvodený z `src/uoamhub.c` v tom repozitári) a využiť ho na:

1. zobrazenie ostatných hráčov (z toho istého UOAM "domain"/hesla) vo `WorldMapGump`,
2. samostatný chat medzi UOAM klientmi (nezávislý od herného chatu),
3. tracking vybraného UOAM hráča na mape.

Panic funkcia (UOAM klient posiela pri stlačení space "panic!" príznak) je **mimo
scope** tejto iterácie — presný bit/offset v `player_info.noip.reserved[12]` nie je
overený (žiadny packet capture k dispozícii), implementuje sa neskôr, keď bude možné
testovať proti reálnemu prenosu. Pozri "Future work" nižšie.

## Protokol (zhrnutie z `uoamhub.c`)

TCP, vlastná binárna hlavička na každom pakete:

```
offset 0-1   0x05 0x00                (magic)
offset 2     packet type              (0x0b handshake, 0x0e unknown-ack, 0x00 data)
offset 3-4   0x03 0x10                (magic)
offset 8-11  uint32 LE packet length  (celková dĺžka vrátane hlavičky)
offset 12-15 uint32 LE sequence       (echo-back)
```

- **Handshake**: prvý paket v spojení má `data[2]==0x0b`. Server odpovie fixnou 60-bajtovou
  handshake-response štruktúrou (obsahuje port servera ako ASCII na offsete 26). Sekundárne
  socket-y (viacero spojení pod jedným klientom) posielajú master client id na offsete 20 —
  **nepoužívame** (jeden socket na klienta stačí).
- **Position update** (`data[2]==0x00`, `data[20]==0x00`, `data[22]!=0x02`, dĺžka `0x8c`):
  `player_info` na offsete 44:
  - `ip[4]` — interná IP klienta (môže byť `0.0.0.0`, server to nevaliduje)
  - `noip.name[64]` — meno hráča, NUL-terminated, NUL musí byť prítomný v poli
  - `noip.reserved[12]` — nepoužité v tejto iterácii (budúci panic flag)
  - `noip.position[16]` — **predpoklad**: 4× `uint32` LE = `x, y, z, map`. Neoverené proti
    živému hubu, treba korigovať pri prvom reálnom teste.
  - heslo (`domain` password) je na offsete 24, 20 bajtov, NUL-terminated, posiela sa v
    **každom** `0x00` pakete (server ho použije len na prvé prihlásenie po handshake).
- **Query list** (`data[22]==0x02`): request bez tela, server odpovie zoznamom `noip`
  blokov (92B každý: 64+12+16) pripojených hráčov v tej istej doméne.
- **Chat poll** (`data[20]==0x01, data[22]==0x01`): request bez tela, server odpovie buď
  "no chat" alebo jedným queued chat itemom (text alebo font/color, rozlíšené `data[52]`
  sub-typom v enqueued dátach: `0x01`=text, `0x03`=font/color). Treba pollovať opakovane
  (žiadny push).
- **Chat send** (`data[20]==0x01, data[22]==0x00`): payload od offsetu 52, sub-type `0x01`
  = plain text správa, broadcast všetkým v doméne (server ju enqueue-ne do každého
  klienta's `chats[]` frontu, poll ju vyzdvihne).
- Server timeout klienta je 60s bez akéhokoľvek requestu → treba periodický keepalive
  (napr. position update alebo chat-poll aspoň raz za ~30-45s).

## 1. `UOAMClient` (`src/ClassicUO.Client/Network/UOAM/UOAMClient.cs`)

- Vlastný socket, rovnaký `TcpSocketWrapper` typ ako používa hlavný `NetClient`
  (`src/ClassicUO.Client/Network/Socket/TcpSocketWrapper.cs`), no **samostatná inštancia**
  — nezávislé od herného spojenia (iný server/port).
- Polling v game-loope (žiadne blocking calls, žiadny vlastný thread) — pripojí sa cez
  `GameController`/`GameScene` update tick, podobne ako `NetClient` je pumpovaný z
  hlavnej slučky.
- Framing zhodný s `client_data_available` v `uoamhub.c`: číta sa do bufferu, packet
  dĺžka na offsete 8, spracuje sa viac paketov naraz ak prišli v jednom recv.
- Verejné API (návrh): `Connect(ip, port, name, password)`, `Disconnect()`,
  `SendPositionUpdate(x, y, z, map)`, `SendChatText(string)`, `PollChat()` (periodicky),
  `PollList()` (periodicky), eventy `PlayerListUpdated`, `ChatReceived`,
  `ConnectionStateChanged`.
- Position update sa posiela periodicky (kým je pripojený a `World.InGame`), napr. pri
  zmene pozície hráča alebo aspoň raz za pevný interval (aby fungoval aj keepalive).

## 2. Lifecycle

- UOAM spojenie existuje **iba počas hry** (po vstupe do `GameScene`/`World.InGame`).
  Nezávisí od stavu herného spojenia mimo toho, že sa neaktivuje pred loginom a
  ukončí sa pri logout/exit.
- **Autoconnect**: ak `UOAMAutoConnect` je zapnutý, connect prebehne automaticky hneď
  po vstupe do sveta (s uloženými menom/IP/portom/heslom z profilu).
- **Manuálne connect/disconnect** — tri rovnocenné ovládacie prvky:
  1. Connect/Disconnect tlačidlo v UOAM options tabe (zobrazuje aktuálny stav:
     Disconnected / Connecting… / Connected).
  2. Nový `MacroType.UOAMConnect` / `MacroType.UOAMDisconnect` (bindovateľné na klávesu
     cez existujúci macro systém, `MacroManager.cs`).
  3. Položka v right-click context menu `WorldMapGump` (vzor existujúcich
     `_options[...]` entries).

## 3. Options (nový tab v `OptionsGump.cs`)

Nové polia v `ProfileManager.CurrentProfile` (analogicky k existujúcim string/bool
profile properties):

- `UOAMPlayerName` (string)
- `UOAMServerIP` (string)
- `UOAMServerPort` (ushort, default 2000 — zhoduje sa s uoamhub default portom)
- `UOAMPassword` (string, uložené ako je — žiadne extra šifrovanie nad rámec existujúcich
  profile-uložených citlivých polí v projekte)
- `UOAMAutoConnect` (bool)
- `UOAMAutoOpenChat` (bool)
- `UOAMNameAliases` (string, delimited zoznam `uoamName=charName` párov — vzor uloženia
  podobný `WorldMapHiddenMarkerFiles` v `WorldMapGump.cs`)

UI v novom tabe:
- textbox Meno, IP, Port, Password (masked)
- checkbox AutoConnect, checkbox AutoOpenChat
- Connect/Disconnect tlačidlo so stavovým textom
- editovateľný zoznam aliasov: riadky (UOAM meno → herné meno) s "+ Add" a per-riadok
  remove tlačidlom

## 4. Chat — `UOAMChatGump`

- Nová trieda, vizuálne klon `JournalGump` (`ExpandableScroll` + `RenderedTextList`),
  navyše riadok s textovým inputom (`StbTextBox` alebo ekvivalent použitý inde v UI) na
  spodku okna. Enter = odošle text cez `UOAMClient.SendChatText`.
- Prichádzajúce správy (z `PollChat`) sa pridávajú do `RenderedTextList` rovnako ako
  journal entries.
- `AutoOpenChat` toggle: gump sa otvorí automaticky pri úspešnom connecte (autoconnect
  aj manuálny). Zatvorenie/otvorenie inak nezávislé od pripojenia (možno mať otvorené aj
  bez pripojenia, len prázdne/needs-connect stav).
- Chat je **spoločný naprieč facetmi/mapami** — nezávisí od filtra zobrazenia na mape.

## 5. World Map integrácia

### 5.1 Markery

- Vo `WorldMapGump` sa UOAM hráči kreslia popri existujúcich party/guild markeroch
  (rovnaký vzor ako `DrawMobile` volania pri `_showPartyMembers`), vlastná odlišná
  farba/ikona.
- Toggle "Show UOAM players" v map context menu (vzor `_options["show_all_markers"]` /
  `_showPartyMembers`), uložený v profile.
- Hráč na inom `Map` (facet) než aktuálny hráč → kreslí sa na `(0, 0)`, meno šedou farbou
  (namiesto normálnej farby markera), aby bolo vizuálne jasné že pozícia nie je presná.

### 5.2 Dedup s party/guild markermi

Cieľ: rovnaký hráč viditeľný cez UO server (party/guild) aj cez UOAM sa nezobrazí
duplicitne.

Pre každého UOAM hráča, porovnanie voči `WorldMapEntityManager.Entities` (existujúci
party/guild dict, `WMapEntity` s `Name`, `X`, `Y`, `Map`):

1. Ak existuje alias záznam pre UOAM meno hráča → použije sa aliasované (herné) meno na
   porovnanie namiesto UOAM mena.
2. Porovnanie mena (alias alebo priame UOAM meno) s `entity.Name`, case-insensitive.
3. Ak meno sedí, over pozíciu: `|uoam.X - entity.X| ≤ tolerance && |uoam.Y - entity.Y| ≤
   tolerance` (tolerance konštanta rádovo niekoľko tiles, kompenzuje rozdielne update
   cykly/lag medzi UOAM pollingom a herným party update).
4. Iba ak sedí **aj meno aj pozícia** → považuje sa za duplicit, UOAM marker sa
   nekreslí (party/guild marker má prioritu ako presnejší zdroj).

### 5.3 Track Player

- Context menu → "UOAM Players" submenu → zoznam aktuálne pripojených/videných UOAM
  hráčov, každý s "Track Player" toggle (radio-button správanie: len jeden aktívny
  naraz; klik na už aktívny ho vypne).
- Kým je tracking aktívny pre hráča: `WorldMapGump` viewport sa centruje na jeho
  pozíciu namiesto pozície vlastného hráča (žiadny voľný pan, kým beží tracking).
  Vypnutie vráti default správanie (centrovanie na vlastného hráča / voľný pan).
- Ak trackovaný hráč je na inom facete (pozícia `(0,0)`, sivé meno) — mapa sa centruje
  na `(0,0)`, žiadny crash/výnimka; tracking ostáva zapnutý a znovu naskočí na reálnu
  pozíciu keď hráč zmení facet späť.

## 6. Future work (mimo scope tejto iterácie)

- **Panic**: UOAM klient pri space odošle position update s panic bitom niekde v
  `noip.reserved[12]`. Presný bit/maska neznáma zo zdrojáku uoamhub (server to necháva
  ako opaque blob). Treba packet capture s reálnym UOAM klientom na potvrdenie. Keď
  bude overené: nový `MacroType.UOAMPanic` (nastaví lokálny panic flag, ktorý sa pošle v
  nasledujúcom position update) + červené blikajúce orámovanie `WorldMapGump` keď
  ktorýkoľvek sledovaný hráč má panic flag zapnutý.
- Prípadné rozšírenie uoamhub servera o natívny panic packet (namiesto reverse-engineeringu
  bitu) — zvažované, zatiaľ zamietnuté v prospech kompatibility s existujúcim
  nezmeneným hubom.

## Testovanie

- Unit testy pre `UOAMClient` packet framing/parsing (handshake response, position
  update encode/decode, list response parsing, chat poll parsing) — bez skutočného
  socketu, na raw byte buffroch (vzor existujúcich testov v `tests/ClassicUO.UnitTests`).
- Dedup logika (alias/name/position match) testovateľná čisto ako unit test nad
  `WMapEntity`-like dátami, bez UI závislosti.
- Manuálne otestovanie proti reálne bežiacemu `uoamhub` (treba skompilovať a spustiť
  `E:\Projects\uoamhub` lokálne, viď jeho README) pred označením position formátu ako
  potvrdeného.
