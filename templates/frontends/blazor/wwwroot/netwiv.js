// Connects Blazor to the C# backend through @netwiv/api. Pages/Home.razor imports this module.
import { invoke } from './vendor/@netwiv/api/core.js';
import { getName } from './vendor/@netwiv/api/app.js';

export const name = () => getName();
export const greet = name => invoke('greet', { name });
export const smokeDone = result => invoke('smoke_done', { result });
