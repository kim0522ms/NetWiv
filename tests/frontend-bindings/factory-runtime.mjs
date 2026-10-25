import assert from 'node:assert/strict';
import { createCommands } from './commands.factory.js';

assert.throws(() => import.meta.resolve('@netwiv/api/core'), { code: 'ERR_MODULE_NOT_FOUND' });

const result = Object.freeze({ display_name: 'routed', count: '2' });
const rejection = new Error('invoke rejected');
const calls = [];
const invoke = async (command, args, options) => {
  calls.push({ command, args, options });
  if (command === 'plugin:library-check|blocked') throw rejection;
  return result;
};
const commands = createCommands(invoke);
assert.ok(Object.isFrozen(commands));
const args = { value: { display_name: 'Ada', count: 2 } };
const options = { headers: { 'x-factory-test': 'app' } };

assert.equal(await commands.package_echo(args, options), result);
assert.equal(calls[0].command, 'package_echo');
assert.equal(calls[0].args, args);
assert.equal(calls[0].options, options);

const pluginOptions = { headers: new Headers({ 'x-factory-test': 'plugin' }) };
assert.equal(await commands['plugin:library-check|ping'](undefined, pluginOptions), result);
assert.equal(calls[1].command, 'plugin:library-check|ping');
assert.deepEqual(calls[1].args, {});
assert.equal(calls[1].options, pluginOptions);

assert.equal(await commands['plugin:library-check|ping'](), result);
assert.equal(calls[2].command, 'plugin:library-check|ping');
assert.deepEqual(calls[2].args, {});
assert.equal(calls[2].options, undefined);

await assert.rejects(commands['plugin:library-check|blocked'](undefined, pluginOptions), error => error === rejection);
assert.equal(calls[3].command, 'plugin:library-check|blocked');
assert.deepEqual(calls[3].args, {});
assert.equal(calls[3].options, pluginOptions);
