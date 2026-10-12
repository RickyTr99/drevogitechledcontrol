# Drevogitech Led Control

Un'unica app, in stile Windows 11, per gestire senza software dei produttori:

- **Tastiera Drevo Tyrfing V2** (USB `0416:A0F8`): effetti, colori per singolo tasto, luminosità su 6 livelli, profili salvati.
- **Mouse Logitech G502 HERO** (USB `046D:C08B`): luci di logo e indicatore DPI (anche sempre acceso, funzione HID++ `0x1300`), livelli DPI, DPI Shift, frequenza di polling, assegnazione dei pulsanti.
- **Sincronizzazione**: il mouse può seguire colore, effetto e luminosità della tastiera.

## Uso

Avvia `bin\app\DrevogitechLedControl.exe` (serve il .NET 10 Desktop Runtime, già presente su questo PC).

- Le luci si aggiornano subito; quelle del mouse vengono anche salvate nella sua memoria interna pochi secondi dopo l'ultima modifica.
- DPI, polling e pulsanti si salvano con **Salva nel mouse**: restano nel mouse e funzionano anche senza l'app.
- Prima della prima scrittura viene salvata una copia dei profili originali del mouse in
  `%APPDATA%\DrevogitechLedControl\backup-g502-originale`; si ripristinano da **Impostazioni → Ripristina**.
- **Applica all'avvio di Windows** riapplica le luci all'accesso (`DrevogitechLedControl.exe --apply`, poi si chiude).

## Compilazione

`build.cmd` (usa l'SDK .NET 10 in `%USERPROFILE%\.dotnet`).

## Protocolli

**Tastiera**: report da 32 byte (ID `0x06`) sulla collection vendor dell'interfaccia 1.
Effetti `06 BE 15 00 01 01 <modo> <velocità 0-9> <luminosità 0-6> <direzione> 00 00 <RGB1> <arcobaleno> <RGB2>`;
colori per tasto `06 BE 19 00 01 01 0E <5 codici HID> <RGB+00 ×5>`.
La luminosità fine si ottiene scalando i colori (curva quadratica) con la tastiera al livello massimo.
Fonti: [DrevoTyrfing](https://github.com/dennisblokland/DrevoTyrfing), [dtv2](https://pypi.org/project/dtv2/).

**Mouse**: Logitech HID++ 2.0 (report lungo `0x11`). Luci dal vivo con la funzione `0x8070`;
profili integrati con `0x8100` (settori da 256 byte, CRC-CCITT), formato secondo
[libratbag](https://github.com/libratbag/libratbag) (`src/hidpp20.c`).
