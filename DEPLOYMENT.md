# Nasazení

## Pilot: RAADIO-BOOK4PRO

Pilot musí proběhnout nejprve se zapnutou volbou **Pouze otestovat bez změny připojení**. Živý test není součást automatického buildu ani testů. Po pilotu se heslo uloží pro aktuální účet do Správce přihlašovacích údajů Windows; další ruční kliknutí na trasu provede celý oficiální postup automaticky.

## Další firemní počítače

1. Po úspěšném pilotu distribuovat podepsaný/hashovaný obsah balíčku z `outputs`.
2. Spouštět v kontextu přihlášeného uživatele, bez administrátorských práv.
3. Synology Drive Client musí být instalovaný v uživatelském profilu a musí mít existující připojení.
4. Na každém PC nejprve zapnout volbu **Pouze otestovat bez změny připojení** a provést zkušební průchod.
5. Režim FIRMA se odblokuje pouze na síti, kde `192.168.1.2:6690` odpovídá a zařízení má MAC `90-09-D0-90-16-7D`.
6. Pokud má aplikace startovat s Windows, nejprve ji rozbalit do trvalého umístění a až potom zapnout volbu **Spouštět automaticky po přihlášení do Windows**. Registrace je pouze pro aktuálního uživatele a nevyžaduje administrátorská práva.
7. Heslo je nutné uložit samostatně pod každým účtem Windows, který aplikaci používá. Lze je změnit nebo odstranit tlačítkem **Správa hesla**.

## Známá omezení

- UI Automation závisí na názvech prvků české nebo anglické verze Synology Drive. Jiný jazyk nebo budoucí změna UI skončí bezpečnou chybou před zápisem.
- Stav probíhajícího přenosu není v buildu 17892 vždy spolehlivě přístupný přes accessibility. V takovém případě aplikace zobrazí, že jej má uživatel zkontrolovat ručně.
- Pokud Synology po změně otevře certifikátové varování, aplikace jej ponechá otevřené bez zásahu.
- Automatická kontrola aktuálního stavu může krátce otevřít oficiální okno Synology Drive; dialog připojení aplikace pouze přečte a ukončí přes Storno.
