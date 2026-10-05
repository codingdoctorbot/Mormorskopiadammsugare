# Bruksanvisning

*[In English](MANUAL.md)*

Mormorskopiadammsugare letar igenom dina gamla säkerhetskopior, hittar alla foton och videor och
kopierar varje bild **en gång** till en ny mapp (*destinationen*). Sedan sorterar det kopiorna i mappar
efter världsdel och år, till exempel `Europe\2015`.

**Dina originalfiler ändras, flyttas eller raderas aldrig.** Allt kopieras – om något går fel är dina
säkerhetskopior exakt som förut.

Det sker i två steg, en flik per steg: **1 Extract** (hitta och kopiera alla unika foton/videor) och
**2 Organize** (sortera kopiorna i mappar).

## Flik 1 – Extract (hämta ut)

| Inställning | Vad den gör | Varför |
|---|---|---|
| **Folders to search** (mappar att leta i) | Mapparna med dina gamla säkerhetskopior. *Add folders…* (flera på en gång) eller dra in dem från Utforskaren. *Remove* tar bara bort mappen från listan. | Du bestämmer exakt var den letar. |
| **Destination** | Dit kopiorna hamnar. Välj en **tom** mapp på en disk med gott om utrymme. Där skapas `Extracted\` (kopiorna hamnar här först) och `_Mormorskopiadammsugare\` (katalog och loggar). | Allt nytt hamnar på ett ställe, skilt från dina säkerhetskopior. |
| **Photos / Videos** (foton / videor) | Vad som ska letas efter. Båda på från början. | Vill du bara ha foton just nu? Kryssa ur Videos. |
| **Skip app caches** (hoppa över programcacher) | Hoppar över AppData, webbläsarcacher och miniatyrmappar. På från början. | De är fulla av små bilder som program och webbsidor sparat automatiskt – inte dina foton. |
| **Skip files under 10 KB** (hoppa över filer under 10 KB) | Ignorerar pyttesmå filer. På från början. | Ikoner, webbgrafik och miniatyrer. Sänk siffran om du har mycket gamla, mycket små bilder att spara. |
| **Verify copies** (kontrollera kopiorna) | Läser tillbaka varje kopia och kontrollerar att den är exakt likadan som originalet. Av från början. | Extra säkerhet; går långsammare. |
| **Dry run** (provkörning) | Räknar vad som *skulle* kopieras, kopierar inget. | Ett säkert första test innan något diskutrymme används. |
| **Copy suspect files** (kopiera misstänkta filer) | Filer som heter som bilder (`.jpg`) men innehåller något annat – ofta en sparad webbsida – hamnar i `Extracted\_suspect` i stället för att hoppas över. | För att vara säker på att inget riktigt missas. |
| **Organize automatically afterwards** (sortera automatiskt efteråt) | Startar sorteringen (flik 2) när uthämtningen är klar. | Ett klick gör allt. Låt det vara av om du vill se förhandsvisningen först. |
| **Parallel reads** (antal filer samtidigt) | Hur många filer som läses på samma gång. Normalt 4. | 4 är snabbt på SSD. Välj **1** för gamla hårddiskar (HDD) och USB-diskar – flera samtidigt gör dem långsammare. |

**Knappar:** *Start* sätter igång. *Cancel* avbryter säkert – tryck Start igen senare så fortsätter den
där den slutade; inget kopieras två gånger. *Open destination* öppnar mappen. *Show log* visar allt som
hänt, inklusive fel.

**Resultatrutorna:** *Photos/Videos found* (hittade i säkerhetskopiorna) · *New copies* (nya kopior den
här gången) · *Duplicates* (dubbletter – innehållet redan kopierat, hoppas över) · *Done earlier* (klart
i en tidigare körning, läses inte ens om) · *Copied* (kopierad data) · *Errors* (fel – blir orange, se
loggen) · *Time* (tid). Raden under listar filer som var för små eller inte riktiga bilder.

## Flik 2 – Organize (sortera)

**Året** – första träffen gäller:
1. Datumet som kameran eller telefonen sparade inne i fotot/videon.
2. Ett datum i filnamnet, t.ex. `IMG_20150612_143005`.
3. Ett år i närmaste mappnamn, t.ex. `Rom 2009`. Mappar som heter som säkerhetskopior (`Backup 2019`,
   `Säkerhetskopia`) ignoreras – det året är när kopian gjordes, inte när bilden togs.
4. Bara om du kryssar i alternativet nedan: filens *ändrad*-datum.

**Platsen** kommer från GPS-positionen som sparats i fotot/videon, uppslagen utan internet. Bilder utan
GPS hamnar i `_Unknown location` (okänd plats) – ofta den största mappen, eftersom äldre kameror,
inskannade foton och bilder skickade via chattappar saknar GPS. Det är normalt.

| Inställning | Vad den gör | Varför |
|---|---|---|
| **Add country folders** (landsmappar) | `Europe\Sweden\2015` i stället för `Europe\2015`. | Lättare att hitta en resa. Byt när som helst – nästa sortering flyttar bara de sorterade filerna. |
| **…and Swedish counties** (svenska län) | (Kräver landsmappar.) Bilder från Sverige får en länsnivå: `Europe\Sweden\Skåne län\2015`. Andra länder påverkas inte. | För svenska bilder är bara landet en för grov indelning. |
| **Best guesses inside the unknown folders** (bästa gissning i de okända mapparna) | Filer utan GPS eller år ligger kvar i `_Unknown location` / `_Unknown year`, men sorteras vidare: **`~Sweden`** när GPS-bilder tagna inom 3 timmar alla är överens om platsen (t.ex. en kamera utan GPS bredvid en telefon med GPS), **originalets albummapp** (`Midsommar 2018`) när minst 3 filer delar den, och **skärmbilder, grafik och nedladdningar** i egna mappar. På från början. | De okända mapparna blir ofta störst – det här gör dem lätta att bläddra i. `~` betyder "bästa gissning"; de säkra mapparna förblir säkra. |
| **Use the file's modified date** (använd ändringsdatum) | Använder det datumet när inget bättre finns. Av från början. | När filer kopieras till säkerhetskopior blir datumet ofta kopieringsdagen, och gamla bilder skulle hamna på fel år. Utan riktigt datum hamnar bilden i stället i `_Unknown year` (okänt år). |

**Knappar:** *Preview* visar trädet över hur filerna **kommer** att sorteras – inget flyttas än.
*Organize* flyttar dem. *Cancel* avbryter; tryck Preview och Organize igen för att fortsätta.

## Mapparna du får

```
Europe\2015\                      känd plats och känt år
Europe\_Unknown year\             känd plats, okänt år
_Unknown location\2009\           okänd plats, känt år
_Unknown location\_Unknown year\  inget känt
_Unknown location\2018\~Sweden\   bästa gissning: samma tid som GPS-bilder från Sverige
_Unknown location\2018\Midsommar 2018\   bästa gissning: originalets albummapp
_Unknown location\_Screenshots\   skärmbilder (även _Graphics, _Downloads)
Extracted\                        väntar på sortering (tom efter Organize)
_Mormorskopiadammsugare\          katalog + loggar – behåll den så länge du använder programmet på mappen
```

Samma namn, olika bilder → båda sparas (`IMG_0001.jpg`, `IMG_0001 (2).jpg`).
Samma bild, flera namn → en kopia, med det bästa namnet.

## Rekommenderat arbetssätt

1. Börja smått: en säkerhetskopia på några GB, till en ny, tom destination.
2. Valfritt: kryssa i **Dry run** först för att se siffrorna.
3. Kör på riktigt, och tryck sedan **Preview** på fliken Organize. Kolla **Show log** efter fel.
4. Nöjd? Lägg till resten av säkerhetskopiorna och tryck Start igen med **samma destination** – redan
   kopierade bilder känns igen och kopieras inte igen.
5. Tills du är helt klar: låt programmet sköta destinationen (flytta inte runt filer i den för hand).
6. När allt är sorterat: säkerhetskopiera destinationen ordentligt – den innehåller nu en kopia av varje
   foto och video.

## Begränsningar

- Bara **exakta** dubbletter slås ihop; en förminskad eller komprimerad kopia (t.ex. skickad via
  WhatsApp) sparas för sig.
- ZIP-filer öppnas inte.
- MTS-, MKV-, WMV- och MPG-videor har inget läsbart datum inuti; de sorteras efter fil- och mappnamn.
