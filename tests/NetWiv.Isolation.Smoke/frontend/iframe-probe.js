parent.postMessage('probe-started', '*');
parent.frames[0].postMessage({ cmd: 'echo', callback: 90909090, error: 90909091, payload: { value: 'forged' } }, '*');
