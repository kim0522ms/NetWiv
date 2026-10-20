import { Component, ElementRef, computed, signal, viewChild, type AfterViewInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { getName } from '@netwiv/api/app';
import { commands } from './generated/commands.js';

type CallState = 'idle' | 'calling' | 'answered' | 'failed';
const smoke = new URLSearchParams(location.search).has('smoke');

@Component({
  selector: 'app-root',
  imports: [FormsModule],
  template: `
    <div class="starter">
      <header class="bar">
        <span class="brand"><svg class="mark" viewBox="76 88 348 348" aria-hidden="true"><rect class="back" x="160" y="88" width="264" height="264" rx="48"/><rect class="gap" x="76" y="148" width="288" height="288" rx="60"/><rect class="front" x="88" y="160" width="264" height="264" rx="48"/><path class="letter" d="M152 360V224h40l56 100V224h40v136h-40l-56-100v100z"/></svg><span>{{ appName() }}</span></span>
        <span class="status" [attr.data-state]="statusState()" aria-live="polite">{{ statusText() }}</span>
      </header>
      <main class="split">
        <section class="call" aria-labelledby="heading">
          <h1 id="heading">{{ bridge() ? 'Your window is running. Call the C# backend.' : 'This page is outside the NetWiv window.' }}</h1>
          <form #form class="ask" (ngSubmit)="greet()">
            <label for="name">Name</label>
            <div class="row">
              <input id="name" name="name" [(ngModel)]="name" autocomplete="off" required />
              <button [disabled]="state() === 'calling'">{{ state() === 'calling' ? 'Calling…' : 'Call greet' }}</button>
            </div>
          </form>
          <div class="answer" [attr.data-state]="answerState()" aria-live="polite">
            <p class="route">commands.greet → Commands.Greet</p>
            @if (state() === 'failed') {
              <p class="message">C# did not answer</p>
              <p class="detail">{{ bridge() ? 'The greet command failed in C#. Greet is in src-netwiv/Program.cs.' : 'Start the app with netwiv dev so this page runs inside the NetWiv window.' }}<code>{{ error() }}</code></p>
            } @else {
              @for (n of [count()]; track n) {
                <p class="message" [class.landed]="n !== null">{{ state() === 'calling' && n === null ? 'Waiting for C#…' : message() }}</p>
                <dl class="count"><dt>Calls counted in C#</dt><dd [class.landed]="n !== null">{{ n ?? '—' }}</dd></dl>
              }
            }
          </div>
        </section>
        <aside class="map" aria-labelledby="map-heading">
          <h2 id="map-heading">Make it yours</h2>
          <ul class="files">
            <li><code>src/app.ts</code><span>This screen. Replace it with your interface.</span></li>
            <li><code>src-netwiv/Program.cs</code><span>Commands, state and the window. Greet is defined here.</span></li>
            <li><code>src/generated/commands.js</code><span>Typed calls to your C# commands, generated on each build.</span></li>
            <li><strong>netwiv.github.io</strong><span>Guides and the API reference.</span></li>
          </ul>
        </aside>
      </main>
    </div>`,
})
export class App implements AfterViewInit {
  readonly appName = signal('NetWiv app');
  readonly bridge = signal(true);
  name = 'Angular';
  readonly state = signal<CallState>('idle');
  readonly message = signal('No call yet');
  readonly count = signal<number | null>(null);
  readonly ms = signal(0);
  readonly error = signal('');
  private readonly form = viewChild.required<ElementRef<HTMLFormElement>>('form');

  readonly statusText = computed(() => {
    const state = this.state();
    return state === 'calling' ? 'Calling C#'
      : state === 'answered' ? `Answered in ${this.ms()} ms`
      : state === 'failed' ? 'No answer'
      : this.bridge() ? 'Ready' : 'Not in a NetWiv window';
  });
  readonly statusState = computed(() => (this.state() === 'idle' && !this.bridge() ? 'offline' : this.state()));
  readonly answerState = computed(() => (this.state() === 'calling' ? (this.count() === null ? 'idle' : 'answered') : this.state()));

  ngAfterViewInit() {
    getName().then(value => this.appName.set(value), () => this.bridge.set(false));
    // Smoke tests open the window with ?smoke=1 and expect one automatic call.
    if (smoke) this.form().nativeElement.requestSubmit();
  }

  async greet() {
    this.state.set('calling');
    const started = performance.now();
    try {
      const result = await commands.greet({ name: this.name });
      this.ms.set(Math.round(performance.now() - started));
      this.message.set(result.message);
      this.count.set(result.count);
      this.state.set('answered');
      if (smoke) {
        if (result.message !== 'Hello, Angular!' || result.count !== 1)
          throw new Error(`Unexpected C# response: ${JSON.stringify(result)}`);
        await commands.smoke_done({ result: 'PASS: Angular form → generated C# → JSON' });
      }
    } catch (cause) {
      this.error.set(String(cause));
      this.state.set('failed');
      if (smoke) await commands.smoke_done({ result: `FAIL: ${cause}` });
    }
  }
}
