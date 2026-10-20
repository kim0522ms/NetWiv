import { useEffect, useRef, useState, type FormEvent } from 'react';
import { commands } from './generated/commands.js';

type CallState = 'idle' | 'calling' | 'answered' | 'failed';
const smoke = new URLSearchParams(location.search).has('smoke');
const appName = 'NetWiv app';

export default function App() {
  const [name, setName] = useState('React');
  const [state, setState] = useState<CallState>('idle');
  const [message, setMessage] = useState('No call yet');
  const [count, setCount] = useState<number | null>(null);
  const [ms, setMs] = useState(0);
  const [error, setError] = useState('');
  const form = useRef<HTMLFormElement>(null);

  useEffect(() => {
    // Smoke tests open the window with ?smoke=1 and expect one automatic call.
    if (smoke) form.current?.requestSubmit();
  }, []);

  async function greet(event: FormEvent) {
    event.preventDefault();
    setState('calling');
    const started = performance.now();
    try {
      const result = await commands.greet({ name });
      setMs(Math.round(performance.now() - started));
      setMessage(result.message);
      setCount(result.count);
      setState('answered');
      if (smoke) {
        if (result.message !== 'Hello, React!' || result.count !== 1)
          throw new Error(`Unexpected C# response: ${JSON.stringify(result)}`);
        await commands.smoke_done({ result: 'PASS: React form → generated C# → JSON' });
      }
    } catch (cause) {
      setError(String(cause));
      setState('failed');
      if (smoke) await commands.smoke_done({ result: `FAIL: ${cause}` });
    }
  }

  const statusText = state === 'calling' ? 'Calling C#'
    : state === 'answered' ? `Answered in ${ms} ms`
    : state === 'failed' ? 'No answer'
    : 'No calls yet';

  return (
    <div className="starter">
      <header className="bar">
        <span className="brand"><svg className="mark" viewBox="76 88 348 348" aria-hidden="true"><rect className="back" x="160" y="88" width="264" height="264" rx="48"/><rect className="gap" x="76" y="148" width="288" height="288" rx="60"/><rect className="front" x="88" y="160" width="264" height="264" rx="48"/><path className="letter" d="M152 360V224h40l56 100V224h40v136h-40l-56-100v100z"/></svg><span>{appName}</span></span>
        <span className="status" data-state={state} aria-live="polite">{statusText}</span>
      </header>
      <main className="split">
        <section className="call" aria-labelledby="heading">
          <h1 id="heading">Your frontend is ready. Call the C# backend.</h1>
          <form className="ask" ref={form} onSubmit={greet}>
            <label htmlFor="name">Name</label>
            <div className="row">
              <input id="name" value={name} onChange={event => setName(event.target.value)} autoComplete="off" required />
              <button disabled={state === 'calling'}>{state === 'calling' ? 'Calling…' : 'Call greet'}</button>
            </div>
          </form>
          <div className="answer" data-state={state === 'calling' ? (count === null ? 'idle' : 'answered') : state} aria-live="polite">
            <p className="route">commands.greet → Commands.Greet</p>
            {state === 'failed' ? (
              <>
                <p className="message">C# did not answer</p>
                <p className="detail">In a browser tab, start the app with netwiv dev instead. In the window, the error below comes from Greet in src-netwiv/Program.cs.<code>{error}</code></p>
              </>
            ) : (
              <>
                <p className={count === null ? 'message' : 'message landed'} key={count ?? 0}>{state === 'calling' && count === null ? 'Waiting for C#…' : message}</p>
                <dl className="count"><dt>Calls counted in C#</dt><dd className={count === null ? undefined : 'landed'} key={count ?? 0}>{count ?? '—'}</dd></dl>
              </>
            )}
          </div>
        </section>
        <aside className="map" aria-labelledby="map-heading">
          <h2 id="map-heading">Make it yours</h2>
          <ul className="files">
            <li><code>src/App.tsx</code><span>This screen. Replace it with your interface.</span></li>
            <li><code>src-netwiv/Program.cs</code><span>Commands, state and the window. Greet is defined here.</span></li>
            <li><code>src/generated/commands.js</code><span>Typed calls to your C# commands, generated on each build.</span></li>
            <li><strong>netwiv.github.io</strong><span>Guides and the API reference.</span></li>
          </ul>
        </aside>
      </main>
    </div>
  );
}
