// The generated C# client (Generated/NetWivCommands.g.cs) calls C# commands through this
// module, which forwards them to @netwiv/api as JSON.
import { invoke } from './vendor/@netwiv/api/core.js';

export const invokeJson = async (command, args) => JSON.stringify((await invoke(command, JSON.parse(args))) ?? null);
