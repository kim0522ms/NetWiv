window.__TAURI_ISOLATION_HOOK__ = async message => {
  if (message.cmd === 'blocked') return new Promise(() => {});
  if (message.cmd === 'echo') {
    await new Promise(resolve => setTimeout(resolve, 10));
    message.payload.value = { original: message.payload.value, filtered: true };
  }
  return message;
};
