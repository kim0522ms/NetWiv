import { invoke } from './vendor/@tauri-apps/api/core.js';

window.netwivGreet = name => invoke('greet', { name });
