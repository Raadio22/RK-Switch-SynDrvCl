# RK-Switch SynDrvCl

![Ikona RK-Switch](Assets/rk-switch.png)

Jednoduchá Windows aplikace pro ruční přepnutí **oficiálního nastavení** Synology Drive Client mezi:

- **FIRMA** – `192.168.1.2` (povoleno jen po úspěšném TCP testu portu `6690` a shodě ARP MAC `90-09-D0-90-16-7D`)
- **MIMO FIRMU** – QuickConnect ID `NAS-ZemOlsar`

Vzhled navazuje na RK-Spp a společnou ikonovou rodinu RK-Geo/RK-Spp. Hlavní stavová karta zobrazuje aktuální trasu, cílovou adresu, dostupnost procesu Synology Drive a čas poslední kontroly. Při aktivním okně se stav automaticky obnovuje jednou za minutu.

## Bezpečnostní hranice

- Aplikace neotevírá ani nemění SQLite databáze Synology Drive.
- Nečte, neukládá ani nezapisuje heslo.
- Nemění uživatelský účet, SSL ani synchronizační úlohy.
- Zapisuje pouze pole **Adresa serveru** přes Windows UI Automation a aktivuje oficiální tlačítko **OK**.
- Certifikátová a jiná potvrzení Synology nikdy nepotvrzuje automaticky.
- Výchozí stav je **zkušební režim**: UI se jen přečte a dialog se ukončí přes **Storno**.
- Volba **Spouštět automaticky po přihlášení do Windows** používá pouze uživatelský klíč `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run`; nevyžaduje správce a lze ji stejným přepínačem opět vypnout.

## Sestavení

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true
```

Výsledný samostatný EXE je v `bin\Release\net8.0-windows\win-x64\publish`.

## Ověřovací pořadí

1. Na `RAADIO-BOOK4PRO` spustit výchozí zkušební režim.
2. Stisknout obě tlačítka a potvrdit, že nic nebylo změněno; u režimu FIRMA musí proběhnout TCP + MAC kontrola.
3. Ověřit zobrazený aktuální server proti dialogu Synology Drive.
4. Před prvním živým testem dokončit/pozastavit přenosy a vytvořit běžnou uživatelskou zálohu nastavení Synology.
5. Teprve potom zaškrtnout **Povolit živé změny**, přečíst varování a potvrdit jeden směr.
6. Ručně ověřit stav všech synchronizačních úloh a případné varování certifikátu.

Viz také [DEPLOYMENT.md](DEPLOYMENT.md).
