import { invoke } from './vendor/@netwiv/api/core.js';

window.netwivGreet = name => invoke('greet', { name });
