import assert from 'node:assert/strict';
import test from 'node:test';
import { runSmoke } from './smoke.js';

// Tests the smoke assertions themselves. Real DI/native IPC evidence comes from verify.mjs --run.
function fixture(change = () => ({})) {
  let active = 0;
  const audit = { workspacesCreated: 0, receiversCreated: 0, resetCalls: 0, lifecycleInitialized: 1,
    maxConcurrentStatuses: 0, disposedWorkspaces: [], disposedReceivers: [] };
  const commands = {
    async greet() {
      audit.workspacesCreated++;
      audit.disposedWorkspaces.push('greeting');
      return { message: 'Hello from the Generic Host, World', workspaceId: 'greeting', sharedDependency: true };
    },
    async audit_snapshot() { return structuredClone(audit); },
    async 'plugin:equipment|get_status'() {
      const id = String(++audit.receiversCreated);
      audit.workspacesCreated++;
      audit.maxConcurrentStatuses = Math.max(audit.maxConcurrentStatuses, ++active);
      await Promise.resolve();
      active--;
      audit.disposedWorkspaces.push(id);
      audit.disposedReceivers.push(id);
      return { state: 'ready', workspaceId: id, receiverId: id, sharedDependency: true, lifecycleInitialized: false };
    },
    async 'plugin:equipment|reset'() { throw new Error('not allowed'); },
    async 'plugin:equipment|unselected'() { throw new Error('native window command is not implemented'); }
  };
  return { ...commands, ...change(commands, audit) };
}

test('smoke accepts the expected scope, disposal and ACL observations', async () => {
  await runSmoke(fixture(() => ({})));
});
test('smoke rejects reused scoped receivers', async () => {
  await assert.rejects(runSmoke(fixture(commands => ({
    async 'plugin:equipment|get_status'() { return { ...await commands['plugin:equipment|get_status'](), receiverId: 'reused' }; }
  }))), /reused a scoped receiver/);
});
test('smoke rejects an IPC response before asynchronous disposal', async () => {
  await assert.rejects(runSmoke(fixture(commands => ({
    async audit_snapshot() { return { ...await commands.audit_snapshot(), disposedReceivers: [] }; }
  }))), /disposal did not finish/);
});
test('smoke rejects serialized calls presented as concurrent', async () => {
  await assert.rejects(runSmoke(fixture(commands => ({
    async audit_snapshot() { return { ...await commands.audit_snapshot(), maxConcurrentStatuses: 1 }; }
  }))), /did not overlap/);
});
test('smoke rejects an incorrectly accepted denied command', async () => {
  await assert.rejects(runSmoke(fixture(() => ({ async 'plugin:equipment|reset'() { return 'reset'; } }))), /Expected rejection/);
});
test('smoke rejects denied requests that activate DI receivers', async () => {
  await assert.rejects(runSmoke(fixture((commands, audit) => ({
    async 'plugin:equipment|reset'() { audit.receiversCreated++; return commands['plugin:equipment|reset'](); }
  }))), /activated a receiver/);
});
