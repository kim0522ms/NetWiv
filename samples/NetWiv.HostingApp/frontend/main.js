import { commands } from './generated/commands.js';
import { runSmoke } from './smoke.js';

const result = document.querySelector('#result');
const show = async action => {
  try { result.textContent = JSON.stringify(await action(), null, 2); }
  catch (error) { result.textContent = String(error); }
};
document.querySelector('#greeting').addEventListener('submit', event => {
  event.preventDefault();
  void show(() => commands.greet({ name: new FormData(event.target).get('name') }));
});
document.querySelector('#status').addEventListener('click', () => void show(() => commands['plugin:equipment|get_status']()));
document.querySelector('#reset').addEventListener('click', () => void show(() => commands['plugin:equipment|reset']()));

if (new URLSearchParams(location.search).get('smoke') === '1') {
  try {
    await runSmoke(commands);
    result.textContent = 'PASS: scoped identities, disposal and permissions';
    await commands.smoke_done({ result: 'PASS' });
  } catch (error) {
    result.textContent = String(error);
    await commands.smoke_done({ result: String(error) });
  }
}
