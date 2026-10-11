import { commands } from './src/generated/commands.js';
import type { GreetingOutput, WireContractInput, WireContractOutput } from './src/generated/commands.js';
const result: GreetingOutput = await commands.greet({ name: 'NetWiv' });
const message: string = result.message;
const count: number = result.count;
void [message, count];
// @ts-expect-error CLR property casing is not the configured JSON casing.
result.Message;
// @ts-expect-error Guest parameters retain their declared types.
commands.greet({ name: 123 });
// @ts-expect-error Required command arguments cannot be omitted.
commands.greet();
// @ts-expect-error State is injected by the backend, not sent by the guest.
commands.greet({ name: 'NetWiv', counter: {} });
// @ts-expect-error Undeclared commands have no generated wrapper.
commands.nonexistent();

const wireInput: WireContractInput = { displayName: 'typed', status: 'in-progress', 'exact-name': 9 };
const wire: WireContractOutput = await commands.wire_profile({ value: wireInput });
await commands.wire_profile({ value: { input: 17, inputOnly: 19 } });
const outputOnly: number = wire.outputOnly;
void outputOnly;
// @ts-expect-error WhenWriting removes the property from the result.
wire.inputOnly;
// @ts-expect-error WhenReading removes setter-based inputs.
commands.wire_profile({ value: { outputOnly: 99 } });
const computed: number = wire.computed;
const writeOnlyResult: number = wire.output;
void [computed, writeOnlyResult];
// @ts-expect-error A setter-only property is input-only.
wire.input;
// @ts-expect-error A computed getter is not a command input.
commands.wire_profile({ value: { computed: 1 } });
// @ts-expect-error JSON-ignored properties have no generated input.
commands.wire_profile({ value: { ignored: 1 } });
const status: 'Ready' | 'in-progress' | number = wire.status;
const nullableStatus: 'Ready' | 'in-progress' | number | null = wire.nullableStatus;
const includedField: number = wire['included-field'];
await commands.wire_profile({ value: { 'included-field': 31 } });
// @ts-expect-error Fields without JsonInclude are absent when IncludeFields is false.
commands.wire_profile({ value: { ignoredField: 1 } });
// @ts-expect-error Explicit JSON names apply to fields too.
commands.wire_profile({ value: { includedField: 31 } });
await commands.wire_profile({ value: { nullableStatus: null } });
await commands.wire_profile({ value: { nullableStatus: 'in-progress' } });
// @ts-expect-error Nullable enum converters retain their named wire values.
commands.wire_profile({ value: { nullableStatus: 'unknown' } });
const note: string | null | undefined = wire.note;
const omitted: number | undefined = wire.omittedCount;
void [status, nullableStatus, includedField, note, omitted];
// @ts-expect-error String enum values have declared wire names.
commands.wire_profile({ value: { status: 'unknown' } });
// @ts-expect-error Conditional JSON omission makes this output optional.
const requiredCount: number = wire.omittedCount;

const constructor = await commands.constructor_profile({ value: { name: 'Ada', count: 2 } });
const retry: number = constructor.retry;
const constructorNote: string | null = constructor.note;
void [retry, constructorNote];
// @ts-expect-error Non-nullable constructor-bound properties are required by Core.
commands.constructor_profile({ value: { count: 2 } });
// @ts-expect-error Required numeric constructor inputs cannot be omitted either.
commands.constructor_profile({ value: { name: 'Ada' } });

const numbers = await commands.number_profile({ value: { count: '42', strict: 2, values: ['1', null], nested: { count: 3, ratio: 'NaN' } } });
const numericOutput: string = numbers.count;
const numericValues: Array<string | null> = numbers.values;
const strictOutput: number = numbers.strict;
const nestedCount: number = numbers.nested.count;
const namedFloat: number | 'NaN' | 'Infinity' | '-Infinity' = numbers.nested.ratio;
void [numericOutput, numericValues, strictOutput, nestedCount, namedFloat];
// @ts-expect-error The strict property overrides its containing type's quoted-number input policy.
commands.number_profile({ value: { count: 42, strict: '2', values: [], nested: { count: 3, ratio: 1 } } });
// @ts-expect-error The output is serialized as a string even when the input was numeric.
const incorrectNumericOutput: number = numbers.count;
// @ts-expect-error Named floating-point tokens are limited to the serializer's three literals.
commands.number_profile({ value: { count: 42, strict: 2, values: [], nested: { count: 3, ratio: 'bad' } } });

const extensible = await commands.extension_profile({ value: { name: 'Ada', extra: 42, nested: { value: null }, list: [1, null] } });
const extensionName: string = extensible.name;
const unknownValue: unknown = extensible.extra;
void [extensionName, unknownValue];
// @ts-expect-error Extension data does not relax the type of a known property.
commands.extension_profile({ value: { name: 42, extra: null } });
// @ts-expect-error Known required properties remain required.
commands.extension_profile({ value: { extra: 42 } });
// @ts-expect-error Additional values require narrowing before use.
const extensionNumber: number = extensible.extra;
