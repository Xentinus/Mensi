/** Testhő (BBT) egész/tized/század számjegyeinek egész-aritmetikás össze- és szétszerelése.
 *  A cél, hogy a PUT-body és az audit-napló ne kapjon lebegőpontos maradékot (pl. a naiv
 *  `whole + tenths / 10 + hundredths / 100` kombinációk kb. 24%-ára `36.120000000000005`-szerű
 *  értéket ad) — az összeadást egész számokon végezzük, és csak a végén osztunk 100-zal. */
export function composeBbt(whole: number, tenths: number, hundredths: number): number {
  return (whole * 100 + tenths * 10 + hundredths) / 100
}

/** composeBbt inverze: egy °C érték szétszedése whole/tenths/hundredths számjegyekre.
 *  A bemenetet egyszer, elöl kerekítjük egész century-Celsius-ra (centi), és onnantól
 *  csak egész aritmetikát végzünk — így elkerüljük, hogy három külön lebegőpontos
 *  szorzás/moduló egymástól függetlenül csúsztasson egyet a legutolsó számjegyen. */
export function decomposeBbt(value: number): { whole: number; tenths: number; hundredths: number } {
  const centi = Math.round(value * 100)
  return {
    whole: Math.floor(centi / 100),
    tenths: Math.floor(centi / 10) % 10,
    hundredths: centi % 10,
  }
}
