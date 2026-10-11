// Keep suite order so a focused run has the same ordering as the full gate.
export function selectCases(suiteCases, options) {
  if (!options.length) return [...suiteCases]
  if (options.length !== 2 || options[0] !== '--cases')
    throw Error('Only --cases name[,name...] is accepted before --')
  const selected = options[1].split(',')
  if (selected.some(name => !name) || new Set(selected).size !== selected.length)
    throw Error('Case selection must contain nonempty unique names')
  const unknown = selected.filter(name => !suiteCases.includes(name))
  if (unknown.length) throw Error(`Unknown conformance cases: ${unknown.join(', ')}`)
  return suiteCases.filter(name => selected.includes(name))
}
