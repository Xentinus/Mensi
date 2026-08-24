import { describe, expect, it } from 'vitest'
import { composeBbt, decomposeBbt } from '~/utils/bbt'

const WHOLE_OPTIONS = [35, 36, 37, 38]
const DIGIT_OPTIONS = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]

describe('composeBbt', () => {
  it('35.12 pontosan jön ki, lebegőpontos maradék nélkül', () => {
    expect(composeBbt(35, 1, 2)).toBe(35.12)
  })

  it('mind a 400 whole/tenths/hundredths kombináció kerek 2 tizedesjegyű szám', () => {
    for (const whole of WHOLE_OPTIONS) {
      for (const tenths of DIGIT_OPTIONS) {
        for (const hundredths of DIGIT_OPTIONS) {
          const v = composeBbt(whole, tenths, hundredths)
          expect(Number(v.toFixed(2))).toBe(v)
        }
      }
    }
  })
})

describe('decomposeBbt', () => {
  it('composeBbt inverze néhány konkrét értékre', () => {
    expect(decomposeBbt(35.12)).toEqual({ whole: 35, tenths: 1, hundredths: 2 })
    expect(decomposeBbt(36.3)).toEqual({ whole: 36, tenths: 3, hundredths: 0 })
    expect(decomposeBbt(38.09)).toEqual({ whole: 38, tenths: 0, hundredths: 9 })
  })

  it('kör-út (compose → decompose) mind a 400 kombinációra az eredeti számjegyeket adja', () => {
    for (const whole of WHOLE_OPTIONS) {
      for (const tenths of DIGIT_OPTIONS) {
        for (const hundredths of DIGIT_OPTIONS) {
          const v = composeBbt(whole, tenths, hundredths)
          expect(decomposeBbt(v)).toEqual({ whole, tenths, hundredths })
        }
      }
    }
  })
})
