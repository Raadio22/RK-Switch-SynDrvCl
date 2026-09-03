# RK-Switch SynDrvCl

![Ikona RK-Switch](Assets/rk-switch.png)

Jednoduchá Windows aplikace pro ruční přepnutí **oficiálního nastavení** Synology Drive Client mezi:

- **FIRMA** – `192.168.1.2` (povoleno jen po úspěšném TCP testu portu `6690` a shodě ARP MAC `90-09-D0-90-16-7D`)
- **MIMO FIRMU** – QuickConnect ID `NAS-ZemOlsar`

Vzhled navazuje na RK-Spp a společnou ikonovou rodinu RK-Geo/RK-Spp. Při spuštění se zobrazí krátká značková obrazovka s průběhem načítání. Hlavní stavová karta zobrazuje aktuální trasu, cílovou adresu, dostupnost procesu Synology Drive a čas poslední kontroly. Při aktivním okně se stav automaticky obnovuje jednou za minutu. Rozložení hlavního okna i dialogů je upravené tak, aby byly ovládací prvky celé viditelné; přihlašovací dialog má pro malé pracovní plochy posuvnou pouze střední část a pevně viditelná tlačítka.

Volitelná funkce **Automaticky přepínat podle sítě** sama volí FIRMA nebo MIMO FIRMU. Kontroluje síť po spuštění aplikace, po změně síťového připojení a dále jednou za minutu. Za firemní síť považuje pouze stav, kdy odpovídá `192.168.1.2:6690` a zároveň souhlasí MAC adresa NASu. Označení sítě ve Windows jako soukromá nebo veřejná samo o sobě rozhodnutí neovlivňuje.

## Bezpečnostní hranice

- Aplikace neotevírá ani nemění SQLite databáze Synology Drive.
- Heslo lze jednou uložit jako obecný přihlašovací údaj ve **Správci přihlašovacích údajů Windows**. Je dostupné jen aktuálnímu účtu Windows a aplikace je nikdy nezapisuje do konfigurace, registru spuštění ani logu.
- Nemění uživatelský účet, SSL ani synchronizační úlohy.
- Zapisuje pouze pole **Adresa serveru** přes Windows UI Automation a aktivuje oficiální tlačítko **OK**.
- Při přechodu na **FIRMA** program vybere v nabídce QuickConnect volbu **Nyní ne**. Nedůvěryhodný SSL certifikát potvrdí volbou **Přesto pokračovat** pouze pro cílovou adresu `192.168.1.2` a až po opakovaném úspěšném ověření portu a přesné MAC adresy NASu. Jiné dialogy automaticky nepotvrzuje.
- Výchozí stav je **živý režim**: pokud je heslo uložené, kliknutí na trasu změní pouze adresu serveru, vyplní heslo do oficiálního dialogu Synology a aktivuje jeho tlačítko **OK**. Při spolehlivě rozpoznaném aktivním přenosu se stále zobrazí varování.
- Volitelný režim **Pouze otestovat bez změny připojení** provede všechny bezpečnostní kontroly, ale dialog ukončí přes **Storno**.
- Volba **Spouštět automaticky po přihlášení do Windows** používá pouze uživatelský klíč `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run`; nevyžaduje správce a lze ji stejným přepínačem opět vypnout.
- Volba **Automaticky přepínat podle sítě** vyžaduje předem uložené heslo. Při rozpoznaném aktivním přenosu změnu odloží, při odchodu z firmy síť pro jistotu ověří podruhé a po změně dodržuje krátkou ochrannou prodlevu. Nastavení volby se ukládá pouze pro aktuální účet Windows do `HKCU\\Software\\RK-Switch-SynDrvCl`.
- Automatika funguje, jen když běží RK-Switch. Pro automatické přepnutí hned po přihlášení je proto vhodné zapnout i **Spouštět po přihlášení do Windows**.
- Diagnostický log bez hesel je v `%LOCALAPPDATA%\\RK-Switch-SynDrvCl\\logs\\rk-switch.log` a lze jej otevřít přímo z aplikace.

Při prvním živém přepnutí aplikace vyžádá heslo účtu DSM a nabídne jeho uložení do zabezpečeného úložiště Windows. Další přepnutí už proběhnou jedním kliknutím. Tlačítko **Správa hesla** umožňuje údaj kdykoli nahradit nebo odstranit. Uživatelské jméno se nepřepisuje; zůstává uložené v Synology Drive.

## Sestavení

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true
```

Výsledný samostatný EXE je v `bin\Release\net8.0-windows\win-x64\publish`.

## Ověřovací pořadí

1. Na `RAADIO-BOOK4PRO` zapnout **Pouze otestovat bez změny připojení**.
2. Stisknout obě tlačítka a potvrdit, že nic nebylo změněno; u režimu FIRMA musí proběhnout TCP + MAC kontrola.
3. Ověřit zobrazený aktuální server proti dialogu Synology Drive.
4. Před prvním živým testem dokončit/pozastavit přenosy a vytvořit běžnou uživatelskou zálohu nastavení Synology.
5. Přes **Správa hesla** uložit heslo DSM, vypnout testovací režim a kliknout na požadovanou trasu.
6. Ručně ověřit stav všech synchronizačních úloh a případné varování certifikátu.

Viz také [DEPLOYMENT.md](DEPLOYMENT.md).
