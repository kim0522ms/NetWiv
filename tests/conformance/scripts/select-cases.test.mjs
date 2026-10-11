import assert from 'node:assert/strict'
import test from 'node:test'
import { selectCases } from './select-cases.mjs'

test('defaults to all cases and preserves suite order for selected cases', () => {
  assert.deepEqual(selectCases(['a', 'b', 'c'], []), ['a', 'b', 'c'])
  assert.deepEqual(selectCases(['a', 'b', 'c'], ['--cases', 'c,a']), ['a', 'c'])
})

test('rejects typos, empty selections, duplicate cases and unrecognized options', () => {
  for (const options of [['--cases', 'unknown'], ['--cases', ''], ['--cases', 'a,'],
    ['--cases', 'a,a'], ['--case', 'a'], ['--cases'], ['--cases', 'a', 'extra']])
    assert.throws(() => selectCases(['a', 'b'], options))
})
