import { commands } from './generated/commands.js';
import './style.css';
document.querySelector('#app').innerHTML = '<main><h1>NetWiv + Vanilla</h1><form><input aria-label="Name" value="World" required><button>Greet</button></form><output></output></main>';
const form = document.querySelector('form');
const output = document.querySelector('output');
form.addEventListener('submit', async event => {
  event.preventDefault();
  try {
    const result = await commands.greet({ name: form.querySelector('input').value });
    output.textContent = result.message + ' (#' + result.count + ')';
  } catch (error) { output.textContent = String(error); }
});
