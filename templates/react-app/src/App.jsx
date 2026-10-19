import { useEffect, useRef, useState } from 'react';
import { isNetWiv } from '@netwiv/api/core';
import { commands } from './generated/commands.js';

const smoke = new URLSearchParams(location.search).has('smoke');

export default function App() {
  const [name, setName] = useState('React');
  const [greeting, setGreeting] = useState(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const form = useRef(null);
  const desktop = isNetWiv();

  async function greet(event) {
    event.preventDefault();
    setPending(true);
    setError('');
    try {
      const result = await commands.greet({ name });
      setGreeting(result);
      if (smoke) {
        if (result.message !== 'Hello, React!' || result.count !== 1)
          throw new Error(`Unexpected C# response: ${JSON.stringify(result)}`);
        await commands.smoke_done({ result: 'PASS: React form → generated C# → JSON' });
      }
    } catch (cause) {
      setError(String(cause));
      if (smoke) await commands.smoke_done({ result: `FAIL: ${cause}` });
    } finally { setPending(false); }
  }

  // Exercises the rendered React form through the actual WebView in smoke mode.
  useEffect(() => {
    if (smoke && desktop) form.current.requestSubmit();
  }, [desktop]);

  return (
    <main>
      <div className="eyebrow">NETWIV / REACT EXAMPLE</div>
      <h1>React 화면, C# 백엔드.</h1>
      <p className="intro">이름을 보내면 C# 명령이 인사와 호출 횟수를 반환합니다.</p>
      <section>
        <form ref={form} onSubmit={greet}>
          <label htmlFor="name">이름</label>
          <div className="fields">
            <input id="name" value={name} onChange={event => setName(event.target.value)} required />
            <button disabled={pending || !desktop}>{pending ? '호출 중…' : 'C#에 인사하기'}</button>
          </div>
        </form>
        {!desktop && <p role="status">브라우저에서는 화면만 볼 수 있습니다. C# 호출은 NetWiv 앱 창에서 실행하세요.</p>}
        {error && <p role="alert" className="error">{error}</p>}
        <div className="response" aria-live="polite">
          <span className="caption">C# 응답</span>
          <h2>{greeting?.message ?? '첫 인사를 기다리고 있어요.'}</h2>
          <p>백엔드 호출 횟수 <strong>{greeting?.count ?? '—'}</strong></p>
        </div>
      </section>
      <footer><code>src/App.jsx</code>에서 화면을, <code>src-netwiv/Program.cs</code>에서 명령을 수정하세요.</footer>
    </main>
  );
}
