import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { commands } from './generated/commands.js';
@Component({ selector: 'app-root', standalone: true, imports: [FormsModule], template: `
<main><h1>NetWiv + Angular</h1><form (ngSubmit)="greet()"><input name="name" aria-label="Name" [(ngModel)]="name" required><button>Greet</button></form><output>{{ message }}</output></main>` })
export class App {
  name = 'World';
  message = '';
  async greet() {
    try { const result = await commands.greet({ name: this.name }); this.message = result.message + ' (#' + result.count + ')'; }
    catch (error) { this.message = String(error); }
  }
}
