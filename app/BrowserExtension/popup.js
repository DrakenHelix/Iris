const byId = id => document.getElementById(id);
const english = !chrome.i18n.getUILanguage().toLowerCase().startsWith('de');
const text = (de, en) => english ? en : de;

document.documentElement.lang = text('de', 'en');
byId('subtitle').textContent = text('Download-Fortschritt', 'Download progress');
byId('connection-title').textContent = text('Lokale Verbindung', 'Local connection');
byId('connect').textContent = text('Verbinden', 'Connect');
byId('disconnect').textContent = text('Trennen', 'Disconnect');
byId('error-title').textContent = text('Fehlerdetails', 'Error details');
byId('privacy').textContent = text('Dateiname und Fortschritt aktiver Downloads bleiben auf diesem PC.',
  'Filenames and progress of active downloads stay on this PC.');

const states = {
  checking: [text('Verbindung wird geprüft …', 'Checking connection…'),
    text('Iris zeigt aktive Downloads in deiner Notch an.', 'Iris shows active downloads in your notch.')],
  idle: [text('Noch nicht verbunden', 'Not connected yet'),
    text('Richte die Verbindung einmal in Iris unter Einstellungen → Downloads ein.',
      'Set up the connection once in Iris under Settings → Downloads.')],
  connected: [text('Mit Iris verbunden', 'Connected to Iris'),
    text('Aktive Downloads erscheinen neben der Uhr in deiner Notch.', 'Active downloads appear next to the clock in your notch.')],
  disconnected: [text('Verbindung getrennt', 'Disconnected'),
    text('Du kannst Chrome jederzeit wieder mit Iris verbinden.', 'You can reconnect Chrome to Iris at any time.')],
  error: [text('Verbindung fehlgeschlagen', 'Connection failed'),
    text('Öffne Iris und prüfe die Verbindung unter Einstellungen → Downloads.',
      'Open Iris and check the connection under Settings → Downloads.')]
};

function show(state, detail = '') {
  byId('connection').dataset.state = state;
  const [title, hint] = states[state];
  byId('status').textContent = title;
  byId('hint').textContent = hint;
  byId('error').hidden = !detail;
  if (!detail) byId('error').open = false;
  byId('error-message').textContent = detail;
}

function render(status) {
  if (status === 'Mit Iris verbunden · nur aktive Downloads') show('connected');
  else if (status === 'Getrennt') show('disconnected');
  else if (status && status !== 'Noch nicht verbunden') {
    show('error', status.replace(/^(?:Nicht verbunden|Verbindung fehlgeschlagen):\s*/, ''));
  } else show('idle');
}

let revision = 0;
chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== 'local' || !changes.status) return;
  revision++;
  render(changes.status.newValue);
});

async function readStatus(expected) {
  try {
    const {status} = await chrome.storage.local.get('status');
    if (revision === expected) render(status);
  } catch (error) {
    if (revision === expected) show('error', error.message);
  }
}

show('checking');
readStatus(revision);

const controls = ['connect', 'disconnect'].map(byId);
for (const button of controls) button.addEventListener('click', async () => {
  const expected = ++revision;
  show('checking');
  for (const control of controls) control.disabled = true;
  try {
    const result = await chrome.runtime.sendMessage({action: button.id});
    if (!result?.ok) throw new Error(result?.error || text('Keine Antwort von der Erweiterung.', 'No response from the extension.'));
    if (revision === expected) await readStatus(expected);
  } catch (error) {
    if (revision === expected) show('error', error.message);
  } finally {
    for (const control of controls) control.disabled = false;
  }
});
