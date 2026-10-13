const require = (condition, message) => { if (!condition) throw new Error(message); };
async function rejects(action, expected) {
  try { await action(); }
  catch (error) { require(String(error).includes(expected), `Unexpected rejection: ${error}`); return; }
  throw new Error(`Expected rejection containing: ${expected}`);
}

// This same function runs in the embedded WebView through generated official invoke wrappers.
export async function runSmoke(commands) {
  const greeting = await commands.greet({ name: 'World' });
  require(greeting.message === 'Hello from the Generic Host, World', 'Host options did not reach the constructor');
  require(greeting.sharedDependency, 'Constructor and method parameter did not share a scope');
  let audit = await commands.audit_snapshot();
  require(audit.disposedWorkspaces.includes(greeting.workspaceId), 'Application scope was not disposed before IPC completion');

  const status = () => commands['plugin:equipment|get_status']();
  const first = await status();
  const second = await status();
  const concurrent = await Promise.all([status(), status()]);
  const statuses = [first, second, ...concurrent];
  require(new Set(statuses.map(value => value.receiverId)).size === 4, 'Plugin calls reused a scoped receiver');
  require(new Set([greeting.workspaceId, ...statuses.map(value => value.workspaceId)]).size === 5, 'Invocations reused a dependency scope');
  require(statuses.every(value => value.state === 'ready' && value.sharedDependency && !value.lifecycleInitialized),
    'DI receivers did not use constructor dependencies independently of the lifecycle object');
  audit = await commands.audit_snapshot();
  require(audit.workspacesCreated === 5 && audit.receiversCreated === 4 && audit.lifecycleInitialized === 1, 'Unexpected activation counts');
  require(audit.maxConcurrentStatuses >= 2, 'Concurrent frontend calls did not overlap while awaited');
  require(statuses.every(value => audit.disposedWorkspaces.includes(value.workspaceId) && audit.disposedReceivers.includes(value.receiverId)),
    'Plugin async receiver/dependency disposal did not finish before IPC completion');
  await rejects(() => commands['plugin:equipment|reset'](), 'not allowed');
  await rejects(() => commands['plugin:equipment|unselected'](), 'not implemented');
  const afterDenial = await commands.audit_snapshot();
  require(afterDenial.resetCalls === 0 && afterDenial.workspacesCreated === audit.workspacesCreated && afterDenial.receiversCreated === audit.receiversCreated,
    'Denied or unselected command activated a receiver or ran its body');
}
